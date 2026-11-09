using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Core.Storage;

namespace WabiInsight.Infrastructure.Storage;

/// <summary>
/// Artifact storage in a private Azure Blob Storage container ("Storage:Provider": "AzureBlob"). Blob names are the same
/// random keys as on the disk ("category/yyyy/MM/{uuid}.bin"), so a file copied from the disk keeps its key and every
/// reference to it in the configuration database stays valid. Blobs are never public: every download still goes
/// through an authorized API endpoint. While <see cref="AzureBlobStorageOptions.ReadFromDiskWhenMissing"/> is on,
/// files saved on the disk before the switch are still read from there.
/// </summary>
public sealed partial class AzureBlobArtifactStorage : IArtifactStorage, IDisposable
{
    private readonly BlobContainerClient _container;
    private readonly FileSystemArtifactStorage? _disk;
    private readonly IClock _clock;
    private readonly ILogger<AzureBlobArtifactStorage> _logger;
    private readonly SemaphoreSlim _ready = new(1, 1);
    private volatile bool _containerChecked;

    public AzureBlobArtifactStorage(IOptions<StorageOptions> options, FileSystemArtifactStorage disk, IClock clock, ILogger<AzureBlobArtifactStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Value;
        if (settings.Problem() is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        _container = new BlobContainerClient(settings.AzureBlob.ConnectionString, settings.AzureBlob.ContainerName);
        _disk = settings.AzureBlob.ReadFromDiskWhenMissing ? disk : null;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>The container's address (account and container name, no credentials), for the storage commands.</summary>
    public Uri ContainerUri => _container.Uri;

    public async Task<string> SaveAsync(string category, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var key = ArtifactKeys.New(category, _clock.UtcNow);
        await UploadAsync(key, content, cancellationToken);
        return key;
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArtifactKeys.Validate(key);
        try
        {
            var download = await _container.GetBlobClient(key).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return download.Value.Content;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return _disk is null ? null : await _disk.OpenReadAsync(key, cancellationToken);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArtifactKeys.Validate(key);
        try
        {
            await _container.GetBlobClient(key).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            // The container does not exist yet: nothing to delete there.
        }

        if (_disk is not null)
        {
            await _disk.DeleteAsync(key, cancellationToken);
        }
    }

    /// <summary>Copies one file from the disk under the same key; false when the container already has it.</summary>
    public async Task<bool> CopyFromDiskAsync(FileSystemArtifactStorage disk, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(disk);
        ArtifactKeys.Validate(key);
        await EnsureContainerAsync(cancellationToken);
        if (await _container.GetBlobClient(key).ExistsAsync(cancellationToken))
        {
            return false;
        }

        await using var content = await disk.OpenReadAsync(key, cancellationToken) ?? throw new FileNotFoundException("The file is no longer on the disk.", key);
        await UploadAsync(key, content, cancellationToken);
        return true;
    }

    private async Task UploadAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        await EnsureContainerAsync(cancellationToken);

        // Keys are random and never reused: refuse to overwrite an existing blob.
        await _container.GetBlobClient(key).UploadAsync(content, new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } }, cancellationToken);
    }

    /// <summary>Creates the private container once per process when it does not exist (a SAS without that right is fine if it exists).</summary>
    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_containerChecked)
        {
            return;
        }

        await _ready.WaitAsync(cancellationToken);
        try
        {
            if (_containerChecked)
            {
                return;
            }

            try
            {
                await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException e) when (e.Status == 403)
            {
                LogCannotCreate(_logger, _container.Name);
            }

            _containerChecked = true;
        }
        finally
        {
            _ready.Release();
        }
    }

    public void Dispose() => _ready.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The storage credential may not create the container {Container}; it must already exist.")]
    private static partial void LogCannotCreate(ILogger logger, string container);
}
