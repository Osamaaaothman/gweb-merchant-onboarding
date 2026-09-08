using Gweb.Shared.Deadline;

namespace Gweb.Domain.Applications;

/// <summary>
/// Interface lives in Domain; DynamoDB and in-memory implementations live in
/// adapters/. Every method that does I/O takes the deadline budget explicitly.
/// </summary>
public interface IApplicationRepository
{
    /// <summary>
    /// Persists a brand-new application. Throws <see cref="Gweb.Shared.Errors.ConflictException"/>
    /// if an application with the same ID already exists (a conditional write, not a
    /// blind overwrite) -- in normal operation this can't happen since IDs are
    /// server-generated GUIDs, but the guarantee is proven by test, not assumed.
    /// </summary>
    Task CreateAsync(Application application, DeadlineBudget budget, CancellationToken cancellationToken = default);

    /// <summary>Returns null, never throws, when no application with that ID exists.</summary>
    Task<Application?> GetByIdAsync(Guid id, DeadlineBudget budget, CancellationToken cancellationToken = default);
}
