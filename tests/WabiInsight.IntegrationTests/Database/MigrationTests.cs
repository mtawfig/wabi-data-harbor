using Microsoft.EntityFrameworkCore;

namespace WabiInsight.IntegrationTests.Database;

[Collection(DatabaseCollectionDefinition.Name)]
public class MigrationTests
{
    private readonly SqlServerTestDatabase _db;

    public MigrationTests(SqlServerTestDatabase db)
    {
        _db = db;
    }

    [Fact]
    public async Task Model_HasNoPendingChanges_AfterLatestMigration()
    {
        await using var context = _db.CreateContext(TestData.SystemPrincipal);

        Assert.False(context.Database.HasPendingModelChanges(), "Add a migration: the model differs from the last snapshot.");
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Theory]
    [InlineData("Organizations")]
    [InlineData("Administrators")]
    [InlineData("OrganizationUsers")]
    [InlineData("LoginIdentities")]
    [InlineData("SystemInitialization")]
    [InlineData("PlatformSettings")]
    [InlineData("AuditLogs")]
    public async Task Migration_CreatesExpectedTable(string table)
    {
        await using var context = _db.CreateContext(TestData.SystemPrincipal);

        var count = await context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = {table}")
            .SingleAsync();

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task OrganizationUsers_OrganizationId_IsNotNullableWithForeignKey()
    {
        await using var context = _db.CreateContext(TestData.SystemPrincipal);

        var isNullable = await context.Database
            .SqlQuery<string>($"SELECT IS_NULLABLE AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OrganizationUsers' AND COLUMN_NAME = 'OrganizationId'")
            .SingleAsync();
        var foreignKeys = await context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE name = 'FK_OrganizationUsers_Organizations_OrganizationId'")
            .SingleAsync();

        Assert.Equal("NO", isNullable);
        Assert.Equal(1, foreignKeys);
    }
}
