using Gweb.Domain.Applications;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Domain.Applications;

public class ApplicationCreateTests
{
    [Fact]
    public void StartsInProgressAtVersionOne()
    {
        var now = DateTimeOffset.UtcNow;

        var application = Application.Create(Guid.NewGuid(), now, "test-actor", "corr-1");

        Assert.Equal(ApplicationStatus.InProgress, application.Status);
        Assert.Equal(1, application.Version);
        Assert.Equal(now, application.CreatedAt);
        Assert.Equal(now, application.UpdatedAt);
    }

    [Fact]
    public void CarriesTheGivenIdActorAndCorrelationId()
    {
        var id = Guid.NewGuid();

        var application = Application.Create(id, DateTimeOffset.UtcNow, "test-actor", "corr-1");

        Assert.Equal(id, application.Id);
        Assert.Equal("test-actor", application.CreatedBy);
        Assert.Equal("corr-1", application.CorrelationId);
    }
}

public class ApplicationSubmitTests
{
    [Fact]
    public void TransitionsFromInProgressToSubmittedAndBumpsVersion()
    {
        var application = Application.Create(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        var submitTime = DateTimeOffset.UtcNow.AddMinutes(5);

        application.Submit(submitTime);

        Assert.Equal(ApplicationStatus.Submitted, application.Status);
        Assert.Equal(2, application.Version);
        Assert.Equal(submitTime, application.UpdatedAt);
    }

    [Fact]
    public void RejectsSubmittingAnAlreadySubmittedApplication()
    {
        var application = Application.Create(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        application.Submit(DateTimeOffset.UtcNow);

        Assert.Throws<ConflictException>(() => application.Submit(DateTimeOffset.UtcNow));
    }
}

public class ApplicationRehydrateTests
{
    [Fact]
    public void ReconstructsExactlyTheGivenState()
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddDays(-1);
        var updatedAt = DateTimeOffset.UtcNow;

        var application = Application.Rehydrate(id, ApplicationStatus.Submitted, 3, createdAt, updatedAt, "actor", "corr-2");

        Assert.Equal(id, application.Id);
        Assert.Equal(ApplicationStatus.Submitted, application.Status);
        Assert.Equal(3, application.Version);
        Assert.Equal(createdAt, application.CreatedAt);
        Assert.Equal(updatedAt, application.UpdatedAt);
        Assert.Equal("corr-2", application.CorrelationId);
    }
}
