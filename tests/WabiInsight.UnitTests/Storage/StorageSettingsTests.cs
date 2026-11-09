using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Core.Storage;
using WabiInsight.Infrastructure.DependencyInjection;
using WabiInsight.Infrastructure.Storage;

namespace WabiInsight.UnitTests.Storage;

/// <summary>Storage:Provider picks the server disk (default) or Azure Blob Storage; bad settings are refused clearly.</summary>
public sealed class StorageSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wi-storage-unit", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ServiceProvider Services(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:ConfigurationDatabase"] = "Server=localhost;Database=Unused;TrustServerCertificate=True";
        settings["Storage:RootPath"] = _root;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddWabiInsightInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void TheServerDiskIsTheDefault()
    {
        Assert.Null(new StorageOptions().Problem());
        using var services = Services([]);
        Assert.IsType<FileSystemArtifactStorage>(services.GetRequiredService<IArtifactStorage>());
    }

    [Fact]
    public void AzureBlob_IsChosenByTheSetting_WithoutContactingAzure()
    {
        using var services = Services(new()
        {
            ["Storage:Provider"] = "azureblob",
            ["Storage:AzureBlob:ConnectionString"] = "UseDevelopmentStorage=true",
            ["Storage:AzureBlob:ContainerName"] = "wabi-test-files",
        });
        var storage = Assert.IsType<AzureBlobArtifactStorage>(services.GetRequiredService<IArtifactStorage>());
        Assert.EndsWith("/wabi-test-files", storage.ContainerUri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public void AzureBlob_WithoutAConnectionString_IsRefusedWithAClearMessage()
    {
        var options = new StorageOptions { Provider = StorageProviders.AzureBlob };
        var problem = options.Problem();
        Assert.NotNull(problem);
        Assert.Contains("Storage:AzureBlob:ConnectionString", problem, StringComparison.Ordinal);
        Assert.Contains("Storage__AzureBlob__ConnectionString", problem, StringComparison.Ordinal);

        using var services = Services(new() { ["Storage:Provider"] = "AzureBlob" });
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IArtifactStorage>());
    }

    [Theory]
    [InlineData("Disk")]
    [InlineData("S3")]
    [InlineData("")]
    public void UnknownProviders_AreRefused(string provider)
    {
        Assert.Contains("Storage:Provider", new StorageOptions { Provider = provider }.Problem(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wabi-insight-files", true)]
    [InlineData("abc", true)]
    [InlineData("ab", false)]
    [InlineData("Wabi-Files", false)]
    [InlineData("wabi--files", false)]
    [InlineData("-wabi", false)]
    [InlineData("wabi_files", false)]
    public void ContainerNames_FollowAzureRules(string name, bool valid)
    {
        Assert.Equal(valid, ArtifactKeys.IsValidContainer(name));
        var options = new StorageOptions { Provider = StorageProviders.AzureBlob, AzureBlob = { ConnectionString = "UseDevelopmentStorage=true", ContainerName = name } };
        Assert.Equal(valid, options.Problem() is null);
    }

    [Fact]
    public void Keys_AreRandomAndValidated()
    {
        var key = ArtifactKeys.New("analysis-reports", new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero));
        Assert.Matches("^analysis-reports/2026/11/[0-9a-f]{32}\\.bin$", key);
        Assert.True(ArtifactKeys.IsValidKey(key));
        Assert.False(ArtifactKeys.IsValidKey("../analysis-reports/2026/11/" + new string('a', 32) + ".bin"));
        Assert.False(ArtifactKeys.IsValidKey("analysis-reports/2026/11/report.pdf"));
        Assert.Throws<ArgumentException>(() => ArtifactKeys.New("Bad Category", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task TheDisk_ListsItsStoredFiles_ForTheCopyCommand()
    {
        var disk = new FileSystemArtifactStorage(Options.Create(new StorageOptions { RootPath = _root }), new SystemClock());
        using var one = new MemoryStream([1, 2, 3]);
        using var two = new MemoryStream([4, 5]);
        var first = await disk.SaveAsync("analysis-reports", one);
        var second = await disk.SaveAsync("support-attachments", two);
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.bin"), "not a storage key");

        Assert.Equal(new[] { first, second }.Order(StringComparer.Ordinal), disk.ListKeys().Order(StringComparer.Ordinal));
    }
}
