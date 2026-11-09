using System.Security.Cryptography;
using Azure;
using WabiInsight.Core.Storage;
using WabiInsight.Infrastructure.Storage;

namespace WabiInsight.Api.Commands;

/// <summary>
/// `dotnet WabiInsight.Api.dll storage-check` writes a small test file to the configured storage (server disk or Azure
/// Blob Storage, per Storage:Provider), reads it back, compares it, deletes it and exits: 0 when everything works.
/// Run it after changing the storage settings. It never prints the connection string.
/// </summary>
internal static partial class StorageCheckCommand
{
    public const string Name = "storage-check";

    public static async Task<int> RunAsync(WebApplication app, CancellationToken cancellationToken)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("StorageCheck");
        var options = app.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        if (options.Problem() is { } problem)
        {
            LogProblem(logger, problem);
            return 1;
        }

        try
        {
            var target = options.UsesAzureBlob
                ? app.Services.GetRequiredService<AzureBlobArtifactStorage>().ContainerUri.GetLeftPart(UriPartial.Path)
                : Path.GetFullPath(options.RootPath);
            LogTarget(logger, options.UsesAzureBlob ? StorageProviders.AzureBlob : StorageProviders.FileSystem, target);

            var storage = app.Services.GetRequiredService<IArtifactStorage>();
            var payload = RandomNumberGenerator.GetBytes(4096);
            string key;
            using (var content = new MemoryStream(payload))
            {
                key = await storage.SaveAsync(Name, content, cancellationToken);
            }

            await using (var read = await storage.OpenReadAsync(key, cancellationToken) ?? throw new InvalidOperationException("The test file was written but could not be found again."))
            using (var copy = new MemoryStream())
            {
                await read.CopyToAsync(copy, cancellationToken);
                if (!copy.ToArray().AsSpan().SequenceEqual(payload))
                {
                    throw new InvalidOperationException("The test file read back differs from the one written.");
                }
            }

            await storage.DeleteAsync(key, cancellationToken);
            if (await storage.OpenReadAsync(key, cancellationToken) is { } left)
            {
                await left.DisposeAsync();
                throw new InvalidOperationException("The test file is still there after deleting it.");
            }

            LogOk(logger);
            return 0;
        }
        catch (RequestFailedException e)
        {
            LogAzureFailed(logger, e.Status, e.ErrorCode ?? "none");
            return 1;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogFailed(logger, e.GetType().Name, e.Message);
            return 1;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Storage settings: {Problem}")]
    private static partial void LogProblem(ILogger logger, string problem);

    [LoggerMessage(Level = LogLevel.Information, Message = "Checking storage provider {Provider} at {Target}.")]
    private static partial void LogTarget(ILogger logger, string provider, string target);

    [LoggerMessage(Level = LogLevel.Information, Message = "Storage check passed: a test file was written, read back identical and deleted.")]
    private static partial void LogOk(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Storage check failed: Azure returned HTTP {Status}, error code {ErrorCode} (403: wrong key or network rules; 404: wrong account).")]
    private static partial void LogAzureFailed(ILogger logger, int status, string errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Storage check failed: {Type}: {Message}")]
    private static partial void LogFailed(ILogger logger, string type, string message);
}
