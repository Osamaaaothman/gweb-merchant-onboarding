using Gweb.Adapters.Persistence;
using Gweb.Domain.Applications;
using Gweb.Services.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Services.Applications;

public class ApplicationServiceCreateTests
{
    [Fact]
    public async Task CreatesAndPersistsANewInProgressApplication()
    {
        var repository = new InMemoryApplicationRepository();
        var clock = new FakeClock(1_700_000_000_000);
        var service = new ApplicationService(repository, clock);
        var budget = DeadlineBudget.Start(35_000, clock, targetMs: 35_000);

        var application = await service.CreateApplicationAsync("test-actor", "corr-1", budget);

        Assert.Equal(ApplicationStatus.InProgress, application.Status);
        var persisted = await repository.GetByIdAsync(application.Id, budget);
        Assert.NotNull(persisted);
        Assert.Equal(application.Id, persisted!.Id);
    }

    [Fact]
    public async Task GeneratesADifferentIdOnEachCall()
    {
        var repository = new InMemoryApplicationRepository();
        var clock = new FakeClock(0);
        var service = new ApplicationService(repository, clock);
        var budget = DeadlineBudget.Start(35_000, clock, targetMs: 35_000);

        var first = await service.CreateApplicationAsync("actor", "corr-1", budget);
        var second = await service.CreateApplicationAsync("actor", "corr-2", budget);

        Assert.NotEqual(first.Id, second.Id);
    }
}

public class ApplicationServiceGetTests
{
    [Fact]
    public async Task ReturnsThePreviouslyCreatedApplicationUnchanged()
    {
        var repository = new InMemoryApplicationRepository();
        var clock = new FakeClock(0);
        var service = new ApplicationService(repository, clock);
        var budget = DeadlineBudget.Start(35_000, clock, targetMs: 35_000);
        var created = await service.CreateApplicationAsync("actor", "corr-1", budget);

        var result = await service.GetApplicationAsync(created.Id, budget);

        Assert.Equal(created.Id, result.Id);
        Assert.Equal(created.Status, result.Status);
    }

    [Fact]
    public async Task ThrowsNotFoundExceptionForAnUnknownId()
    {
        var repository = new InMemoryApplicationRepository();
        var clock = new FakeClock(0);
        var service = new ApplicationService(repository, clock);
        var budget = DeadlineBudget.Start(35_000, clock, targetMs: 35_000);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetApplicationAsync(Guid.NewGuid(), budget));
    }
}
