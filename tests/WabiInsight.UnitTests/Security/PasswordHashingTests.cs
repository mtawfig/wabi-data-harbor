using WabiInsight.Core.Security;
using WabiInsight.Infrastructure.Security;

namespace WabiInsight.UnitTests.Security;

public class PasswordHashingTests
{
    [Fact]
    public void Hash_IsSalted_AndVerifies()
    {
        var hasher = new IdentityPasswordHashingService();

        var first = hasher.Hash("correct horse battery staple");
        var second = hasher.Hash("correct horse battery staple");

        Assert.NotEqual(first, second);
        Assert.Equal(PasswordVerification.Success, hasher.Verify(first, "correct horse battery staple"));
        Assert.Equal(PasswordVerification.Failed, hasher.Verify(first, "Correct horse battery staple"));
        Assert.Equal(PasswordVerification.Failed, hasher.Verify("", "x"));
    }

    [Fact]
    public void TemporaryPassword_IsRandom_AndLongEnough()
    {
        var a = TemporaryPasswordGenerator.Generate();
        var b = TemporaryPasswordGenerator.Generate();

        Assert.Equal(20, a.Length);
        Assert.NotEqual(a, b);
        Assert.Equal(40, TemporaryPasswordGenerator.NewSecurityStamp().Length);
    }
}
