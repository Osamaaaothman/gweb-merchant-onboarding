using System.Collections.Concurrent;
using Gweb.Domain.Documents;
using Gweb.Shared.Deadline;

namespace Gweb.Adapters.Storage;

/// <summary>
/// Test/local-dev implementation of IDocumentStorage. Simulates "no upload has
/// happened yet" (GetUploadedObjectAsync returns null) until SimulateUpload is called
/// -- tests drive that explicitly instead of this adapter guessing when an upload
/// "would" have completed.
/// </summary>
public sealed class InMemoryDocumentStorage : IDocumentStorage
{
    private readonly ConcurrentDictionary<string, UploadedObject> _uploadedObjects = new();

    public Task<PresignedUpload> CreatePresignedUploadAsync(
        string s3Key,
        string contentType,
        long maxSizeBytes,
        string checksumSha256Base64,
        TimeSpan ttl,
        DeadlineBudget budget,
        CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["key"] = s3Key,
            ["Content-Type"] = contentType,
            ["x-amz-checksum-sha256"] = checksumSha256Base64,
        };
        return Task.FromResult(new PresignedUpload($"https://in-memory-storage.invalid/{s3Key}", fields));
    }

    public Task<UploadedObject?> GetUploadedObjectAsync(string s3Key, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        _uploadedObjects.TryGetValue(s3Key, out var uploaded);
        return Task.FromResult(uploaded);
    }

    /// <summary>Test-only: simulates a client having actually uploaded bytes to this key.</summary>
    public void SimulateUpload(string s3Key, UploadedObject uploadedObject) => _uploadedObjects[s3Key] = uploadedObject;
}
