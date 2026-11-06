using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Security;

namespace WabiInsight.Api.Security;

/// <summary>
/// Fixed-window limits (P12.1): per client address on the sensitive anonymous endpoints (login, bootstrap), per
/// signed-in principal on the analysis endpoints that cost model tokens and source queries, and a global budget per
/// principal (or client address) across the whole API.
/// </summary>
public static class RateLimitingSetup
{
    public const string LoginPolicy = "login";
    public const string BootstrapPolicy = "bootstrap";
    public const string AiPolicy = "ai";

    public static IServiceCollection AddWabiRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<SecurityOptions>>((options, security) =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy(LoginPolicy, context => PerClient(context, security.Value.LoginRateLimitPerMinute));
                options.AddPolicy(BootstrapPolicy, context => PerClient(context, security.Value.BootstrapRateLimitPerMinute));
                options.AddPolicy(AiPolicy, context => PerPrincipal(context, security.Value.AiRateLimitPerMinute));
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => PerPrincipal(context, security.Value.ApiRateLimitPerMinute));
            });
        services.AddRateLimiter(_ => { });
        return services;
    }

    private static RateLimitPartition<string> PerPrincipal(HttpContext context, int permitPerMinute)
    {
        var principal = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null;
        var key = principal ?? "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return Window(key, permitPerMinute);
    }

    private static RateLimitPartition<string> PerClient(HttpContext context, int permitPerMinute)
        => Window(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", permitPerMinute);

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
