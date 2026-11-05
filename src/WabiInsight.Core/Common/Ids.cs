namespace WabiInsight.Core.Common;

/// <summary>Primary keys are UUID v7: non-enumerable (IDOR-resistant) and index-friendly.</summary>
public static class Ids
{
    public static Guid New() => Guid.CreateVersion7();
}
