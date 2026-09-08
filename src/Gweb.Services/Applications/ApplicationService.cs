using Gweb.Domain.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Services.Applications;

/// <summary>
/// Use-case orchestration for the application lifecycle. Knows nothing about HTTP or
/// AWS SDK types -- handlers translate requests into calls here, this translates
/// domain results back for the handler to map to a response.
/// </summary>
public sealed class ApplicationService(IApplicationRepository repository, IClock clock)
{
    public async Task<Application> CreateApplicationAsync(
        string createdBy,
        string correlationId,
        DeadlineBudget budget,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.NowMs());
        var application = Application.Create(Guid.NewGuid(), now, createdBy, correlationId);

        await repository.CreateAsync(application, budget, cancellationToken).ConfigureAwait(false);

        return application;
    }

    public async Task<Application> GetApplicationAsync(
        Guid id,
        DeadlineBudget budget,
        CancellationToken cancellationToken = default)
    {
        var application = await repository.GetByIdAsync(id, budget, cancellationToken).ConfigureAwait(false);

        return application ?? throw new NotFoundException($"Application {id} not found.");
    }
}
