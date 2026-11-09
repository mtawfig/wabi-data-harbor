using WabiInsight.Core.Ai;

namespace WabiInsight.Infrastructure.Reports;

/// <summary>
/// Turns a stored answer into report content for the exporters: the study text when the answer had one,
/// otherwise a plain document built from the verified narrative, highlights and panels (no model call).
/// </summary>
public static class AnswerReportBuilder
{
    public static ReportContent Build(AskResponse answer, string historyTitle, string prompt, string language)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var rtl = language == "ar";
        if (answer.Report is { } report)
        {
            return new ReportContent
            {
                Title = report.Title,
                Subtitle = report.Subtitle,
                // Studies written before P13.16 may carry "Figure:" labels; the exported document captions its figures itself.
                Introduction = ReportComposer.WithoutCaptionLabels(report.Introduction),
                Sections = report.Sections.Select(s => new ReportSection { Title = s.Title, Body = ReportComposer.WithoutCaptionLabels(s.Body), Panels = [.. s.Panels] }).ToList(),
                Summary = ReportComposer.WithoutCaptionLabels(report.Summary),
                KeyFindings = [.. report.KeyFindings],
                Recommendations = report.Recommendations.Select(r => new ReportRecommendation { Title = r.Title, Detail = r.Detail }).ToList(),
            };
        }

        var panels = Enumerable.Range(0, answer.Panels.Count).ToList();
        var kpis = panels.Where(i => answer.Panels[i].Plan.Visualization?.Type == "kpi").ToList();
        var rest = panels.Except(kpis).ToList();
        var sections = new List<ReportSection>();
        if (kpis.Count > 0)
        {
            sections.Add(new ReportSection { Title = rtl ? "الأرقام الرئيسية" : "Headline numbers", Body = string.Empty, Panels = kpis });
        }

        if (rest.Count > 0)
        {
            sections.Add(new ReportSection { Title = rtl ? "النتائج" : "Results", Body = string.Empty, Panels = rest });
        }

        return new ReportContent
        {
            Title = string.IsNullOrWhiteSpace(historyTitle) ? answer.Objective : historyTitle,
            Subtitle = answer.Objective == historyTitle ? string.Empty : answer.Objective,
            Introduction = (rtl ? "السؤال: " : "Question: ") + prompt,
            Sections = sections,
            Summary = answer.Interpretation?.Narrative ?? string.Empty,
            KeyFindings = [.. answer.Interpretation?.Highlights ?? []],
            Recommendations = [],
        };
    }
}
