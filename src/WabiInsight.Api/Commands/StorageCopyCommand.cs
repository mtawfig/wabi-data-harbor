using Azure;
using WabiInsight.Infrastructure.Storage;

namespace WabiInsight.Api.Commands;

/// <summary>
/// `dotnet WabiInsight.Api.dll storage-copy` copies every file stored on the server disk (Storage:RootPath) to the Azure
/// Blob Storage container under the same key, skipping files the container already has, and exits: 0 when nothing
/// failed. Run it once after switching Storage:Provider to AzureBlob; it can be run again safely. The disk files are
/// left in place (remove them yourself once the copy is verified).
/// </summary>
internal static partial class StorageCopyCommand
{
    public const string Name = "storage-copy";

    public static async Task<int> RunAsync(WebApplication app, CancellationToken cancellationToken)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("StorageCopy");
        var options = app.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        if (!options.UsesAzureBlob)
        {
            LogNotAzure(logger);
            return 1;
        }

        if (options.Problem() is { } problem)
        {
            LogProblem(logger, problem);
            return 1;
        }

        var disk = app.Services.GetRequiredService<FileSystemArtifactStorage>();
        var azure = app.Services.GetRequiredService<AzureBlobArtifactStorage>();
        var root = Path.GetFullPath(options.RootPath);
        var container = azure.ContainerUri.GetLeftPart(UriPartial.Path);
        LogStart(logger, root, container);
        int copied = 0, skipped = 0, failed = 0;
        foreach (var key in disk.ListKeys())
        {
            try
            {
                if (await azure.CopyFromDiskAsync(disk, key, cancellationToken))
                {
                    copied++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (RequestFailedException e)
            {
                failed++;
                LogFileFailed(logger, key, $"HTTP {e.Status} {e.ErrorCode}");
            }
            catch (IOException e)
            {
                failed++;
                LogFileFailed(logger, key, e.Message);
            }
        }

        LogDone(logger, copied, skipped, failed);
        return failed == 0 ? 0 : 1;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "storage-copy copies the disk files to Azure Blob Storage: set Storage:Provider to AzureBlob first.")]
    private static partial void LogNotAzure(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Storage settings: {Problem}")]
    private static partial void LogProblem(ILogger logger, string problem);

    [LoggerMessage(Level = LogLevel.Information, Message = "Copying the files of {Root} to {Container}.")]
    private static partial void LogStart(ILogger logger, string root, string container);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not copy {Key}: {Reason}")]
    private static partial void LogFileFailed(ILogger logger, string key, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Storage copy finished: {Copied} copied, {Skipped} already in the container, {Failed} failed.")]
    private static partial void LogDone(ILogger logger, int copied, int skipped, int failed);
}
