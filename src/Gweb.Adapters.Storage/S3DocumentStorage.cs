using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Gweb.Domain.Documents;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Storage;

/// <summary>
/// Real implementation of IDocumentStorage. Client uploads go straight to S3 via a
/// pre-signed POST -- Lambda never receives document bytes (docs/03-ARCHITECTURE-RULES.md
/// §3). The presigned POST pins Content-Type, a size range, and the client-declared
/// SHA-256 checksum as policy conditions, so S3 itself rejects an upload that doesn't
/// match before this system ever sees it -- complete()'s own verification is defense
/// in depth on top of that, not the only line of defense.
/// </summary>
public sealed class S3DocumentStorage(IAmazonS3 client, string bucketName) : IDocumentStorage
{
    private const int LeadingBytesToRead = 16;

    public async Task<PresignedUpload> CreatePresignedUploadAsync(
        string s3Key,
        string contentType,
        long maxSizeBytes,
        string checksumSha256Base64,
        TimeSpan ttl,
        DeadlineBudget budget,
        CancellationToken cancellationToken = default)
    {
        var request = new CreatePresignedPostRequest
        {
            BucketName = bucketName,
            Key = s3Key,
            Expires = DateTime.UtcNow.Add(ttl),
        };
        request.Conditions.Add(S3PostCondition.ExactMatch("Content-Type", contentType));
        request.Conditions.Add(S3PostCondition.ContentLengthRange(1, maxSizeBytes));
        request.Conditions.Add(S3PostCondition.ExactMatch("x-amz-checksum-sha256", checksumSha256Base64));
        request.Fields["Content-Type"] = contentType;
        request.Fields["x-amz-checksum-sha256"] = checksumSha256Base64;

        // Local signing only -- no network call, so this doesn't strictly need the
        // budget-derived timeout S3CallExecutor gives read/write calls. Still routed
        // through it for consistency and in case credential resolution ever blocks.
        var response = await S3CallExecutor.ExecuteAsync(
            _ => client.CreatePresignedPostAsync(request), budget, cancellationToken).ConfigureAwait(false);

        return new PresignedUpload(response.Url.ToString(), response.Fields);
    }

    public async Task<UploadedObject?> GetUploadedObjectAsync(string s3Key, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var metadata = await GetMetadataOrNullAsync(s3Key, budget, cancellationToken).ConfigureAwait(false);
        if (metadata is null)
        {
            return null;
        }

        var getRequest = new GetObjectRequest
        {
            BucketName = bucketName,
            Key = s3Key,
            ByteRange = new ByteRange(0, LeadingBytesToRead - 1),
        };
        using var getResponse = await S3CallExecutor.ExecuteAsync(
            ct => client.GetObjectAsync(getRequest, ct), budget, cancellationToken).ConfigureAwait(false);
        using var responseStream = getResponse.ResponseStream;
        using var buffer = new MemoryStream();
        await responseStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        return new UploadedObject(metadata.ContentLength, metadata.ChecksumSHA256, buffer.ToArray());
    }

    /// <summary>
    /// A 404 here (object not uploaded yet) is an expected, normal outcome, not a
    /// dependency failure -- handled directly rather than through S3CallExecutor,
    /// which deliberately strips status-code detail from what it forwards.
    /// </summary>
    private async Task<GetObjectMetadataResponse?> GetMetadataOrNullAsync(
        string s3Key, DeadlineBudget budget, CancellationToken cancellationToken)
    {
        // Reuse S3CallExecutor's timeout derivation, but call the client directly so
        // a 404 can be distinguished from a genuine dependency failure.
        var timeoutMs = budget.ForCall(500);
        if (timeoutMs <= 0)
        {
            throw new DependencyTimeoutException("Deadline budget exhausted before the S3 call could be attempted.");
        }
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        var request = new GetObjectMetadataRequest
        {
            BucketName = bucketName,
            Key = s3Key,
            ChecksumMode = ChecksumMode.ENABLED,
        };

        try
        {
            return await client.GetObjectMetadataAsync(request, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DependencyTimeoutException("S3 call exceeded its allotted timeout budget.");
        }
        catch (AmazonS3Exception)
        {
            throw new DependencyUnavailableException("S3 is temporarily unavailable.");
        }
    }
}
