using System.Text;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Infrastructure.Storage;
using WabiInsight.Infrastructure.Support;

namespace WabiInsight.UnitTests.Support;

public sealed class FileSystemArtifactStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wi-storage-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Save_UsesRandomKeys_AndRoundTrips()
    {
        var storage = new FileSystemArtifactStorage(Options.Create(new StorageOptions { RootPath = _root }), new SystemClock());
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var key = await storage.SaveAsync("support-attachments", content);

        Assert.Matches("^support-attachments/[0-9]{4}/[0-9]{2}/[0-9a-f]{32}\\.bin$", key);
        await using var read = await storage.OpenReadAsync(key);
        Assert.NotNull(read);
        using var reader = new StreamReader(read!);
        Assert.Equal("hello", await reader.ReadToEndAsync());

        await storage.DeleteAsync(key);
        Assert.Null(await storage.OpenReadAsync(key));
    }

    [Fact]
    public async Task Delete_WorksWhileTheFileIsBeingRead_AndTheReaderKeepsItsContent()
    {
        var storage = new FileSystemArtifactStorage(Options.Create(new StorageOptions { RootPath = _root }), new SystemClock());
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("a logo being downloaded"));
        var key = await storage.SaveAsync("organization-logos", content);
        var folder = Path.GetDirectoryName(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)))!;

        await using (var download = await storage.OpenReadAsync(key))
        {
            Assert.NotNull(download);

            // Windows keeps an open file locked; the delete must not wait for the download, nor fail.
            await storage.DeleteAsync(key);
            Assert.Null(await storage.OpenReadAsync(key));
            Assert.Empty(storage.ListKeys());

            // The same key can be written again at once (a replaced logo gets a new key, but nothing blocks this name).
            using var reader = new StreamReader(download!);
            Assert.Equal("a logo being downloaded", await reader.ReadToEndAsync());
        }

        // Nothing is left behind once the download has closed.
        Assert.Empty(Directory.GetFiles(folder));
        await storage.DeleteAsync(key);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("support-attachments/2026/11/not-a-guid.bin")]
    [InlineData("C:\\Windows\\win.ini")]
    public async Task Open_RejectsKeysThatAreNotStorageKeys(string key)
    {
        var storage = new FileSystemArtifactStorage(Options.Create(new StorageOptions { RootPath = _root }), new SystemClock());
        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(key));
    }

    [Fact]
    public void Reference_FormatIsYearAndSixDigits()
    {
        Assert.Equal("WI-2026-000001", SupportTicketService.Format(2026, 1));
        Assert.Equal("WI-2027-123456", SupportTicketService.Format(2027, 123456));
    }
}
