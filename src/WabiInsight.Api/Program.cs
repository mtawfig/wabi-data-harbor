using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WabiInsight.Api.Authentication;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Commands;
using WabiInsight.Api.Hosting;
using WabiInsight.Api.Security;
using WabiInsight.Core.Email;
using WabiInsight.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Local, git-ignored overrides (connection strings, bootstrap secrets). Never commit real values to
// appsettings.json; use appsettings.Local.json, user secrets or environment variables instead.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
}

builder.Services
    .AddControllers(options => options.Filters.Add<AntiforgeryValidationFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<ApiErrorUsageHandler>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "wi.xsrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

// Server-level body cap (defense in depth); the per-endpoint default and the upload exceptions are applied by RequestBodyLimitMiddleware.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = builder.Configuration.GetValue<long?>("Security:MaxRequestBodyBytes") ?? 1024 * 1024;
});

// Behind nginx or IIS + ARR the connection comes from the proxy: take the client address and scheme from
// X-Forwarded-For / X-Forwarded-Proto so per-client rate limits and audit IP addresses see the real caller.
// Only proxies on this machine (loopback) are trusted by default; list others in ReverseProxy:KnownProxies.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    if (builder.Configuration.GetValue<bool>("ReverseProxy:TrustAllProxies"))
    {
        // Platforms whose front ends have no fixed addresses (Azure App Service): the application is reachable only
        // through them, and with ForwardLimit 1 only the entry the front end appended (the real caller) is used.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }

    foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(proxy, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }
});

builder.Services.AddWabiInsightInfrastructure(builder.Configuration);

// On shutdown the host has to wait at least as long as the e-mail worker keeps sending (Email:ShutdownGraceSeconds),
// plus a few seconds to record the last outcome; the host default is 30 seconds.
builder.Services.AddOptions<HostOptions>().Configure<IOptions<EmailOptions>>((host, email) =>
{
    var needed = TimeSpan.FromSeconds(email.Value.ShutdownGraceSeconds + 10);
    if (host.ShutdownTimeout < needed)
    {
        host.ShutdownTimeout = needed;
    }
});

builder.Services.AddWabiAuthentication();
builder.Services.AddWabiAuthorization();
builder.Services.AddWabiRateLimiting();

var app = builder.Build();

if (args.Length > 0)
{
    if (string.Equals(args[0], MigrateCommand.Name, StringComparison.OrdinalIgnoreCase))
    {
        return await MigrateCommand.RunAsync(app, CancellationToken.None);
    }

    if (string.Equals(args[0], BootstrapCommand.Name, StringComparison.OrdinalIgnoreCase))
    {
        return await BootstrapCommand.RunAsync(app, CancellationToken.None);
    }

    if (string.Equals(args[0], SeedDemoCommand.Name, StringComparison.OrdinalIgnoreCase))
    {
        return await SeedDemoCommand.RunAsync(app, CancellationToken.None);
    }

    if (string.Equals(args[0], StorageCheckCommand.Name, StringComparison.OrdinalIgnoreCase))
    {
        return await StorageCheckCommand.RunAsync(app, CancellationToken.None);
    }

    if (string.Equals(args[0], StorageCopyCommand.Name, StringComparison.OrdinalIgnoreCase))
    {
        return await StorageCopyCommand.RunAsync(app, CancellationToken.None);
    }
}

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

// Optional: the Angular build served by the API itself (Web:RootPath; Azure App Service). Behind nginx or IIS it stays off.
var webRoot = WebAppHosting.ResolveRoot(app.Configuration);
if (webRoot is not null)
{
    var hostingLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WebAppHosting");
    if (File.Exists(Path.Combine(webRoot, "index.html")))
    {
        WebAppHosting.LogServing(hostingLogger, webRoot);
    }
    else
    {
        WebAppHosting.LogMissing(hostingLogger, webRoot);
    }

    app.UseWebAppFiles(webRoot);
}

app.UseAuthentication();
// After authentication: the per-user budgets are keyed by the signed-in principal, so they need it resolved first.
app.UseRateLimiter();
app.UseMiddleware<TenantContextMiddleware>();
app.UseMiddleware<PasswordChangeRequiredMiddleware>();
app.UseAuthorization();
app.UseMiddleware<RequestBodyLimitMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = WriteHealthResponseAsync,
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync,
}).AllowAnonymous();

app.MapControllers();
if (webRoot is not null)
{
    app.MapWebAppFallback(webRoot);
}

await app.RunAsync();
return 0;

static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    var payload = new
    {
        status = report.Status.ToString(),
        checkedAt = DateTimeOffset.UtcNow,
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description,
            durationMs = entry.Value.Duration.TotalMilliseconds,
        }),
    };

    return context.Response.WriteAsJsonAsync(payload);
}

/// <summary>Marker for WebApplicationFactory in integration tests.</summary>
public partial class Program;
