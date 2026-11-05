using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for `dotnet ef migrations add`. It never connects: migrations are generated
/// from the model. Uses the WI_DESIGN_CONNECTION variable only if a developer wants `dotnet ef database update`.
/// </summary>
public sealed class ConfigurationDbContextFactory : IDesignTimeDbContextFactory<ConfigurationDbContext>
{
    public ConfigurationDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("WI_DESIGN_CONNECTION")
            ?? "Server=localhost,1433;Database=WabiInsight;User Id=sa;Password=design-time-only;TrustServerCertificate=True;Encrypt=True";

        var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(ConfigurationDbContext).Assembly.FullName))
            .Options;

        return new ConfigurationDbContext(options, new TenantContext(), new SystemClock());
    }
}
