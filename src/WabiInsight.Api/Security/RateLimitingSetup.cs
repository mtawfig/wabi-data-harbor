using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using WabiInsight.Api.Authentication;
using WabiInsight.Core.Security;

namespace WabiInsight.Api.Security;

/// <summary>
/// Fixed-window limits (P12.1): per client address on the sensitive anonymous endpoints (login, bootstrap), per
/// signed-in principal on the analysis endpoints that cost model tokens and source queries, and a global budget per
/// principal (or client address) across the whole API. The limiter runs after authentication (P13.25): people who
/// share one address (an office behind one router, a reverse proxy) each get their own budget once signed in.
/// </summary>
public static class RateLimitingSetup
{
    public const string LoginPolicy = "login";
    public const string BootstrapPolicy = "bootstrap";
    public const string AiPolicy = "ai";
    public const string PasswordResetPolicy = "password-reset";
    public const string DemoRequestPolicy = "demo-request";

    public static IServiceCollection AddWabiRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<SecurityOptions>>((options, security) =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy(LoginPolicy, context => PerClient(context, security.Value.LoginRateLimitPerMinute));
                options.AddPolicy(BootstrapPolicy, context => PerClient(context, security.Value.BootstrapRateLimitPerMinute));
                options.AddPolicy(PasswordResetPolicy, context => PerClient(context, security.Value.PasswordResetRateLimitPerMinute));
                options.AddPolicy(DemoRequestPolicy, context => PerClient(context, security.Value.DemoRequestRateLimitPerMinute));
                options.AddPolicy(AiPolicy, context => PerPrincipal(context, security.Value.AiRateLimitPerMinute));
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => PerPrincipal(context, security.Value.ApiRateLimitPerMinute));
            });
        services.AddRateLimiter(_ => { });
        return services;
    }

    private static RateLimitPartition<string> PerPrincipal(HttpContext context, int permitPerMinute)
    {
        return Window(PrincipalKey(context) ?? ClientKey(context), permitPerMinute);
    }

    private static RateLimitPartition<string> PerClient(HttpContext context, int permitPerMinute)
        => Window(ClientKey(context), permitPerMinute);

    /// <summary>The signed-in account (administrator and organization user ids never collide: the type is part of the key); null when anonymous.</summary>
    internal static string? PrincipalKey(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var subject = context.User.FindFirst(WabiClaims.Subject)?.Value;
        return string.IsNullOrEmpty(subject) ? null : $"user:{context.User.FindFirst(WabiClaims.PrincipalType)?.Value}:{subject}";
    }

    private static string ClientKey(HttpContext context) => "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    private static RateLimitPartition<string> Window(string key, int permitPerMinute)
    {
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }
}
