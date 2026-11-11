using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Applications;
using WabiInsight.Core.Common;
using WabiInsight.Core.Monitoring;
using WabiInsight.Infrastructure.Authentication;
using WabiInsight.Infrastructure.Health;
using WabiInsight.Infrastructure.Insights;
using WabiInsight.Infrastructure.Storage;

namespace WabiInsight.UnitTests.Platform;

/// <summary>P13.31: what a Windows server with other regional settings, paths and time zone names must not change.</summary>
public sealed class DeploymentReadinessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wi-readiness-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>The zones the organization form offers (src/web/src/app/core/organizations/organizations.models.ts).</summary>
    public static TheoryData<string> OfferedZones() =>
    [
        "Asia/Dubai", "Asia/Riyadh", "Asia/Qatar", "Asia/Kuwait", "Asia/Bahrain", "Asia/Muscat", "Africa/Cairo", "Asia/Amman", "Europe/London", "Europe/Berlin", "America/New_York",
    ];

    [Theory]
    [MemberData(nameof(OfferedZones))]
    public void EveryOfferedZone_HasAWindowsName_ThatKeepsTheSameClock(string iana)
    {
        Assert.True(TimeZones.WindowsNames.TryGetValue(iana, out var windowsName), $"{iana} has no Windows name.");
        var byIana = TimeZoneInfo.FindSystemTimeZoneById(iana);
        var byWindowsName = TimeZoneInfo.FindSystemTimeZoneById(windowsName!);

        // Winter and summer of two years: the same offset from UTC under either name, daylight saving included.
        foreach (var moment in new[] { new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 4, 2, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 10, 20, 9, 0, 0, TimeSpan.Zero) })
        {
            Assert.Equal(byIana.GetUtcOffset(moment), byWindowsName.GetUtcOffset(moment));
        }
    }

    [Fact]
    public void AServerThatKnowsWindowsNamesOnly_StillFindsTheOfferedZones()
    {
        // What .NET does on Windows without ICU: IANA names are unknown, Windows names and "UTC" are found.
        static TimeZoneInfo? WindowsOnly(string name) => name.Contains('/', StringComparison.Ordinal) ? null : TimeZoneInfo.FindSystemTimeZoneById(name);

        Assert.True(TimeZones.TryFind("Asia/Dubai", WindowsOnly, out var dubai));
        Assert.Equal(TimeSpan.FromHours(4), dubai.GetUtcOffset(new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero)));
        Assert.True(TimeZones.TryFind(" Africa/Cairo ", WindowsOnly, out var cairo));
        Assert.Equal(TimeSpan.FromHours(3), cairo.GetUtcOffset(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.True(TimeZones.TryFind("UTC", WindowsOnly, out var utc));
        Assert.Equal(TimeSpan.Zero, utc.BaseUtcOffset);

        // A zone that is not offered has no Windows name: unknown there, and the caller is told so.
        Assert.False(TimeZones.TryFind("Asia/Tokyo", WindowsOnly, out var fallback));
        Assert.Equal(TimeZoneInfo.Utc, fallback);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Mars/Olympus")]
    public void UnknownOrEmptyZone_IsNotFound_AndSchedulesFallBackToUtc(string? id)
    {
        Assert.False(TimeZones.TryFind(id, out _));
        Assert.Equal(TimeZoneInfo.Utc, JobRecurrence.ResolveTimeZone(id));
    }

    [Fact]
    public void StorageKeys_UseTheGregorianYearAndLatinDigits_UnderAnyRegionalFormat()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // Saudi Arabia's regional format counts years in the Um Al-Qura calendar (1448 for November 2026).
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var key = ArtifactKeys.New("analysis-reports", new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero));
            Assert.StartsWith("analysis-reports/2026/11/", key, StringComparison.Ordinal);
            Assert.True(ArtifactKeys.IsValidKey(key));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("//")]
    public async Task StorageRoot_WithATrailingSeparator_WorksLikeTheSameFolderWithout(string suffix)
    {
        var configured = _root + suffix.Replace('/', Path.DirectorySeparatorChar);
        var storage = new FileSystemArtifactStorage(Options.Create(new StorageOptions { RootPath = configured }), new SystemClock());

        using var content = new MemoryStream(Encoding.UTF8.GetBytes("report"));
        var key = await storage.SaveAsync("analysis-reports", content);
        await using (var read = await storage.OpenReadAsync(key))
        {
            Assert.NotNull(read);
        }

        Assert.Equal([key], storage.ListKeys().ToList());
        Assert.True(File.Exists(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar))));
        await storage.DeleteAsync(key);
        Assert.Null(await storage.OpenReadAsync(key));
    }

    [Fact]
    public void DeploymentSettings_NameEveryDevelopmentDefault_AndNothingOnceSet()
    {
        var defaults = DeploymentSettings.Find(new PlatformUrlOptions(), new StorageOptions(), new DataProtectionOptions(), new InsightsOptions(), ianaTimeZones: true);
        Assert.Equal(["platform_base_url", "storage_root_relative", "key_ring_relative", "insights_login_missing"], defaults.Select(f => f.Code).ToArray());

        var absolute = Path.Combine(Path.GetTempPath(), "wabi-insight");
        var set = DeploymentSettings.Find(
            new PlatformUrlOptions { BaseUrl = "https://insight.example.com" },
            new StorageOptions { RootPath = Path.Combine(absolute, "files") },
            new DataProtectionOptions { KeyRingPath = Path.Combine(absolute, "keys") },
            new InsightsOptions { ConnectionString = "Server=localhost;Database=WabiInsightDB;User Id=wabi_insight_insights;Password=not-a-real-one" },
            ianaTimeZones: true);
        Assert.Empty(set);

        // Files in Azure Blob Storage need no folder on the server; a plain http or local address is still reported.
        var blob = DeploymentSettings.Find(
            new PlatformUrlOptions { BaseUrl = "http://insight.example.com" },
            new StorageOptions { Provider = StorageProviders.AzureBlob },
            new DataProtectionOptions { KeyRingPath = Path.Combine(absolute, "keys") },
            new InsightsOptions { ConnectionString = "x" },
            ianaTimeZones: false);
        Assert.Equal(["platform_base_url", "time_zone_names"], blob.Select(f => f.Code).ToArray());

        // No message repeats a setting's value: the insights connection string never reaches a log or the health page.
        Assert.DoesNotContain(defaults.Concat(blob), f => f.Message.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }
}
