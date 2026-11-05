using System.Net;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Email;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Email;

[Collection(DatabaseCollectionDefinition.Name)]
public sealed class AdministratorProvisioningTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;

    public AdministratorProvisioningTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task CreateAdministrator_EmailsTemporaryPassword_AndForcesChangeOnFirstLogin()
    {
        using var admin = _factory.CreateClient();
        await admin.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        var create = await admin.PostJsonAsync("/api/platform/administrators", new { email = "new.admin@wabitex.test", fullName = "New Admin", preferredLanguage = "ar" });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var body = await create.ReadJsonAsync();
        Assert.Equal("n***n@wabitex.test", body.GetProperty("deliveredTo").GetString());
        Assert.True(body.GetProperty("mustChangePassword").GetBoolean());
        Assert.DoesNotContain("temporaryPassword", await create.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var email = await _factory.Emails.WaitForAsync("new.admin@wabitex.test");
        Assert.Equal("ar", email.Language);
        Assert.Contains("dir=\"rtl\"", email.HtmlBody, StringComparison.Ordinal);
        var temporary = RecordingEmailSender.TemporaryPasswordOf(email);
        Assert.Equal(20, temporary.Length);

        using var newAdmin = _factory.CreateClient();
        var login = await newAdmin.LoginAsync("new.admin@wabitex.test", temporary);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.ReadJsonAsync()).GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await newAdmin.PostJsonAsync("/api/platform/administrators", new { email = "x@y.test", fullName = "X" })).StatusCode);

        var change = await newAdmin.PostJsonAsync("/api/auth/change-password", new { currentPassword = temporary, newPassword = "NewAdmin#Strong#Pass#2026" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        // Outbox row: metadata only, marked Sent, no body persisted.
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var record = await context.EmailMessages.SingleAsync(e => e.EmailMessageId == body.GetProperty("emailMessageId").GetGuid());
        await WaitUntilAsync(async () => (await context.EmailMessages.AsNoTracking().SingleAsync(e => e.EmailMessageId == record.EmailMessageId)).Status == EmailDeliveryStatus.Sent);
        var sent = await context.EmailMessages.AsNoTracking().SingleAsync(e => e.EmailMessageId == record.EmailMessageId);
        Assert.Equal("Account.Created", sent.TemplateCode);
        Assert.Equal(1, sent.Attempts);
        Assert.NotNull(sent.SentAt);
        Assert.DoesNotContain(typeof(EmailMessageRecord).GetProperties(), p => p.Name.Contains("Body", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAdministrator_RejectsDuplicateEmail_AndInvalidEmail()
    {
        using var admin = _factory.CreateClient();
        await admin.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        var duplicate = await admin.PostJsonAsync("/api/platform/administrators", new { email = TestData.AliceEmail, fullName = "Alice again" });
        var invalid = await admin.PostJsonAsync("/api/platform/administrators", new { email = "not-an-email", fullName = "Nobody" });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.DoesNotContain(_factory.Emails.Messages, m => m.ToEmail == TestData.AliceEmail);
    }

    [Fact]
    public async Task OrganizationUser_CannotCreateAdministrators()
    {
        using var user = _factory.CreateClient();
        await user.LoginAsync(TestData.BobEmail, TestData.UserPassword);

        var response = await user.PostJsonAsync("/api/platform/administrators", new { email = "intruder@wabitex.test", fullName = "Intruder" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeliveryFailure_IsRecordedOnTheOutboxRow_AfterRetries()
    {
        using var factory = new ApiFactory(_db.ConnectionString);
        factory.Emails.FailDeliveries = true;
        using var admin = factory.CreateClient();
        await admin.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        var create = await admin.PostJsonAsync("/api/platform/administrators", new { email = "unreachable@wabitex.test", fullName = "Unreachable" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.ReadJsonAsync()).GetProperty("emailMessageId").GetGuid();

        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        await WaitUntilAsync(async () => (await context.EmailMessages.AsNoTracking().SingleAsync(e => e.EmailMessageId == id)).Status == EmailDeliveryStatus.Failed, TimeSpan.FromSeconds(15));
        var failed = await context.EmailMessages.AsNoTracking().SingleAsync(e => e.EmailMessageId == id);
        Assert.Equal(2, failed.Attempts);
        Assert.Contains("Simulated SMTP failure", failed.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Administrators_CanBeListed_AndDeactivated_WithLastAdminProtection()
    {
        using var admin = _factory.CreateClient();
        await admin.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        var list = await (await admin.GetAsync("/api/platform/administrators")).ReadJsonAsync();
        Assert.Contains(list.EnumerateArray(), a => a.GetProperty("email").GetString() == TestData.AdminEmail && a.GetProperty("isBootstrapAdministrator").GetBoolean());
        Assert.DoesNotContain("passwordHash", await (await admin.GetAsync("/api/platform/administrators")).Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var self = await admin.PostJsonAsync($"/api/platform/administrators/{_db.Data.AdministratorId}/status", new { isActive = false });
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);

        var created = await admin.PostJsonAsync("/api/platform/administrators", new { email = "temp.admin@wabitex.test", fullName = "Temp Admin" });
        var tempId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();
        var deactivate = await admin.PostJsonAsync($"/api/platform/administrators/{tempId}/status", new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        var temporary = RecordingEmailSender.TemporaryPasswordOf(await _factory.Emails.WaitForAsync("temp.admin@wabitex.test"));
        using var temp = _factory.CreateClient();
        var denied = await temp.LoginAsync("temp.admin@wabitex.test", temporary);
        Assert.EndsWith("/account_disabled", (await denied.ReadJsonAsync()).GetProperty("type").GetString(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostJsonAsync($"/api/platform/administrators/{tempId}/status", new { isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await temp.LoginAsync("temp.admin@wabitex.test", temporary)).StatusCode);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Condition not met in time.");
    }
}
