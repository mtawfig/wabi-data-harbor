using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Security;

namespace WabiInsight.Api.Security;

/// <summary>Per-client fixed-window limits on the sensitive anonymous endpoints (login, bootstrap).</summary>
public static class RateLimitingSetup
{
    public const string LoginPolicy = "login";
    public const string BootstrapPolicy = "bootstrap";

    public static IServiceCollection AddWabiRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<SecurityOptions>>((options, security) =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy(LoginPolicy, context => PerClient(context, security.Value.LoginRateLimitPerMinute));
                options.AddPolicy(BootstrapPolicy, context => PerClient(context, security.Value.BootstrapRateLimitPerMinute));
            });
        services.AddRateLimiter(_ => { });
        return services;
    }

    private static RateLimitPartition<string> PerClient(HttpContext context, int permitPerMinute)
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }
}
