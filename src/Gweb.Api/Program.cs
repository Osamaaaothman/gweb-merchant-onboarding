using Amazon.DynamoDBv2;
using Gweb.Adapters.Persistence;
using Gweb.Api;
using Gweb.Api.Applications;
using Gweb.Config;
using Gweb.Domain.Applications;
using Gweb.Services.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Logging;

var builder = WebApplication.CreateBuilder(args);

// Hosts this whole app as one Lambda function behind API Gateway HTTP API. See
// docs/adr/0001-runtime-and-language-choice.md for why this is one Lambda for the
// whole API rather than one-per-route (the architecture doc's stated preference) --
// it is the idiomatic way to run ASP.NET Core on Lambda, and the tradeoff is recorded
// there, not silently accepted.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

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
}

builder.Services.AddSingleton<ApplicationService>();

var app = builder.Build();

app.MapGet("/v1/health", HealthEndpoint.GetHealthAsync);
app.MapApplicationEndpoints();

app.Run();

// Exposes the implicit top-level-statements Program class to WebApplicationFactory<Program>
// in the integration tests.
public partial class Program;
