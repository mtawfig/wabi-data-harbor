using WabiInsight.Core.Security;

namespace WabiInsight.UnitTests.Security;

public class EmailNormalizerTests
{
    [Fact]
    public void Normalize_TrimsAndUppercases()
    {
        Assert.Equal("ALICE@ORG-A.TEST", EmailNormalizer.Normalize("  alice@Org-A.test "));
    }

    [Theory]
    [InlineData("alice@org-a.test", true)]
    [InlineData("alice", false)]
    [InlineData("@org.test", false)]
    [InlineData("alice@", false)]
    [InlineData("a@b@c", false)]
    [InlineData("a b@c.d", false)]
    [InlineData("", false)]
    public void IsPlausible_ChecksShape(string email, bool expected)
    {
        Assert.Equal(expected, EmailNormalizer.IsPlausible(email));
    }
}
