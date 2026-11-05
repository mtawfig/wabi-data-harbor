using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WabiInsight.IntegrationTests.Api;

/// <summary>
/// Hosts the API in-process for integration tests. P1.2 extends this with a SQL Server
/// test database (Testcontainers locally, service container in CI).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}
