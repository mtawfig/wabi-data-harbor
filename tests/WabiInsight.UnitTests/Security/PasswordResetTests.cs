using WabiInsight.Core.Email;
using WabiInsight.Core.Security;
using WabiInsight.Infrastructure.Authentication;
using WabiInsight.Infrastructure.Email;

namespace WabiInsight.UnitTests.Security;

/// <summary>P13.32: the pieces of the self-service password reset that need no database.</summary>
public sealed class PasswordResetTests
{
    private const string Link = "https://insight.example.com/reset-password#token=abc-DEF_123";

    [Fact]
    public void Tokens_AreLongRandomAndSafeInALink_AndOnlyTheirHashIsComparable()
    {
        var tokens = Enumerable.Range(0, 500).Select(_ => PasswordResetService.NewToken()).ToList();
        Assert.All(tokens, token => Assert.Matches("^[A-Za-z0-9_-]{43}$", token));
        Assert.Equal(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count());

        var hash = PasswordResetService.Hash(tokens[0]);
        Assert.Matches("^[0-9A-F]{64}$", hash);
        Assert.Equal(hash, PasswordResetService.Hash(tokens[0]));
        Assert.NotEqual(hash, PasswordResetService.Hash(tokens[1]));
        Assert.DoesNotContain(tokens[0], hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Defaults_AreThirtyMinutes_ThreeLinksAnHour()
    {
        var options = new SecurityOptions();
        Assert.Equal(30, options.PasswordResetLinkMinutes);
        Assert.Equal(3, options.PasswordResetRequestsPerHour);
        Assert.Equal(10, options.PasswordResetRateLimitPerMinute);
    }

    [Theory]
    [InlineData("en", "ltr", "Choose a new password", "30 minutes")]
    [InlineData("ar", "rtl", "اختيار كلمة مرور جديدة", "30 دقيقة")]
    public void ResetLinkEmail_HasTheLinkAndItsLifetime_AndNoPassword(string language, string direction, string button, string lifetime)
    {
        var message = EmailTemplates.PasswordResetLink("user@org.test", "Sara <b>Ali</b>", Link, 30, language, "help@org.test");

        Assert.Equal(language, message.Language);
        Assert.Equal("help@org.test", message.ReplyTo);
        Assert.Contains($"dir=\"{direction}\"", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(button, message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(lifetime, message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(lifetime, message.TextBody, StringComparison.Ordinal);
        // The link is in the button, in plain sight below it, and in the text part.
        Assert.Contains($"href=\"{Link}\"", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(Link, message.TextBody, StringComparison.Ordinal);
        // A name is text, never markup; and this message never carries a password.
        Assert.DoesNotContain("<b>Ali</b>", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;Ali&lt;/b&gt;", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Temporary password", message.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("كلمة المرور المؤقتة", message.TextBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", false, "organization administrator")]
    [InlineData("en", true, "another Platform Administrator")]
    [InlineData("ar", false, "مدير مؤسستك")]
    [InlineData("ar", true, "مدير منصة آخر")]
    public void PasswordChangedNotice_NamesWhoCanHelp(string language, bool platformAdministrator, string helper)
    {
        var message = EmailTemplates.PasswordChangedNotice("user@org.test", "Sara", "https://insight.example.com/login", language, platformAdministrator);

        Assert.Contains(helper, message.TextBody, StringComparison.Ordinal);
        Assert.Contains(helper, message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("https://insight.example.com/login", message.HtmlBody, StringComparison.Ordinal);
        Assert.Null(message.ReplyTo);
    }

    [Fact]
    public void Version1_SendsEveryOrganizationsMail_ThroughThePlatformServer()
    {
        var platform = new CountingSender();
        var route = new PlatformEmailRoute(platform);

        // One switch point: an organization's own server (Version 2) is chosen here, and nowhere else.
        Assert.Same(platform, route.SenderFor(null));
        Assert.Same(platform, route.SenderFor(Guid.NewGuid()));
        Assert.Same(platform, route.SenderFor(Guid.NewGuid()));
    }

    private sealed class CountingSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
