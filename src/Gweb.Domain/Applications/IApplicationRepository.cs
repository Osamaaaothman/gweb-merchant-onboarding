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

    /// <summary>
    /// Persists a status change to an existing application (so far, only
    /// Submit() -- Phase 9). Deliberately a separate method from CreateAsync rather
    /// than a unified "SaveAsync" with an expectedVersion=0-means-create sentinel
    /// (the convention every other repository in this codebase uses): Application
    /// already has its own dedicated creation path with its own semantics (version
    /// starts at 1, not 0), so folding update into it would mean two different
    /// "expectedVersion" meanings on the same method. Throws
    /// <see cref="Gweb.Shared.Errors.ConflictException"/> if <paramref name="expectedVersion"/>
    /// doesn't match what's actually stored.
    /// </summary>
    Task UpdateAsync(Application application, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default);
}
