using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Api.Commands;

/// <summary>
/// `dotnet WabiInsight.Api.dll seed-demo` creates a demo organization and an organization user for local
/// development, screenshots and demos. Development environment only; idempotent. Real organization
/// onboarding arrives in P2.1 and replaces this for every other environment.
/// </summary>
internal static partial class SeedDemoCommand
{
    public const string Name = "seed-demo";

    public const string DemoOrganizationCode = "DEMO";
    public const string DemoUserEmail = "demo.user@demo.test";
    public const string DemoUserPassword = "Demo#Password#2026";

    public static async Task<int> RunAsync(WebApplication app, CancellationToken cancellationToken)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SeedDemo");
        if (!app.Environment.IsDevelopment())
        {
            LogRefused(logger, app.Environment.EnvironmentName);
            return 1;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHashingService>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        using var system = context.BeginSystemScope();

        var organization = await context.Organizations.SingleOrDefaultAsync(o => o.OrganizationCode == DemoOrganizationCode, cancellationToken);
        if (organization is null)
        {
            organization = new Organization
            {
                OrganizationId = Ids.New(),
                OrganizationCode = DemoOrganizationCode,
                OrganizationName = "Demo Organization",
                OrganizationNameAr = "المؤسسة التجريبية",
                Description = "Local development demo tenant (seed-demo command).",
                TimeZone = "Asia/Dubai",
                DefaultLanguage = "en",
                Status = OrganizationStatus.Active,
            };
            context.Organizations.Add(organization);
        }

        // System roles and the built-in Platform Insights application, exactly as onboarding provisions them.
        foreach (var template in SystemRoles.Templates)
        {
            var roleExists = await context.Roles.IgnoreQueryFilters().AnyAsync(r => r.OrganizationId == organization.OrganizationId && r.RoleCode == template.Code, cancellationToken);
            if (!roleExists)
            {
                context.Roles.Add(new Role
                {
                    RoleId = Ids.New(),
                    OrganizationId = organization.OrganizationId,
                    RoleCode = template.Code,
                    NameEn = template.NameEn,
                    NameAr = template.NameAr,
                    Description = template.Description,
                    IsSystem = template.IsSystem,
                    TemplateVersion = SystemRoles.TemplateVersion,
                });
            }
        }

        if (!await context.Applications.IgnoreQueryFilters().AnyAsync(a => a.OrganizationId == organization.OrganizationId && a.BuiltInCode == Application.PlatformInsightsCode, cancellationToken))
        {
            context.Applications.Add(new Application
            {
                ApplicationId = Ids.New(),
                OrganizationId = organization.OrganizationId,
                ApplicationName = "Platform Insights",
                ApplicationNameAr = "تحليلات المنصة",
                ApplicationCode = Application.PlatformInsightsCode,
                DatabaseType = DatabaseType.PlatformInsights,
                IsBuiltIn = true,
                BuiltInCode = Application.PlatformInsightsCode,
                IsApproved = true,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        var normalized = EmailNormalizer.Normalize(DemoUserEmail);
        var exists = await context.LoginIdentities.AnyAsync(l => l.NormalizedEmail == normalized, cancellationToken);
        if (!exists)
        {
            var user = new OrganizationUser
            {
                UserId = Ids.New(),
                OrganizationId = organization.OrganizationId,
                Email = DemoUserEmail,
                NormalizedEmail = normalized,
                FullName = "Demo User",
                EmployeeNumber = "EMP-0001",
                PreferredLanguage = "en",
                Status = OrganizationUserStatus.Active,
                MustChangePassword = false,
                PasswordHash = hasher.Hash(DemoUserPassword),
                SecurityStamp = TemporaryPasswordGenerator.NewSecurityStamp(),
            };
            context.OrganizationUsers.Add(user);
            context.LoginIdentities.Add(new LoginIdentity
            {
                NormalizedEmail = normalized,
                PrincipalType = PrincipalType.OrganizationUser,
                PrincipalId = user.UserId,
                OrganizationId = organization.OrganizationId,
                CreatedAt = clock.UtcNow,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        // The demo user administers the demo organization.
        var demoUserId = await context.LoginIdentities.Where(l => l.NormalizedEmail == normalized).Select(l => l.PrincipalId).SingleAsync(cancellationToken);
        var adminRoleId = await context.Roles.IgnoreQueryFilters().Where(r => r.OrganizationId == organization.OrganizationId && r.RoleCode == SystemRoles.OrganizationAdmin).Select(r => r.RoleId).SingleAsync(cancellationToken);
        if (!await context.UserRoles.IgnoreQueryFilters().AnyAsync(ur => ur.UserId == demoUserId && ur.RoleId == adminRoleId, cancellationToken))
        {
            context.UserRoles.Add(new UserRole { OrganizationId = organization.OrganizationId, UserId = demoUserId, RoleId = adminRoleId, AssignedAt = clock.UtcNow });
            await context.SaveChangesAsync(cancellationToken);
        }

        LogDone(logger, DemoOrganizationCode, DemoUserEmail);
        return 0;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "seed-demo is available only in the Development environment (current: {Environment}).")]
    private static partial void LogRefused(ILogger logger, string environment);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data ready: organization {Organization}, user {Email} (password in SeedDemoCommand, development only).")]
    private static partial void LogDone(ILogger logger, string organization, string email);
}
