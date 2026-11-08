using WabiInsight.Core.Applications;
using WabiInsight.Core.Organizations;

namespace WabiInsight.UnitTests.Applications;

public sealed class ApplicationExamplesTests
{
    [Fact]
    public void Normalize_TrimsText_DropsEmptyRows_AndKeepsEitherLanguage()
    {
        var examples = ApplicationExamples.Normalize(
        [
            new("  Orders\u0007 per   month  ", "الطلبات شهريًا"),
            new(null, " أكبر العملاء "),
            new(" ", ""),
        ]);
        Assert.Equal([new ApplicationExampleQuestion("Orders per month", "الطلبات شهريًا"), new ApplicationExampleQuestion(null, "أكبر العملاء")], examples);
        Assert.Equal(examples, ApplicationExamples.Parse(ApplicationExamples.Serialize(examples)));
        Assert.Empty(ApplicationExamples.Parse("not json"));
        Assert.Empty(ApplicationExamples.Parse(null));
    }

    [Fact]
    public void Normalize_EnforcesTheCountAndLengthLimits()
    {
        var seven = Enumerable.Range(1, ApplicationExamples.MaxCount + 1).Select(i => new ApplicationExampleQuestion($"Question {i}", null)).ToList();
        Assert.Equal("application_examples_too_many", Assert.Throws<DomainException>(() => ApplicationExamples.Normalize(seven)).Message);
        Assert.Equal("application_example_too_long", Assert.Throws<DomainException>(() => ApplicationExamples.Normalize([new(null, new string('س', ApplicationExamples.MaxLength + 1))])).Message);
        Assert.Equal(ApplicationExamples.MaxCount, ApplicationExamples.Normalize(seven.Take(ApplicationExamples.MaxCount).Append(new(" ", null)).ToList()).Count);
    }
}
