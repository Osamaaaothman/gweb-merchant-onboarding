using Gweb.Adapters.Mcc;

namespace Gweb.Tests.Adapters.Mcc;

public class StaticMccCatalogTests
{
    private static StaticMccCatalog Catalog() => new();

    [Theory]
    [InlineData("6012")]
    [InlineData("6051")]
    [InlineData("6211")]
    public void ContainsEveryMccThePhase6RiskPolicyMustBeAbleToClassify(string code)
    {
        // Phase 6's risk policy demonstrates enhanced review for exactly these three
        // codes (docs/08-IMPLEMENTATION-PLAN.md Phase 6) -- if the catalog doesn't
        // carry them, that phase has nothing real to classify.
        var result = Catalog().GetByCode(code);

        Assert.NotNull(result);
        Assert.Equal(code, result!.Code);
    }

    [Fact]
    public void GetByCodeReturnsNullForAnUnknownCode()
    {
        var result = Catalog().GetByCode("0000");

        Assert.Null(result);
    }

    [Fact]
    public void SearchWithNoQueryReturnsTheFirstCodesOrderedByCode()
    {
        var results = Catalog().Search(query: null, limit: 5);

        Assert.Equal(5, results.Count);
        Assert.Equal(results.OrderBy(c => c.Code, StringComparer.Ordinal).Select(c => c.Code), results.Select(c => c.Code));
    }

    [Fact]
    public void SearchByExactCodeRanksItFirst()
    {
        var results = Catalog().Search(query: "6012", limit: 10);

        Assert.Equal("6012", results[0].Code);
    }

    [Fact]
    public void SearchByCodePrefixRanksAllMatchingCodesBeforeDescriptionOnlyMatches()
    {
        var results = Catalog().Search(query: "60", limit: 20);

        var codePrefixMatches = results.Where(c => c.Code.StartsWith("60", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(codePrefixMatches);
        var firstNonPrefixIndex = results.ToList().FindIndex(c => !c.Code.StartsWith("60", StringComparison.Ordinal));
        if (firstNonPrefixIndex >= 0)
        {
            Assert.All(results.Take(firstNonPrefixIndex), c => Assert.StartsWith("60", c.Code, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void SearchByDescriptionSubstringIsCaseInsensitive()
    {
        // 7995's description is "Betting, including Lottery Tickets, Casino Gaming,
        // and Wagers" -- searching its actual wording, not the Category grouping
        // ("High-Risk / Gambling"), since Search only matches Code and Description.
        var results = Catalog().Search(query: "CASINO", limit: 10);

        Assert.Contains(results, c => c.Code == "7995");
    }

    [Fact]
    public void SearchRespectsTheLimit()
    {
        var results = Catalog().Search(query: null, limit: 3);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void SearchWithNonPositiveLimitReturnsNothing()
    {
        var results = Catalog().Search(query: "grocery", limit: 0);

        Assert.Empty(results);
    }

    [Fact]
    public void SearchWithNoMatchesReturnsAnEmptyList()
    {
        var results = Catalog().Search(query: "zzz-not-a-real-mcc-query-zzz", limit: 10);

        Assert.Empty(results);
    }

    [Fact]
    public void EveryCodeIsUniqueAndFourDigits()
    {
        var all = Catalog().Search(query: null, limit: 1000);

        Assert.Equal(all.Count, all.Select(c => c.Code).Distinct().Count());
        Assert.All(all, c => Assert.Equal(4, c.Code.Length));
        Assert.All(all, c => Assert.True(c.Code.All(char.IsAsciiDigit)));
    }
}
