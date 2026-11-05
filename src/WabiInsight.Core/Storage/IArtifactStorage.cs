namespace WabiInsight.Core.Storage;

/// <summary>
/// Opaque binary storage for attachments and generated artifacts (review B9). Keys are random, never
/// derived from user input, and never exposed as public paths: every download goes through an authorized API endpoint.
/// </summary>
public interface IArtifactStorage
{
    /// <summary>Stores the content under a new random key inside the given category (e.g. "support-attachments").</summary>
    Task<string> SaveAsync(string category, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Opens the stored content, or returns null when the key does not exist.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Virus-scan hook point for uploads (specification §15.10). The default implementation accepts every file.</summary>
public interface IAttachmentScanner
{
    Task<AttachmentScanResult> ScanAsync(string fileName, Stream content, CancellationToken cancellationToken = default);
}

public sealed record AttachmentScanResult(bool IsClean, string? Reason = null)
{
    public static readonly AttachmentScanResult Clean = new(true);
}
