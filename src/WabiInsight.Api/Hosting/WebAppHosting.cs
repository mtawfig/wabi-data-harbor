using Microsoft.Extensions.FileProviders;

namespace WabiInsight.Api.Hosting;

/// <summary>
/// Optional: the API also serves the Angular build, so the application and its API share one address (Azure App
/// Service, where there is no nginx or IIS in front). Off unless <c>Web:RootPath</c> names an existing folder; behind
/// nginx or IIS the web server keeps serving the files and nothing changes.
/// </summary>
public static partial class WebAppHosting
{
    public const string RootPathKey = "Web:RootPath";

    /// <summary>The application shell's policy, as nginx.conf sets it: scripts self-hosted, Angular Material's runtime styles, ECharts on canvas.</summary>
    public const string ShellContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    /// <summary>Hashed build outputs and fonts never change under the same name; everything else (index.html, i18n files) is revalidated.</summary>
    private static readonly string[] LongLivedExtensions = [".js", ".css", ".woff", ".woff2", ".png", ".svg", ".ico", ".webp", ".jpg"];

    /// <summary>
    /// The web app's folder, or null when the API does not serve it. The folder is created when missing so a web app
    /// deployed after the API starts is served at once (files are read from disk on every request).
    /// </summary>
    public static string? ResolveRoot(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration[RootPathKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var root = Path.GetFullPath(configured);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>Serves the build's files (before rate limiting and authentication: they are public and identical for everyone).</summary>
    public static WebApplication UseWebAppFiles(this WebApplication app, string root)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new PhysicalFileProvider(root) });
        app.UseStaticFiles(StaticOptions(root));
        return app;
    }

    /// <summary>Any other page address (deep links such as /history/123) returns index.html; /api and /health keep their own answers, and missing files stay 404.</summary>
    public static WebApplication MapWebAppFallback(this WebApplication app, string root)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapFallbackToFile("{*path:nonfile:regex(^(?!api(/|$)|health(/|$)).*$)}", "index.html", StaticOptions(root)).AllowAnonymous();
        return app;
    }

    private static StaticFileOptions StaticOptions(string root) => new()
    {
        FileProvider = new PhysicalFileProvider(root),
        OnPrepareResponse = context =>
        {
            var name = context.File.Name;
            var longLived = LongLivedExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
            context.Context.Response.Headers.CacheControl = longLived ? "public, max-age=2592000, immutable" : "no-cache";
        },
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Serving the web app from {Root}.")]
    public static partial void LogServing(ILogger logger, string root);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Web:RootPath is {Root} but there is no index.html yet; pages are served once the web app is deployed there.")]
    public static partial void LogMissing(ILogger logger, string root);
}
