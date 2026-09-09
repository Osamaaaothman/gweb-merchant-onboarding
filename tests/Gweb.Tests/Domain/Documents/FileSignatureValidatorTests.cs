using Gweb.Domain.Documents;

namespace Gweb.Tests.Domain.Documents;

public class FileSignatureValidatorTests
{
    [Fact]
    public void AcceptsARealPdfSignature()
    {
        byte[] bytes = "%PDF-1.7 rest of file..."u8.ToArray();

        Assert.True(FileSignatureValidator.Matches("application/pdf", bytes));
    }

    [Fact]
    public void RejectsAPdfContentTypeWithAJpegSignature()
    {
        byte[] bytes = [0xFF, 0xD8, 0xFF, 0xE0];

        Assert.False(FileSignatureValidator.Matches("application/pdf", bytes));
    }

    [Fact]
    public void AcceptsARealJpegSignature()
    {
        byte[] bytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

        Assert.True(FileSignatureValidator.Matches("image/jpeg", bytes));
    }

    [Fact]
    public void AcceptsARealPngSignature()
    {
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

        Assert.True(FileSignatureValidator.Matches("image/png", bytes));
    }

    [Fact]
    public void RejectsAnUnrecognizedContentType()
    {
        byte[] bytes = "whatever"u8.ToArray();

        Assert.False(FileSignatureValidator.Matches("application/octet-stream", bytes));
    }

    [Fact]
    public void RejectsBytesShorterThanTheSignature()
    {
        byte[] bytes = [0x89, 0x50];

        Assert.False(FileSignatureValidator.Matches("image/png", bytes));
    }

    [Fact]
    public void RejectsAPdfExtensionMasqueradingAnExecutable()
    {
        // The exact scenario docs/04-SECURITY-RULES.md §3 calls out: extension/MIME
        // are attacker-controlled, the actual bytes are what matter.
        byte[] windowsExecutableBytes = [0x4D, 0x5A, 0x90, 0x00];

        Assert.False(FileSignatureValidator.Matches("application/pdf", windowsExecutableBytes));
    }
}
