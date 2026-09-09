using Gweb.Domain.Mcc;
using Gweb.Services.Mcc;

namespace Gweb.Tests.Services.Mcc;

public class McCatalogServiceTests
{
    private sealed class FakeCatalog : IMccCatalog
    {
        public string? LastQuery;
        public int LastLimit;

        public IReadOnlyList<MccCode> Search(string? query, int limit)
        {
            LastQuery = query;
            LastLimit = limit;
            return [new MccCode("1234", "Fake Entry", "Test")];
        }

        public MccCode? GetByCode(string code) => code == "1234" ? new MccCode("1234", "Fake Entry", "Test") : null;
    }

    [Fact]
    public void DefaultsToTwentyFiveWhenNoLimitIsGiven()
    {
        var fake = new FakeCatalog();
        var service = new McCatalogService(fake);

        service.Search("query", limit: null);

        Assert.Equal(25, fake.LastLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void TreatsANonPositiveLimitAsTheDefault(int requestedLimit)
    {
        var fake = new FakeCatalog();
        var service = new McCatalogService(fake);

        service.Search("query", requestedLimit);

        Assert.Equal(25, fake.LastLimit);
    }

    [Fact]
    public void CapsAnOverlyLargeLimitAtOneHundred()
    {
        var fake = new FakeCatalog();
        var service = new McCatalogService(fake);

        service.Search("query", limit: 5000);

        Assert.Equal(100, fake.LastLimit);
    }

    [Fact]
    public void PassesAReasonableLimitThroughUnchanged()
    {
        var fake = new FakeCatalog();
        var service = new McCatalogService(fake);

        service.Search("query", limit: 10);

        Assert.Equal(10, fake.LastLimit);
    }

    [Fact]
    public void GetByCodeDelegatesToTheCatalog()
    {
        var service = new McCatalogService(new FakeCatalog());

        var result = service.GetByCode("1234");

        Assert.NotNull(result);
        Assert.Equal("Fake Entry", result!.Description);
    }
}
