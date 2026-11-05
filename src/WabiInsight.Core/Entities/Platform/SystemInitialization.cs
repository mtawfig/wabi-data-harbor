namespace WabiInsight.Core.Entities.Platform;

/// <summary>Single-row table recording bootstrap completion. The fixed key makes a second row impossible.</summary>
public class SystemInitialization
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public bool BootstrapCompleted { get; set; }

    public DateTimeOffset? BootstrapCompletedAt { get; set; }

    public Guid? BootstrapAdministratorId { get; set; }

    public string? SchemaVersion { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
