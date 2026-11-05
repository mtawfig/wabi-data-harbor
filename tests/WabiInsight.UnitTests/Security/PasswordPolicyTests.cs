using WabiInsight.Core.Security;

namespace WabiInsight.UnitTests.Security;

public class PasswordPolicyTests
{
    private static readonly SecurityOptions Options = new();

    [Fact]
    public void AcceptsLongPassword_WithoutCompositionRules()
    {
        Assert.Empty(PasswordPolicy.Validate("correct horse battery staple", "a@b.test", Options, allowSamplePassword: false));
    }

    [Theory]
    [InlineData("short", "password_too_short")]
    [InlineData("", "password_required")]
    [InlineData("a@b.test", "password_equals_email")]
    [InlineData(PasswordPolicy.SamplePassword, "password_is_sample")]
    public void RejectsPolicyViolations(string password, string expected)
    {
        Assert.Contains(expected, PasswordPolicy.Validate(password, "a@b.test", Options, allowSamplePassword: false));
    }

    [Fact]
    public void SamplePassword_IsAllowed_OnlyWhenExplicitlyEnabled()
    {
        Assert.Empty(PasswordPolicy.Validate(PasswordPolicy.SamplePassword, "a@b.test", Options, allowSamplePassword: true));
    }

    [Fact]
    public void RejectsOverlongPassword()
    {
        Assert.Contains("password_too_long", PasswordPolicy.Validate(new string('x', 129), null, Options, allowSamplePassword: false));
    }
}
