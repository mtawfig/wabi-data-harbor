using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Authentication;
using WabiInsight.Core.Common;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Api.Authentication;

/// <summary>
/// HttpOnly cookie sessions (specification §8): sliding idle timeout, absolute lifetime enforced via the
/// issued-at ticket property, JSON 401/403 instead of redirects, and periodic security-stamp revalidation
/// against the database so password changes, deactivation and suspension end sessions.
/// </summary>
public static class CookieAuthenticationSetup
{
    public const string CookieName = "wi.session";
    private const string IssuedAtProperty = ".wi.issued";

    public static IServiceCollection AddWabiAuthentication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IRequestContext, HttpRequestContext>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<SecurityOptions>>((options, security) =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.IsEssential = true;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(security.Value.SessionIdleMinutes);
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    },
                    OnSigningIn = context =>
                    {
                        context.Properties!.Items[IssuedAtProperty] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                        return Task.CompletedTask;
                    },
                    OnValidatePrincipal = ValidatePrincipalAsync,
                };
            });

        return services;
    }

    public static AuthenticationProperties NewSessionProperties() => new()
    {
        IsPersistent = false,
        AllowRefresh = true,
    };

    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var security = services.GetRequiredService<IOptions<SecurityOptions>>().Value;
        var clock = services.GetRequiredService<IClock>();
        var now = clock.UtcNow;

        // Absolute lifetime: the ticket's original issue time, carried through sliding renewals.
        if (context.Properties.Items.TryGetValue(IssuedAtProperty, out var issuedRaw)
            && long.TryParse(issuedRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var issuedUnix)
            && now - DateTimeOffset.FromUnixTimeSeconds(issuedUnix) > TimeSpan.FromHours(security.SessionAbsoluteHours))
        {
            await RejectAsync(context);
            return;
        }

        var user = context.Principal!;
        var validatedAt = long.TryParse(user.FindFirstValue(WabiClaims.ValidatedAt), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? DateTimeOffset.FromUnixTimeSeconds(v)
            : DateTimeOffset.MinValue;

        if (now - validatedAt < TimeSpan.FromMinutes(security.SecurityStampValidationMinutes))
        {
            return;
        }

        if (!Guid.TryParse(user.FindFirstValue(WabiClaims.Subject), out var id)
            || !Enum.TryParse<PrincipalType>(user.FindFirstValue(WabiClaims.PrincipalType), out var type))
        {
            await RejectAsync(context);
            return;
        }

        var resolver = services.GetRequiredService<IAccountResolver>();
        var resolved = await resolver.ResolveAsync(type, id, context.HttpContext.RequestAborted);

        if (resolved.Account is null
            || !string.Equals(resolved.Account.SecurityStamp, user.FindFirstValue(WabiClaims.SecurityStamp), StringComparison.Ordinal))
        {
            await RejectAsync(context);
            return;
        }

        context.ReplacePrincipal(WabiClaims.ToClaimsPrincipal(resolved.Account, now));
        context.ShouldRenew = true;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
