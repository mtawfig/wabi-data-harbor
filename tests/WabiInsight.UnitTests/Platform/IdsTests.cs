using WabiInsight.Core.Common;

namespace WabiInsight.UnitTests.Platform;

public class IdsTests
{
    [Fact]
    public void New_ProducesVersion7Guids()
    {
        var id = Ids.New();

        Assert.Equal(7, id.Version);
    }

    [Fact]
    public void New_IsUniqueAndTimeOrdered()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => Ids.New()).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        var first = ids[0].ToString("N")[..12];
        var last = ids[^1].ToString("N")[..12];
        Assert.True(string.CompareOrdinal(first, last) <= 0, "UUID v7 timestamp prefix must not decrease.");
    }
}
