using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Core.Storage;

namespace WabiInsight.Infrastructure.Storage;

/// <summary>Where generated reports, exports, attachments and logos are kept (the "Storage:Provider" setting).</summary>
public static class StorageProviders
{
    /// <summary>A directory on the server's disk (<see cref="StorageOptions.RootPath"/>). The default.</summary>
    public const string FileSystem = "FileSystem";

    /// <summary>A private Azure Blob Storage container (<see cref="StorageOptions.AzureBlob"/>).</summary>
    public const string AzureBlob = "AzureBlob";
}

/// <summary>Artifact storage settings, bound from the "Storage" section.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary><see cref="StorageProviders.FileSystem"/> (default) or <see cref="StorageProviders.AzureBlob"/>.</summary>
    public string Provider { get; set; } = StorageProviders.FileSystem;

    /// <summary>Directory outside the web root. Relative paths resolve against the current directory.</summary>
    public string RootPath { get; set; } = Path.Combine(".artifacts", "storage");

    public AzureBlobStorageOptions AzureBlob { get; set; } = new();

    public bool UsesAzureBlob => string.Equals(Provider, StorageProviders.AzureBlob, StringComparison.OrdinalIgnoreCase);

    /// <summary>A problem with the settings that stops the application from storing files, or null. Never contains the connection string.</summary>
    public string? Problem()
    {
        if (!string.Equals(Provider, StorageProviders.FileSystem, StringComparison.OrdinalIgnoreCase) && !UsesAzureBlob)
        {
            return $"Storage:Provider must be \"{StorageProviders.FileSystem}\" or \"{StorageProviders.AzureBlob}\".";
        }

        if (UsesAzureBlob && string.IsNullOrWhiteSpace(AzureBlob.ConnectionString))
        {
            return "Storage:Provider is AzureBlob but Storage:AzureBlob:ConnectionString is empty. Set it in appsettings.Local.json or the environment variable Storage__AzureBlob__ConnectionString.";
        }

        if (UsesAzureBlob && !ArtifactKeys.IsValidContainer(AzureBlob.ContainerName))
        {
            return "Storage:AzureBlob:ContainerName must be 3-63 lowercase letters, digits or single hyphens, starting and ending with a letter or digit.";
        }

        return null;
    }
}

/// <summary>The "Storage:AzureBlob" section.</summary>
public sealed class AzureBlobStorageOptions
{
    /// <summary>
    /// The storage account connection string (account key or SAS). A secret: set it in appsettings.Local.json or the
    /// environment variable Storage__AzureBlob__ConnectionString, never in appsettings.json.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>A private container; created on first use when the credential may create it.</summary>
    public string ContainerName { get; set; } = "wabi-insight-files";

    /// <summary>
    /// When a file is not in the container, read it from <see cref="StorageOptions.RootPath"/> instead, so files saved on
    /// the disk before the switch still open (see the storage-copy command). Deletes remove both copies.
    /// </summary>
    public bool ReadFromDiskWhenMissing { get; set; } = true;
}

/// <summary>
/// Storage keys look like "category/yyyy/MM/{uuid}.bin": random, never derived from user input (review B9). Every
/// provider validates a key before touching its store.
/// </summary>
public static partial class ArtifactKeys
{
    public static string New(string category, DateTimeOffset now)
    {
        if (!IsValidCategory(category))
        {
            throw new ArgumentException("Invalid storage category.", nameof(category));
        }

        return $"{category}/{now:yyyy}/{now:MM}/{Ids.New():N}.bin";
    }

    public static bool IsValidCategory(string? category) => category is not null && CategoryPattern().IsMatch(category);

    public static bool IsValidKey(string? key) => key is not null && KeyPattern().IsMatch(key);

    public static string Validate(string key) => IsValidKey(key) ? key : throw new ArgumentException("Invalid storage key.", nameof(key));

    public static bool IsValidContainer(string? name) => name is not null && ContainerPattern().IsMatch(name);

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex CategoryPattern();

    [GeneratedRegex("^[a-z0-9-]{1,40}/[0-9]{4}/[0-9]{2}/[0-9a-f]{32}\\.bin$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^(?=.{3,63}$)[a-z0-9](?:[a-z0-9]|-(?!-))*[a-z0-9]$")]
    private static partial Regex ContainerPattern();
}

/// <summary>
/// v1 artifact provider (review B9): a local directory outside the web root with random object names.
/// Keys look like "category/yyyy/MM/{uuid}.bin"; anything else is rejected before touching the file system.
/// </summary>
public sealed class FileSystemArtifactStorage : IArtifactStorage
{
    private readonly string _root;
    private readonly IClock _clock;

    public FileSystemArtifactStorage(IOptions<StorageOptions> options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        _root = Path.GetFullPath(options.Value.RootPath);
        _clock = clock;
    }

    public async Task<string> SaveAsync(string category, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var key = ArtifactKeys.New(category, _clock.UtcNow);
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>The keys of every file stored under the root (files that do not follow the key format are skipped).</summary>
    public IEnumerable<string> ListKeys()
    {
        if (!Directory.Exists(_root))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(_root, "*.bin", SearchOption.AllDirectories))
        {
            var key = Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (ArtifactKeys.IsValidKey(key))
            {
                yield return key;
            }
        }
    }

    private string Resolve(string key)
    {
        ArtifactKeys.Validate(key);
        var path = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Storage key escapes the root.", nameof(key));
        }

        return path;
    }
}

/// <summary>Accepts every upload. Replace with a real scanner (ClamAV, cloud API) in deployment.</summary>
public sealed class NoOpAttachmentScanner : IAttachmentScanner
{
    public Task<AttachmentScanResult> ScanAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
        => Task.FromResult(AttachmentScanResult.Clean);
}
