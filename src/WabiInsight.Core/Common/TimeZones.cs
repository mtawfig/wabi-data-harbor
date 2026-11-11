namespace WabiInsight.Core.Common;

/// <summary>
/// Finds an organization's time zone by its IANA name ("Asia/Dubai"). A Windows server whose .NET runtime runs without
/// the ICU libraries knows Windows names only, and would treat every IANA name as unknown; the zones the product
/// offers are therefore also looked up by their Windows name, so schedules and "today" stay on the organization's
/// clock on any server.
/// </summary>
public static class TimeZones
{
    /// <summary>The zones offered when an organization is created or edited, with the Windows name of each.</summary>
    public static IReadOnlyDictionary<string, string> WindowsNames { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Asia/Dubai"] = "Arabian Standard Time",
        ["Asia/Muscat"] = "Arabian Standard Time",
        ["Asia/Riyadh"] = "Arab Standard Time",
        ["Asia/Qatar"] = "Arab Standard Time",
        ["Asia/Kuwait"] = "Arab Standard Time",
        ["Asia/Bahrain"] = "Arab Standard Time",
        ["Africa/Cairo"] = "Egypt Standard Time",
        ["Asia/Amman"] = "Jordan Standard Time",
        ["Europe/London"] = "GMT Standard Time",
        ["Europe/Berlin"] = "W. Europe Standard Time",
        ["America/New_York"] = "Eastern Standard Time",
    };

    /// <summary>True when this server resolves IANA names itself (Linux, or Windows with ICU).</summary>
    public static bool IanaNamesAvailable => TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Dubai", out _);

    /// <summary>The zone with this name, or false when the server knows no such zone.</summary>
    public static bool TryFind(string? id, out TimeZoneInfo zone) => TryFind(id, SystemZone, out zone);

    /// <summary>As <see cref="TryFind(string?, out TimeZoneInfo)"/>, with the server's own lookup replaced (tests).</summary>
    public static bool TryFind(string? id, Func<string, TimeZoneInfo?> find, out TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(find);
        zone = TimeZoneInfo.Utc;
        var name = id?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var found = find(name) ?? (WindowsNames.TryGetValue(name, out var windowsName) ? find(windowsName) : null);
        if (found is null)
        {
            return false;
        }

        zone = found;
        return true;
    }

    private static TimeZoneInfo? SystemZone(string name)
        => TimeZoneInfo.TryFindSystemTimeZoneById(name, out var zone) ? zone : null;
}
