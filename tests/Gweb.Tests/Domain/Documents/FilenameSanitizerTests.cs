using Gweb.Domain.Documents;

namespace Gweb.Tests.Domain.Documents;

public class FilenameSanitizerTests
{
    [Fact]
    public void PassesThroughAnOrdinaryFilenameUnchanged()
    {
        Assert.Equal("passport.pdf", FilenameSanitizer.Sanitize("passport.pdf"));
    }

    [Fact]
    public void StripsAPathTraversalPrefix()
    {
        Assert.Equal("passport.pdf", FilenameSanitizer.Sanitize("../../etc/passport.pdf"));
    }

    [Fact]
    public void StripsAWindowsStylePathPrefix()
    {
        Assert.Equal("passport.pdf", FilenameSanitizer.Sanitize(@"C:\Users\jane\Documents\passport.pdf"));
    }

    [Fact]
    public void RemovesControlCharacters()
    {
        var result = FilenameSanitizer.Sanitize("passport\0.pdf");

        Assert.DoesNotContain('\0', result);
    }

    [Fact]
    public void TruncatesAnExcessivelyLongFilename()
    {
        var longName = new string('a', 500) + ".pdf";

        var result = FilenameSanitizer.Sanitize(longName);

        Assert.True(result.Length <= 200);
    }

    [Fact]
    public void FallsBackToAPlaceholderForAnEmptyResult()
    {
        Assert.Equal("document", FilenameSanitizer.Sanitize("   "));
    }
}
