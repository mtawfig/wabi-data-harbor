using Testcontainers.Azurite;

namespace WabiInsight.IntegrationTests.Storage;

/// <summary>
/// An Azure Blob Storage endpoint for the storage tests: Azurite, Microsoft's local Azure Storage emulator.
/// Resolution order: WI_TEST_AZURITE_CONNECTION (a running Azurite, for example `azurite-blob --skipApiVersionCheck`),
/// then a Testcontainers Azurite container when Docker is available.
/// </summary>
public sealed class AzuriteStorage : IAsyncLifetime
{
    public const string ConnectionVariable = "WI_TEST_AZURITE_CONNECTION";

    private AzuriteContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            ConnectionString = fromEnvironment;
            return;
        }

        try
        {
            // The newest Azure SDK can be ahead of the emulator's API version list: skip that check (the protocol is the same).
            _container = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest").WithInMemoryPersistence().WithCommand("--skipApiVersionCheck").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Storage tests need Azurite. Set {ConnectionVariable} to its connection string (UseDevelopmentStorage=true for a local " +
                "azurite-blob on port 10000) or make Docker available for Testcontainers.", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>A container name no other test uses.</summary>
    public static string NewContainerName() => $"wi-test-{Guid.NewGuid():N}"[..40];
}
