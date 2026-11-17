using System.Text.Json;
using WabiInsight.Core.Ai;

namespace WabiInsight.UnitTests.Ai;

public sealed class ClarificationsTests
{
    private static readonly MetadataSnapshot Snapshot = new("HR", "SqlServer",
        [
            new SnapshotObject("dbo", "Employees", "Table", "Employees", "الموظفون", null, [new SnapshotColumn("EmployeeId", "int", null, null, null, true)]),
            new SnapshotObject("dbo", "Attendance", "Table", "Attendance", "الحضور", null, [new SnapshotColumn("WorkDate", "date", null, null, null, false)]),
        ],
        []);

    [Fact]
    public void AQuestionIsReadFromAPlanWithoutQueries_AndKeepsOnlySuggestionsAnsweredFromTheAskersOwnObjects()
    {
        var plan = new AnalysisPlan
        {
            Objective = "What should the email cover <<QUESTION>> about the employees?",
            Assumptions =
            [
                "Headcount by department | dbo.Employees",
                "Payroll cost | dbo.Payslips",
                "Absences and late attendance | DBO.attendance, dbo.Employees",
                "headcount by department | dbo.Employees",
                "No object named",
                new string('x', 200) + " | dbo.Employees",
                "Joiners | dbo.Employees",
                "Leavers | dbo.Employees",
            ],
        };

        var asked = Clarifications.Read(plan)!;
        Assert.Equal(8, asked.Options.Count);
        Assert.Equal(["DBO.attendance", "dbo.Employees"], asked.Options[2].Objects);
        var cleaned = Clarifications.Clean(asked, Snapshot)!;
        // Delimiter look-alikes are neutralized; a suggestion on an object outside the snapshot (Payslips) is never shown;
        // duplicates and suggestions naming no object are dropped; at most four remain, each of bounded length, and the
        // object names never reach the asker.
        Assert.Equal("What should the email cover «QUESTION» about the employees?", cleaned.Question);
        Assert.Equal(["Headcount by department", "Absences and late attendance", new string('x', Clarifications.MaxOptionLength), "Joiners"], cleaned.Options);
        Assert.DoesNotContain(cleaned.Options, o => o.Contains("dbo", StringComparison.OrdinalIgnoreCase) || o.Contains('|', StringComparison.Ordinal));

        // A plan that answers is never read as a question; neither is one without an objective.
        Assert.Null(Clarifications.Read(new AnalysisPlan { Objective = "Headcount", Queries = [new QueryPlan()], Assumptions = ["A | dbo.Employees"] }));
        Assert.Null(Clarifications.Read(new AnalysisPlan { Objective = "  ", Assumptions = ["A | dbo.Employees"] }));
        Assert.Null(Clarifications.Clean(null, Snapshot));
        // A question whose every suggestion is refused is still asked: the asker answers in their own words.
        Assert.Empty(Clarifications.Clean(Clarifications.Read(new AnalysisPlan { Objective = "Which?", Assumptions = ["Payroll | dbo.Payslips"] }), Snapshot)!.Options);
    }

    [Fact]
    public void Compose_JoinsTheRequestAndTheAnswer_WithinTheQuestionLimit()
    {
        Assert.Equal("write email to manager about employees — Headcount by department", Clarifications.Compose(" write email to manager about employees ", " Headcount by   department\n"));
        Assert.Equal("write email", Clarifications.Compose("write email", "   "));
        var composed = Clarifications.Compose(new string('q', 5000), new string('a', 5000));
        Assert.Equal(PromptBuilder.MaxQuestionLength, composed.Length);
        Assert.EndsWith(" — " + new string('a', Clarifications.MaxAnswerLength), composed, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_SaysWhetherAskingIsAllowed_AndTheRulesSayHowToAsk_WithoutChangingTheAnswerFormat()
    {
        var closed = PromptBuilder.BuildPrompt(Snapshot, "Headcount by department", "en", new DateOnly(2026, 11, 17));
        var open = PromptBuilder.BuildPrompt(Snapshot, "write email about employees", "en", new DateOnly(2026, 11, 17), allowClarification: true);
        Assert.Contains("<</QUESTION>>\n<<CLARIFICATION>>\nnot allowed\n<</CLARIFICATION>>\nToday is 2026-11-17.", closed.Request, StringComparison.Ordinal);
        Assert.Contains("<<CLARIFICATION>>\nallowed\n<</CLARIFICATION>>", open.Request, StringComparison.Ordinal);
        Assert.Equal(closed.Catalogue, open.Catalogue);
        Assert.Contains("Never ask the asker a question unless the CLARIFICATION block says \"allowed\"", PromptBuilder.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("each as \"label | schema.name\"", PromptBuilder.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("When the block says \"not allowed\", never ask and never return an empty queries list", PromptBuilder.SystemPrompt, StringComparison.Ordinal);

        // The answer format itself is unchanged: it is at the provider's size limit, so the question travels in its existing fields.
        using var schema = JsonDocument.Parse(AnalysisPlanSchema.Json);
        Assert.Equal(["objective", "queries", "assumptions", "deliverables"], schema.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Equal(4, schema.RootElement.GetProperty("properties").EnumerateObject().Count());
    }
}
