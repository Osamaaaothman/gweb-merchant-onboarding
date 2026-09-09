using Gweb.Domain.Documents;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Domain.Documents;

public class DocumentCreateAndBeginUploadTests
{
    [Fact]
    public void StartsInUploadingStatusAtVersionOne()
    {
        var now = DateTimeOffset.UtcNow;

        var document = Document.CreateAndBeginUpload(
            Guid.NewGuid(), Guid.NewGuid(), DocumentType.GovernmentId, "id.pdf", "application/pdf", 1024,
            "applications/x/documents/y/z.pdf", "abc123==", now, "actor", "corr-1");

        Assert.Equal(DocumentStatus.Uploading, document.Status);
        Assert.Equal(1, document.Version);
    }
}

public class DocumentMarkReceivedTests
{
    private static Document Uploading() => Document.CreateAndBeginUpload(
        Guid.NewGuid(), Guid.NewGuid(), DocumentType.GovernmentId, "id.pdf", "application/pdf", 1024,
        "applications/x/documents/y/z.pdf", "abc123==", DateTimeOffset.UtcNow, "actor", "corr-1");

    [Fact]
    public void TransitionsFromUploadingToReceived()
    {
        var document = Uploading();

        document.MarkReceived("abc123==", 1024, DateTimeOffset.UtcNow);

        Assert.Equal(DocumentStatus.Received, document.Status);
        Assert.Equal("abc123==", document.ActualChecksumSha256);
        Assert.Equal(2, document.Version);
    }

    [Fact]
    public void IsIdempotentWhenCalledTwiceWithTheSameChecksum()
    {
        var document = Uploading();
        var now = DateTimeOffset.UtcNow;
        document.MarkReceived("abc123==", 1024, now);
        var versionAfterFirstCall = document.Version;

        document.MarkReceived("abc123==", 1024, now.AddSeconds(1));

        Assert.Equal(DocumentStatus.Received, document.Status);
        Assert.Equal(versionAfterFirstCall, document.Version);
    }

    [Fact]
    public void RejectsASecondCallWithADifferentChecksum()
    {
        var document = Uploading();
        document.MarkReceived("abc123==", 1024, DateTimeOffset.UtcNow);

        Assert.Throws<ConflictException>(() => document.MarkReceived("different==", 1024, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RejectsMarkingReceivedFromRequestedDirectly()
    {
        // Illegal transition: REQUESTED must go through UPLOADING first.
        var document = Uploading();
        document.MarkRejected("bad signature", DateTimeOffset.UtcNow);

        Assert.Throws<ConflictException>(() => document.MarkReceived("abc123==", 1024, DateTimeOffset.UtcNow));
    }
}

public class DocumentMarkRejectedTests
{
    private static Document Uploading() => Document.CreateAndBeginUpload(
        Guid.NewGuid(), Guid.NewGuid(), DocumentType.GovernmentId, "id.pdf", "application/pdf", 1024,
        "applications/x/documents/y/z.pdf", "abc123==", DateTimeOffset.UtcNow, "actor", "corr-1");

    [Fact]
    public void TransitionsFromUploadingToRejectedWithAReason()
    {
        var document = Uploading();

        document.MarkRejected("checksum mismatch", DateTimeOffset.UtcNow);

        Assert.Equal(DocumentStatus.Rejected, document.Status);
        Assert.Equal("checksum mismatch", document.RejectionReason);
    }

    [Fact]
    public void RejectsRejectingAnAlreadyAcceptedDocument()
    {
        var document = Uploading();
        document.MarkReceived("abc123==", 1024, DateTimeOffset.UtcNow);
        document.MarkAccepted(DateTimeOffset.UtcNow);

        Assert.Throws<ConflictException>(() => document.MarkRejected("late objection", DateTimeOffset.UtcNow));
    }
}

public class DocumentMarkAcceptedAndNeedsReviewTests
{
    [Fact]
    public void AcceptsFromReceived()
    {
        var document = Document.CreateAndBeginUpload(
            Guid.NewGuid(), Guid.NewGuid(), DocumentType.GovernmentId, "id.pdf", "application/pdf", 1024,
            "applications/x/documents/y/z.pdf", "abc123==", DateTimeOffset.UtcNow, "actor", "corr-1");
        document.MarkReceived("abc123==", 1024, DateTimeOffset.UtcNow);

        document.MarkAccepted(DateTimeOffset.UtcNow);

        Assert.Equal(DocumentStatus.Accepted, document.Status);
    }

    [Fact]
    public void RejectsAcceptingAFreshlyCreatedUploadingDocument()
    {
        var document = Document.CreateAndBeginUpload(
            Guid.NewGuid(), Guid.NewGuid(), DocumentType.GovernmentId, "id.pdf", "application/pdf", 1024,
            "applications/x/documents/y/z.pdf", "abc123==", DateTimeOffset.UtcNow, "actor", "corr-1");

        Assert.Throws<ConflictException>(() => document.MarkAccepted(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void FlagsForReviewFromReceived()
    {
        var document = Document.CreateAndBeginUpload(
            Guid.NewGuid(), Guid.NewGuid(), DocumentType.GovernmentId, "id.pdf", "application/pdf", 1024,
            "applications/x/documents/y/z.pdf", "abc123==", DateTimeOffset.UtcNow, "actor", "corr-1");
        document.MarkReceived("abc123==", 1024, DateTimeOffset.UtcNow);

        document.MarkNeedsReview(DateTimeOffset.UtcNow);

        Assert.Equal(DocumentStatus.NeedsReview, document.Status);
    }
}
