using Gweb.Adapters.Persistence;
using Gweb.Domain.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Adapters.Persistence;

public class InMemoryApplicationRepositoryTests
{
    private static DeadlineBudget Budget() =>
        DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    [Fact]
    public async Task ReturnsNullWhenNoApplicationWithThatIdExists()
    {
        var repository = new InMemoryApplicationRepository();

        var result = await repository.GetByIdAsync(Guid.NewGuid(), Budget());

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateThenGetReturnsTheSameApplication()
    {
        var repository = new InMemoryApplicationRepository();
        var application = Application.Create(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");

        await repository.CreateAsync(application, Budget());
        var result = await repository.GetByIdAsync(application.Id, Budget());

        Assert.NotNull(result);
        Assert.Equal(application.Id, result!.Id);
        Assert.Equal(application.Status, result.Status);
    }

    [Fact]
    public async Task RejectsCreatingTwoApplicationsWithTheSameId()
    {
        // Proves the conditional-write guarantee even though IDs are server-generated
        // GUIDs and this can't happen in normal operation -- see IApplicationRepository.
        var repository = new InMemoryApplicationRepository();
        var id = Guid.NewGuid();
        var first = Application.Create(id, DateTimeOffset.UtcNow, "actor", "corr-1");
        var second = Application.Create(id, DateTimeOffset.UtcNow, "actor", "corr-2");

        await repository.CreateAsync(first, Budget());

        await Assert.ThrowsAsync<ConflictException>(() => repository.CreateAsync(second, Budget()));
    }
}
