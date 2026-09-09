using Gweb.Domain.Documents;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Domain.Documents;

public class DocumentKeyGeneratorTests
{
    [Fact]
    public void ProducesTheDocumentedKeyShape()
    {
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var key = DocumentKeyGenerator.Generate(applicationId, documentId, "application/pdf");

        Assert.StartsWith($"applications/{applicationId}/documents/{documentId}/", key);
        Assert.EndsWith(".pdf", key);
    }

    [Fact]
    public void ProducesADifferentKeyOnEachCallEvenForTheSameDocument()
    {
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var first = DocumentKeyGenerator.Generate(applicationId, documentId, "application/pdf");
        var second = DocumentKeyGenerator.Generate(applicationId, documentId, "application/pdf");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void RejectsAnUnsupportedContentType()
    {
        Assert.Throws<ValidationException>(() =>
            DocumentKeyGenerator.Generate(Guid.NewGuid(), Guid.NewGuid(), "application/octet-stream"));
    }

    [Theory]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    public void UsesTheCorrectExtensionPerContentType(string contentType, string expectedExtension)
    {
        var key = DocumentKeyGenerator.Generate(Guid.NewGuid(), Guid.NewGuid(), contentType);

        Assert.EndsWith(expectedExtension, key);
    }
}
