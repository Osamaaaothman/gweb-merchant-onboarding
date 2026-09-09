using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Gweb.Adapters.Storage;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Moq;

namespace Gweb.Tests.Adapters.Storage;

public class S3DocumentStorageTests
{
    private const string BucketName = "test-bucket";

    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    [Fact]
    public async Task CreatePresignedUploadPinsContentTypeSizeRangeAndChecksumAsConditions()
    {
        var mockClient = new Mock<IAmazonS3>();
        mockClient
            .Setup(c => c.CreatePresignedPostAsync(It.IsAny<CreatePresignedPostRequest>()))
            .ReturnsAsync(new CreatePresignedPostResponse { Url = "https://bucket.s3.amazonaws.com/", Fields = [] });
        var storage = new S3DocumentStorage(mockClient.Object, BucketName);

        await storage.CreatePresignedUploadAsync("applications/x/documents/y/z.pdf", "application/pdf", 5_000_000, "ZGVjbGFyZWQ=", TimeSpan.FromMinutes(5), Budget());

        mockClient.Verify(c => c.CreatePresignedPostAsync(It.Is<CreatePresignedPostRequest>(r =>
            r.BucketName == BucketName &&
            r.Key == "applications/x/documents/y/z.pdf" &&
            r.Conditions.Count == 3)), Times.Once);
    }

    [Fact]
    public async Task GetUploadedObjectReturnsNullWhenTheObjectDoesNotExist()
    {
        var mockClient = new Mock<IAmazonS3>();
        mockClient
            .Setup(c => c.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound });
        var storage = new S3DocumentStorage(mockClient.Object, BucketName);

        var result = await storage.GetUploadedObjectAsync("applications/x/documents/y/z.pdf", Budget());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetUploadedObjectReturnsSizeChecksumAndLeadingBytesWhenTheObjectExists()
    {
        var mockClient = new Mock<IAmazonS3>();
        mockClient
            .Setup(c => c.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse { ContentLength = 2048, ChecksumSHA256 = "ZGVjbGFyZWQ=" });
        mockClient
            .Setup(c => c.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = new MemoryStream("%PDF-1.7 rest"u8.ToArray()) });
        var storage = new S3DocumentStorage(mockClient.Object, BucketName);

        var result = await storage.GetUploadedObjectAsync("applications/x/documents/y/z.pdf", Budget());

        Assert.NotNull(result);
        Assert.Equal(2048, result!.SizeBytes);
        Assert.Equal("ZGVjbGFyZWQ=", result.ChecksumSha256Base64);
        Assert.Equal("%PDF"u8.ToArray(), result.LeadingBytes[..4]);
    }

    [Fact]
    public async Task RequestsARangedReadRatherThanTheWholeObjectForTheSignatureCheck()
    {
        var mockClient = new Mock<IAmazonS3>();
        mockClient
            .Setup(c => c.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse { ContentLength = 5_000_000 });
        mockClient
            .Setup(c => c.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = new MemoryStream(new byte[16]) });
        var storage = new S3DocumentStorage(mockClient.Object, BucketName);

        await storage.GetUploadedObjectAsync("applications/x/documents/y/z.pdf", Budget());

        mockClient.Verify(c => c.GetObjectAsync(It.Is<GetObjectRequest>(r => r.ByteRange != null), It.IsAny<CancellationToken>()), Times.Once);
    }
}
