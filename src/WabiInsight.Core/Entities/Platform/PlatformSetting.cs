namespace WabiInsight.Core.Entities.Platform;

/// <summary>Platform-wide key/value setting managed by Platform Administrators (specification §26.1).</summary>
public class PlatformSetting
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsSensitive { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedByAdministratorId { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
