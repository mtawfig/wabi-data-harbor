using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Infrastructure.Databases;

namespace WabiInsight.UnitTests.Applications;

public sealed class SqlServerAdapterTests
{
    private readonly SqlServerAdapter _adapter = new();

    [Fact]
    public void Normalize_ForcesReadOnlyIntent_ShortTimeout_AndStableOrdering()
    {
        var a = _adapter.Normalize("Server=db1,1433;Database=Sales;User Id=reader;Password=p@ss;TrustServerCertificate=True");
        var b = _adapter.Normalize("database=Sales; server=db1,1433 ;User Id=reader;Password=p@ss;TrustServerCertificate=True");
        Assert.Equal(a, b);
        Assert.Contains("Application Intent=ReadOnly", a, StringComparison.Ordinal);
        Assert.Contains("Connect Timeout=10", a, StringComparison.Ordinal);
        Assert.Contains("Application Name=WabiInsight", a, StringComparison.Ordinal);
        Assert.Equal("db1,1433 / Sales", _adapter.Hint(a));
    }

    [Theory]
    [InlineData("", "connection_string_server_required")]
    [InlineData("Server=;Database=x", "connection_string_server_required")]
    [InlineData("Server=db1", "connection_string_database_required")]
    [InlineData("this is not a connection string", "connection_string_invalid")]
    public void Normalize_RejectsUnusableInput(string input, string code)
    {
        var ex = Assert.Throws<ArgumentException>(() => _adapter.Normalize(input));
        Assert.StartsWith(code, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_ReturnsFailure_WithoutThrowing_ForUnreachableServer()
    {
        var result = await _adapter.TestAsync("Server=127.0.0.1,1;Database=x;User Id=u;Password=p;Connect Timeout=3;TrustServerCertificate=True");
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.DoesNotContain("Password", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Factory_ResolvesByType_AndRejectsPlatformInsights()
    {
        var factory = new DatabaseAdapterFactory([new SqlServerAdapter(), new OracleAdapter()]);
        Assert.Equal(DatabaseType.Oracle, factory.Resolve(DatabaseType.Oracle).DatabaseType);
        Assert.Throws<ArgumentException>(() => factory.Resolve(DatabaseType.PlatformInsights));
    }

    [Fact]
    public void Oracle_Normalize_RequiresDataSourceAndUser()
    {
        var oracle = new OracleAdapter();
        Assert.Throws<ArgumentException>(() => oracle.Normalize("User Id=scott;Password=tiger"));
        var normalized = oracle.Normalize("Data Source=orcl.example:1521/XEPDB1;User Id=reader;Password=p");
        Assert.Contains("CONNECTION TIMEOUT=10", normalized.ToUpperInvariant(), StringComparison.Ordinal);
        Assert.Equal("orcl.example:1521/XEPDB1 / reader", oracle.Hint(normalized));
    }
}
