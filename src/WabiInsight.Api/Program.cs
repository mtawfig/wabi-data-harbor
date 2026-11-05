using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WabiInsight.Api.Authentication;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Commands;
using WabiInsight.Api.Security;
using WabiInsight.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers(options => options.Filters.Add<AntiforgeryValidationFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddProblemDetails();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "wi.xsrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddWabiInsightInfrastructure(builder.Configuration);
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
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseMiddleware<PasswordChangeRequiredMiddleware>();
app.UseAuthorization();

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
