using System.Text.Json.Serialization;
using Amazon.DynamoDBv2;
using Amazon.S3;
using Gweb.Adapters.Evaluation;
using Gweb.Adapters.Mcc;
using Gweb.Adapters.Persistence;
using Gweb.Adapters.RiskPolicy;
using Gweb.Adapters.Storage;
using Gweb.Api;
using Gweb.Api.Applications;
using Gweb.Api.Documents;
using Gweb.Api.Evaluation;
using Gweb.Api.Mcc;
using Gweb.Config;
using Gweb.Domain.Applications;
using Gweb.Domain.Documents;
using Gweb.Domain.Evaluation;
using Gweb.Domain.Mcc;
using Gweb.Domain.RiskPolicy;
using Gweb.Services.Applications;
using Gweb.Services.Documents;
using Gweb.Services.Evaluation;
using Gweb.Services.Mcc;
using Gweb.Shared.Clock;
using Gweb.Shared.Logging;

var builder = WebApplication.CreateBuilder(args);

// Hosts this whole app as one Lambda function behind API Gateway HTTP API. See
// docs/adr/0001-runtime-and-language-choice.md for why this is one Lambda for the
// whole API rather than one-per-route (the architecture doc's stated preference) --
// it is the idiomatic way to run ASP.NET Core on Lambda, and the tradeoff is recorded
// there, not silently accepted.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

// Enums (EntityType, GovernmentIdentificationType, ...) serialize/deserialize as
// their string names ("Llc", "Passport") both directions -- friendlier for API
// consumers than the numeric default, and matches every example in the README.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Loaded and validated once at process start (= Lambda cold start on a fresh
// execution environment), not per request.
var config = AppConfigLoader.LoadBaseConfig(Environment.GetEnvironmentVariable);
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton(new StructuredLogger(Console.Out, config.LogLevel));

