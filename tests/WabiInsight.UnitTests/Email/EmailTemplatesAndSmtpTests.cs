using WabiInsight.Core.Email;
using WabiInsight.Infrastructure.Authentication;
using WabiInsight.Infrastructure.Email;

namespace WabiInsight.UnitTests.Email;

public class EmailTemplatesAndSmtpTests
{
    [Fact]
    public void TemporaryPasswordTemplate_English_ContainsPasswordAndLink()
    {
        var message = EmailTemplates.TemporaryPassword("a@b.test", "Alice <Admin>", "Temp#Pass#123456", "en", isNewAccount: true, "https://insight.example/login");

        Assert.Equal("Your Wabi Insight account", message.Subject);
        Assert.Contains("Temp#Pass#123456", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Temp#Pass#123456", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Alice &lt;Admin&gt;", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("https://insight.example/login", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("dir=\"ltr\"", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TemporaryPasswordTemplate_Arabic_IsRtl()
    {
        var message = EmailTemplates.TemporaryPassword("a@b.test", "علي", "Temp#Pass#123456", "ar", isNewAccount: false, "https://insight.example/login");

        Assert.Equal("ar", message.Language);
        Assert.Contains("dir=\"rtl\"", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("كلمة المرور المؤقتة: Temp#Pass#123456", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SmtpMessage_UsesConfiguredSenderAndReplyTo()
    {
        var options = new EmailOptions { FromEmail = "no-reply@wabitex.com", FromName = "Wabi Insight Notifications", ReplyTo = "support@wabitex.com" };
        var message = EmailTemplates.TemporaryPassword("a@b.test", "Alice", "x", "en", true, "https://x/login");

        using var mime = SmtpEmailSender.BuildMessage(message, options);

        Assert.Equal("Wabi Insight Notifications", mime.From.Mailboxes.Single().Name);
        Assert.Equal("no-reply@wabitex.com", mime.From.Mailboxes.Single().Address);
        Assert.Equal("support@wabitex.com", mime.ReplyTo.Mailboxes.Single().Address);
        Assert.Equal("a@b.test", mime.To.Mailboxes.Single().Address);
        Assert.NotNull(mime.HtmlBody);
        Assert.NotNull(mime.TextBody);
    }

    [Theory]
    [InlineData("support@wabitex.com", "s***t@wabitex.com")]
    [InlineData("ab@x.y", "a***b@x.y")]
    [InlineData("a@x.y", "a@x.y")]
    public void Mask_HidesTheLocalPart(string email, string expected)
    {
        Assert.Equal(expected, AdministratorAccountService.Mask(email));
    }
}
