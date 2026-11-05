using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Core.Storage;

namespace WabiInsight.Infrastructure.Storage;

/// <summary>Artifact storage settings, bound from the "Storage" section.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Directory outside the web root. Relative paths resolve against the current directory.</summary>
    public string RootPath { get; set; } = Path.Combine(".artifacts", "storage");
}

/// <summary>
/// v1 artifact provider (review B9): a local directory outside the web root with random object names.
/// Keys look like "category/yyyy/MM/{uuid}.bin"; anything else is rejected before touching the file system.
/// </summary>
public sealed partial class FileSystemArtifactStorage : IArtifactStorage
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
        if (!CategoryPattern().IsMatch(category))
        {
            throw new ArgumentException("Invalid storage category.", nameof(category));
        }

        var now = _clock.UtcNow;
        var key = $"{category}/{now:yyyy}/{now:MM}/{Ids.New():N}.bin";
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

    private string Resolve(string key)
    {
        if (!KeyPattern().IsMatch(key))
        {
            throw new ArgumentException("Invalid storage key.", nameof(key));
        }

        var path = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Storage key escapes the root.", nameof(key));
        }

        return path;
    }

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex CategoryPattern();

    [GeneratedRegex("^[a-z0-9-]{1,40}/[0-9]{4}/[0-9]{2}/[0-9a-f]{32}\\.bin$")]
    private static partial Regex KeyPattern();
}

/// <summary>Accepts every upload. Replace with a real scanner (ClamAV, cloud API) in deployment.</summary>
public sealed class NoOpAttachmentScanner : IAttachmentScanner
{
    public Task<AttachmentScanResult> ScanAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
        => Task.FromResult(AttachmentScanResult.Clean);
}