// PERSISTENCE_PROVIDER=inmemory is for pure local dev without an AWS account; every
// deployed environment uses dynamodb (the default), reading the table name IaC
// injects. Swapping the adapter is a config change, never a code change -- see
// docs/03-ARCHITECTURE-RULES.md §5.
var persistenceProvider = Environment.GetEnvironmentVariable("PERSISTENCE_PROVIDER") ?? "dynamodb";
if (string.Equals(persistenceProvider, "inmemory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IApplicationRepository, InMemoryApplicationRepository>();
    builder.Services.AddSingleton<IApplicantRepository, InMemoryApplicantRepository>();
    builder.Services.AddSingleton<IBusinessRepository, InMemoryBusinessRepository>();
    builder.Services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
    builder.Services.AddSingleton<IDocumentStorage, InMemoryDocumentStorage>();
    builder.Services.AddSingleton<IMcClassificationRepository, InMemoryMcClassificationRepository>();
}
else
{
    var applicationsTableName = AppConfigLoader.RequireEnv("APPLICATIONS_TABLE_NAME", Environment.GetEnvironmentVariable);
    // DYNAMODB_SERVICE_URL is unset in every deployed environment -- it exists only
    // so local development/testing can point the real repository at DynamoDB Local
    // instead of a mock, without any code change (docs/03-ARCHITECTURE-RULES.md §5).
    var dynamoDbServiceUrl = Environment.GetEnvironmentVariable("DYNAMODB_SERVICE_URL");
    var dynamoDbConfig = new AmazonDynamoDBConfig();
    if (!string.IsNullOrWhiteSpace(dynamoDbServiceUrl))
    {
        dynamoDbConfig.ServiceURL = dynamoDbServiceUrl;
    }
    builder.Services.AddSingleton<IAmazonDynamoDB>(new AmazonDynamoDBClient(dynamoDbConfig));
    builder.Services.AddSingleton<IApplicationRepository>(
        sp => new DynamoDbApplicationRepository(sp.GetRequiredService<IAmazonDynamoDB>(), applicationsTableName));
    builder.Services.AddSingleton<IApplicantRepository>(
        sp => new DynamoDbApplicantRepository(sp.GetRequiredService<IAmazonDynamoDB>(), applicationsTableName));
    builder.Services.AddSingleton<IBusinessRepository>(
        sp => new DynamoDbBusinessRepository(sp.GetRequiredService<IAmazonDynamoDB>(), applicationsTableName));
    builder.Services.AddSingleton<IDocumentRepository>(
        sp => new DynamoDbDocumentRepository(sp.GetRequiredService<IAmazonDynamoDB>(), applicationsTableName));
    builder.Services.AddSingleton<IMcClassificationRepository>(
        sp => new DynamoDbMcClassificationRepository(sp.GetRequiredService<IAmazonDynamoDB>(), applicationsTableName));

    var documentsBucketName = AppConfigLoader.RequireEnv("DOCUMENTS_BUCKET_NAME", Environment.GetEnvironmentVariable);
    // S3_SERVICE_URL mirrors DYNAMODB_SERVICE_URL -- unset in every deployed
    // environment, only used to point at a local S3-compatible endpoint if one is
    // ever wired up for local testing.
    var s3ServiceUrl = Environment.GetEnvironmentVariable("S3_SERVICE_URL");
    var s3Config = new AmazonS3Config();
    if (!string.IsNullOrWhiteSpace(s3ServiceUrl))
    {
        s3Config.ServiceURL = s3ServiceUrl;
        s3Config.ForcePathStyle = true;
    }
    builder.Services.AddSingleton<IAmazonS3>(new AmazonS3Client(s3Config));
    builder.Services.AddSingleton<IDocumentStorage>(
        sp => new S3DocumentStorage(sp.GetRequiredService<IAmazonS3>(), documentsBucketName));
}

// Packaged static data (see docs/adr/0004-mcc-catalog-storage.md) -- not gated by
// PERSISTENCE_PROVIDER at all, since it involves no AWS resource in either branch.
builder.Services.AddSingleton<IMccCatalog, StaticMccCatalog>();
builder.Services.AddSingleton<McCatalogService>();

// Same reasoning as the MCC catalog (docs/adr/0005-risk-policy-representation.md):
// packaged config, no AWS resource, not gated by PERSISTENCE_PROVIDER. No HTTP
// endpoint yet either -- the brief's API surface has no dedicated risk-policy route;
// this is consumed internally once classify/evaluate (Phase 7/8) exist.
builder.Services.AddSingleton<IRiskPolicy, StaticRiskPolicy>();

// MockEvaluationProvider is always registered as a concrete singleton, regardless of
// AI_PROVIDER -- ClassificationService uses it as the guaranteed-available fallback
// when the real provider fails (see docs/adr/0006-ai-evaluation-provider.md).
builder.Services.AddSingleton<MockEvaluationProvider>();

if (config.AiProvider == AiProvider.Gemini)
{
    var geminiApiKey = AppConfigLoader.RequireEnv("AI_API_KEY", Environment.GetEnvironmentVariable);
    // A named (not typed) HttpClient -- GeminiEvaluationProvider's constructor also
    // needs the API key and model name, which aren't DI-resolvable types, so it's
    // constructed explicitly below rather than left to AddHttpClient<T>'s own
    // constructor injection.
    builder.Services.AddHttpClient(nameof(GeminiEvaluationProvider), client =>
    {
        client.BaseAddress = new Uri("https://generativelanguage.googleapis.com");
    });
    builder.Services.AddSingleton<IEvaluationProvider>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GeminiEvaluationProvider));
        return new GeminiEvaluationProvider(httpClient, geminiApiKey, config.GeminiModel);
    });
}
else
{
    builder.Services.AddSingleton<IEvaluationProvider>(sp => sp.GetRequiredService<MockEvaluationProvider>());
}

builder.Services.AddSingleton(sp => new ClassificationService(
    sp.GetRequiredService<IMcClassificationRepository>(),
    sp.GetRequiredService<IBusinessRepository>(),
    sp.GetRequiredService<IMccCatalog>(),
    sp.GetRequiredService<IEvaluationProvider>(),
    sp.GetRequiredService<MockEvaluationProvider>(),
    sp.GetRequiredService<IClock>(),
    sp.GetRequiredService<StructuredLogger>()));

builder.Services.AddSingleton<ApplicationService>();
builder.Services.AddSingleton<ApplicantService>();
builder.Services.AddSingleton<BusinessService>();
builder.Services.AddSingleton(sp => new DocumentService(
    sp.GetRequiredService<IDocumentRepository>(),
    sp.GetRequiredService<IDocumentStorage>(),
    sp.GetRequiredService<IClock>(),
    TimeSpan.FromSeconds(config.PresignTtlSeconds)));

var app = builder.Build();

app.MapGet("/v1/health", HealthEndpoint.GetHealthAsync);
app.MapApplicationEndpoints();
app.MapDocumentEndpoints();
app.MapMcEndpoints();
app.MapClassificationEndpoints();

app.Run();

// Exposes the implicit top-level-statements Program class to WebApplicationFactory<Program>
// in the integration tests.
public partial class Program;
