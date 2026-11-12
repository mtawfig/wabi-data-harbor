using System.Text.Json;
using WabiInsight.Core.Platform;

namespace WabiInsight.UnitTests.Platform;

public sealed class AuditDetailReducerTests
{
    private static JsonElement Parse(string? json) => JsonElement.Parse(json!);

    private static string[] Withheld(string? json) => [.. Parse(json).GetProperty("withheld").EnumerateArray().Select(w => w.GetString()!)];

    [Fact]
    public void NamesAndFreeText_AreWithheld_WhileCodesNumbersFlagsIdsAndDatesStay()
    {
        var objectId = Guid.NewGuid();
        var (json, reduced) = AuditDetailReducer.Reduce($$"""
            {"objectId":"{{objectId}}","schemaName":"hr","objectName":"Salaries","isApproved":true,"columns":4,
             "hint":"sql-prod-01 / Payroll","title":"Q3 redundancy plan","classification":"Restricted",
             "expiresAt":"2026-11-12T08:30:00+00:00","note":"call Sara first","referenceNumber":"WI-2026-0042"}
            """);

        Assert.True(reduced);
        var root = Parse(json);
        Assert.Equal(objectId, root.GetProperty("objectId").GetGuid());
        Assert.True(root.GetProperty("isApproved").GetBoolean());
        Assert.Equal(4, root.GetProperty("columns").GetInt32());
        Assert.Equal("Restricted", root.GetProperty("classification").GetString());
        Assert.Equal("WI-2026-0042", root.GetProperty("referenceNumber").GetString());
        Assert.Equal("2026-11-12T08:30:00+00:00", root.GetProperty("expiresAt").GetString());
        Assert.Equal(["hint", "note", "objectName", "schemaName", "title"], Withheld(json));
        foreach (var secret in new[] { "Salaries", "sql-prod-01", "Payroll", "redundancy", "Sara", "\"hr\"" })
        {
            Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NestedObjectsAndLists_AreReducedTheSameWay()
    {
        var roleId = Guid.NewGuid();
        var (json, reduced) = AuditDetailReducer.Reduce($$"""
            {"before":{"granted":false,"scope":"Department","roleName":"Payroll clerks"},
             "after":{"granted":true,"allColumns":true,"columns":-1,"scope":"All"},
             "recipients":["sara@org-a.test","omar@org-a.test"],
             "operations":[{"operation":"CreateOrganizationUser","targetReferences":["{{roleId}}","email:new.user@org-a.test","new"]}],
             "types":["Anthropic","OpenAiCompatible"],"roleCodes":["Management","PayrollClerks"]}
            """);

        Assert.True(reduced);
        var root = Parse(json);
        Assert.Equal("Department", root.GetProperty("before").GetProperty("scope").GetString());
        Assert.False(root.GetProperty("before").TryGetProperty("roleName", out _));
        Assert.Equal(-1, root.GetProperty("after").GetProperty("columns").GetInt32());
        Assert.Empty(root.GetProperty("recipients").EnumerateArray());
        var operation = root.GetProperty("operations")[0];
        Assert.Equal("CreateOrganizationUser", operation.GetProperty("operation").GetString());
        Assert.Equal(roleId, Assert.Single(operation.GetProperty("targetReferences").EnumerateArray()).GetGuid()); // the address and "new" are not identifiers
        Assert.Equal(2, root.GetProperty("types").GetArrayLength());
        Assert.Empty(root.GetProperty("roleCodes").EnumerateArray()); // an organization's own role codes are its configuration
        Assert.Equal(["recipients", "roleCodes", "roleName", "targetReferences"], Withheld(json));
        Assert.DoesNotContain("@", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Payroll", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("reason", "locked_out", true)]            // a platform code
    [InlineData("reason", "Their manager asked by phone", false)]  // the same field holding free text
    [InlineData("code", "HR", false)]                     // also holds role and application codes, which an organization defines
    [InlineData("status", "Resolved", true)]
    [InlineData("name", "Finance", false)]
    [InlineData("model", "claude-opus-5-5", false)]
    [InlineData("fileName", "salaries.xlsx", false)]
    [InlineData("email", "sara@org-a.test", false)]
    public void ATextValueIsShown_OnlyInACodeField_AndOnlyWhenItLooksLikeACode(string field, string value, bool shown)
    {
        var (json, reduced) = AuditDetailReducer.Reduce(JsonSerializer.Serialize(new Dictionary<string, object> { [field] = value, ["count"] = 1 }));
        Assert.Equal(!shown, reduced);
        Assert.Equal(shown, Parse(json).TryGetProperty(field, out _));
        Assert.Equal(1, Parse(json).GetProperty("count").GetInt32());
    }

    [Fact]
    public void NothingToWithhold_LeavesTheDetailsAsTheyAre_AndUnreadableDetailsAreWithheldWhole()
    {
        const string Plain = """{"action":"created","isApproved":true,"bytes":2048}""";
        Assert.Equal((Plain, false), AuditDetailReducer.Reduce(Plain));
        Assert.Equal((null, false), AuditDetailReducer.Reduce(null));
        Assert.Equal(("", false), AuditDetailReducer.Reduce(""));
        Assert.Equal((null, true), AuditDetailReducer.Reduce("Salaries table approved by Sara"));

        var id = Guid.NewGuid().ToString();
        Assert.Equal(id, AuditDetailReducer.ReduceEntityId(id));
        Assert.Null(AuditDetailReducer.ReduceEntityId("hr.Salaries"));
        Assert.Null(AuditDetailReducer.ReduceEntityId(null));
    }
}
