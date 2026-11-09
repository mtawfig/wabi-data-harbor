using System.Net;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Infrastructure.Storage;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Storage;

/// <summary>
/// "Storage:Provider": "AzureBlob" against real Azure Blob Storage (Azurite): private container created on first use,
/// the same keys as on the disk, files saved on the disk before the switch still readable and copyable, and the API
/// keeping an organization's files in the container end to end.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class AzureBlobStorageTests : IClassFixture<AzuriteStorage>, IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly AzuriteStorage _azurite;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wi-azure-storage-tests", Guid.NewGuid().ToString("N"));

    public AzureBlobStorageTests(SqlServerTestDatabase db, AzuriteStorage azurite)
    {
        _db = db;
        _azurite = azurite;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private StorageOptions Settings(string container, bool readFromDisk = true) => new()
    {
        Provider = StorageProviders.AzureBlob,
        RootPath = _root,
        AzureBlob = new AzureBlobStorageOptions { ConnectionString = _azurite.ConnectionString, ContainerName = container, ReadFromDiskWhenMissing = readFromDisk },
    };

    private FileSystemArtifactStorage Disk() => new(Options.Create(new StorageOptions { RootPath = _root }), new SystemClock());

    private AzureBlobArtifactStorage Azure(string container, bool readFromDisk = true)
        => new(Options.Create(Settings(container, readFromDisk)), Disk(), new SystemClock(), NullLogger<AzureBlobArtifactStorage>.Instance);

    private static async Task<byte[]> ReadAllAsync(Stream? stream)
    {
        Assert.NotNull(stream);
        await using (stream)
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            return copy.ToArray();
        }
    }

    [Fact]
    public async Task SavesReadsAndDeletes_InAPrivateContainerItCreates()
    {
        var name = AzuriteStorage.NewContainerName();
        using var storage = Azure(name);
        byte[] payload = [1, 2, 3, 4, 5];

        string key;
        using (var content = new MemoryStream(payload))
        {
            key = await storage.SaveAsync("analysis-reports", content);
        }

        Assert.Matches("^analysis-reports/[0-9]{4}/[0-9]{2}/[0-9a-f]{32}\\.bin$", key);
        Assert.Equal(payload, await ReadAllAsync(await storage.OpenReadAsync(key)));

        var container = new BlobContainerClient(_azurite.ConnectionString, name);
        Assert.True(await container.ExistsAsync());
        Assert.Equal(PublicAccessType.None, (await container.GetAccessPolicyAsync()).Value.BlobPublicAccess);
        Assert.True(await container.GetBlobClient(key).ExistsAsync());
        Assert.False(Directory.Exists(_root) && Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Any());

        await storage.DeleteAsync(key);
        Assert.Null(await storage.OpenReadAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync("../secrets.bin"));
    }

    [Fact]
    public async Task FilesSavedOnTheDisk_StillOpen_AndAreCopiedUnderTheSameKey()
    {
        var name = AzuriteStorage.NewContainerName();
        var disk = Disk();
        byte[] payload = [9, 8, 7];
        string key;
        using (var content = new MemoryStream(payload))
        {
            key = await disk.SaveAsync("support-attachments", content);
        }

        using var storage = Azure(name);
        Assert.Equal(payload, await ReadAllAsync(await storage.OpenReadAsync(key)));
        using (var noFallback = Azure(name, readFromDisk: false))
        {
            Assert.Null(await noFallback.OpenReadAsync(key));
        }

        Assert.True(await storage.CopyFromDiskAsync(disk, key));
        Assert.False(await storage.CopyFromDiskAsync(disk, key));
        var blob = new BlobContainerClient(_azurite.ConnectionString, name).GetBlobClient(key);
        Assert.Equal(payload, (await blob.DownloadContentAsync()).Value.Content.ToArray());

        await storage.DeleteAsync(key);
        Assert.False(await blob.ExistsAsync());
        Assert.Null(await disk.OpenReadAsync(key));
    }

    [Fact]
    public async Task TheApi_KeepsAnOrganizationsFilesInTheContainer_WhenTheSettingSaysSo()
    {
        var name = AzuriteStorage.NewContainerName();
        using var factory = new ApiFactory(_db.ConnectionString, new Dictionary<string, string?>
        {
            ["Storage:Provider"] = StorageProviders.AzureBlob,
            ["Storage:RootPath"] = _root,
            ["Storage:AzureBlob:ConnectionString"] = _azurite.ConnectionString,
            ["Storage:AzureBlob:ContainerName"] = name,
        });
        using var alice = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await alice.LoginAsync(TestData.AliceEmail, TestData.UserPassword)).StatusCode);

        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        using (var form = new MultipartFormDataContent { { new ByteArrayContent(png) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") } }, "file", "logo.png" } })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await alice.PutAsync(new Uri("/api/organization/logo", UriKind.Relative), form)).StatusCode);
        }

        var logo = await alice.GetAsync("/api/organization/logo");
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal(png, await logo.Content.ReadAsByteArrayAsync());

        var blobs = new List<string>();
        await foreach (var blob in new BlobContainerClient(_azurite.ConnectionString, name).GetBlobsAsync(BlobTraits.None, BlobStates.None, "organization-logos/", CancellationToken.None))
        {
            blobs.Add(blob.Name);
        }

        Assert.Single(blobs);
        Assert.False(Directory.Exists(_root) && Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Any());
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync(new Uri("/api/organization/logo", UriKind.Relative))).StatusCode);
    }
}
