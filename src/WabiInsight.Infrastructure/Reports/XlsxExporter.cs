using System.Globalization;
using ClosedXML.Excel;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Analysis;

namespace WabiInsight.Infrastructure.Reports;

/// <summary>
/// XLSX export with ClosedXML (specification §20): a Summary sheet (title, question, narrative, highlights,
/// KPIs, chart images) and one sheet per panel with typed cells; right-to-left sheets for Arabic.
/// </summary>
public sealed class XlsxExporter : IAnswerExporter
{
    public ExportFormat Format => ExportFormat.Xlsx;

    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public string Extension => "xlsx";

    public byte[] Export(ExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rtl = request.Language == "ar";
        var content = request.Content;
        using var workbook = new XLWorkbook();
        var summary = workbook.AddWorksheet(SummarySheetName(rtl));
        summary.RightToLeft = rtl;
        summary.Column(1).Width = 110;
        var row = 1;
        summary.Cell(row, 1).Value = content.Title.Length > 0 ? content.Title : "Report";
        summary.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(18).Font.SetFontColor(XLColor.FromHtml("#0B1F3A"));
        row++;
        if (content.Subtitle.Length > 0)
        {
            summary.Cell(row++, 1).SetValue(content.Subtitle).Style.Font.SetFontColor(XLColor.FromHtml("#64748B"));
        }

        summary.Cell(row++, 1).SetValue($"{(rtl ? "أُعدّ لـ" : "Prepared for")} {request.OrganizationName} · {(rtl ? "المصدر" : "Source")}: {request.ApplicationName} · {(rtl ? "أُنشئ في" : "Generated")} {request.GeneratedAt:yyyy-MM-dd HH:mm} UTC").Style.Font.SetFontColor(XLColor.FromHtml("#64748B"));
        row++;
        summary.Cell(row++, 1).SetValue(rtl ? "السؤال" : "Question").Style.Font.SetBold();
        summary.Cell(row++, 1).SetValue(request.Prompt).Style.Alignment.SetWrapText();
        row++;
        if (content.Summary.Length > 0)
        {
            summary.Cell(row++, 1).SetValue(rtl ? "الملخص التنفيذي" : "Executive summary").Style.Font.SetBold();
            summary.Cell(row++, 1).SetValue(content.Summary).Style.Alignment.SetWrapText();
            row++;
        }

        if (content.KeyFindings.Count > 0)
        {
            summary.Cell(row++, 1).SetValue(rtl ? "أبرز النتائج" : "Key findings").Style.Font.SetBold();
            foreach (var finding in content.KeyFindings)
            {
                summary.Cell(row++, 1).SetValue("• " + finding).Style.Alignment.SetWrapText();
            }

            row++;
        }

        var kpis = request.Answer.Panels.Where(p => p.Plan.Visualization?.Type == "kpi" && p.Result.Rows.Count > 0).ToList();
        if (kpis.Count > 0)
        {
            summary.Cell(row++, 1).SetValue(rtl ? "الأرقام الرئيسية" : "Headline numbers").Style.Font.SetBold();
            foreach (var panel in kpis)
            {
                var numeric = panel.Result.Columns.ToList().FindIndex(c => c.IsNumeric);
                var value = panel.Result.Rows[0][numeric >= 0 ? numeric : 0];
                summary.Cell(row, 1).SetValue(panel.Title.Length > 0 ? panel.Title : panel.Plan.Objective);
                SetTyped(summary.Cell(row, 2), value, isNumeric: true);
                summary.Cell(row, 2).Style.Font.SetBold().Font.SetFontColor(XLColor.FromHtml("#087DF2"));
                row++;
            }

            row++;
        }

        if (content.Recommendations.Count > 0)
        {
            summary.Cell(row++, 1).SetValue(rtl ? "التوصيات" : "Recommendations").Style.Font.SetBold();
            var n = 0;
            foreach (var rec in content.Recommendations)
            {
                n++;
                summary.Cell(row++, 1).SetValue($"{n.ToString(CultureInfo.InvariantCulture)}. {rec.Title}: {rec.Detail}").Style.Alignment.SetWrapText();
            }

            row++;
        }

        foreach (var panel in request.Answer.Panels)
        {
            var type = panel.Plan.Visualization?.Type ?? "table";
            if (type is "bar" or "line" or "pie" && panel.Result.Series.Count > 0)
            {
                row++;
                summary.Cell(row++, 1).SetValue(panel.Title.Length > 0 ? panel.Title : panel.Plan.Objective).Style.Font.SetBold();
                var chart = ChartImageRenderer.RenderImage(type, ColumnHeaders.Series(panel, rtl), culture: DocumentCulture.For(rtl));
                using var png = new MemoryStream(chart.Png);
                // Rows above may be taller than the default because of wrapped text; anchor one row lower and leave room below the
                // image (default rows are 20 px; a chart with angled labels is taller than 340 px).
                summary.AddPicture(png).MoveTo(summary.Cell(row + 1, 1)).WithSize(chart.Width, chart.Height);
                row += (int)Math.Ceiling(chart.Height / 20d) + 5;
            }
        }

        var sheetNames = PanelSheetNames(request.Answer, rtl);
        var index = 0;
        foreach (var panel in request.Answer.Panels)
        {
            index++;
            var sheet = workbook.AddWorksheet(sheetNames[index - 1]);
            sheet.RightToLeft = rtl;
            var columns = panel.Result.Columns;
            var names = ColumnHeaders.For(panel, rtl);
            for (var c = 0; c < columns.Count; c++)
            {
                var header = sheet.Cell(1, c + 1);
                header.Value = ColumnHeaders.Header(names, columns[c].Name);
                header.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#E8EFF8"));
            }

            var r = 2;
            foreach (var dataRow in panel.Result.Rows)
            {
                for (var c = 0; c < columns.Count; c++)
                {
                    SetTyped(sheet.Cell(r, c + 1), dataRow[c], columns[c].IsNumeric);
                }

                r++;
            }

            if (panel.Result.Rows.Count > 0)
            {
                sheet.Range(1, 1, r - 1, columns.Count).SetAutoFilter();
                sheet.SheetView.FreezeRows(1);
            }

            sheet.Columns().AdjustToContents(1, Math.Min(r, 200));
            foreach (var column in sheet.Columns())
            {
                column.Width = Math.Min(60, Math.Max(10, column.Width));
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void SetTyped(IXLCell cell, object? value, bool isNumeric)
    {
        switch (value)
        {
            case null:
                cell.Clear();
                break;
            case System.Text.Json.JsonElement element:
                SetTyped(cell, element.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.Null => null,
                    _ => element.GetString(),
                }, isNumeric);
                break;
            case bool b:
                cell.Value = b;
                break;
            case DateTime dt:
                cell.Value = dt;
                cell.Style.DateFormat.Format = dt.TimeOfDay == TimeSpan.Zero ? "yyyy-mm-dd" : "yyyy-mm-dd hh:mm";
                break;
            case DateTimeOffset dto:
                cell.Value = dto.DateTime;
                cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                break;
            case string s when isNumeric && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                cell.Value = parsed;
                break;
            case string s when DateTime.TryParseExact(s, ["yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date):
                cell.Value = date;
                cell.Style.DateFormat.Format = date.TimeOfDay == TimeSpan.Zero ? "yyyy-mm-dd" : "yyyy-mm-dd hh:mm";
                break;
            case string s:
                cell.Value = s;
                break;
            default:
                if (ResultCalculator.ToDouble(value) is { } number)
                {
                    cell.Value = number;
                }
                else
                {
                    cell.Value = ResultCalculator.Format(value);
                }

                break;
        }
    }

    /// <summary>The name of the Summary sheet in the workbook's language.</summary>
    internal static string SummarySheetName(bool rtl) => rtl ? "الملخص" : "Summary";

    /// <summary>The sheet of each panel, in the answer's order.</summary>
    internal static IReadOnlyList<string> PanelSheetNames(AskResponse answer, bool rtl)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SummarySheetName(rtl) };
        return answer.Panels.Select((panel, i) => SheetName(panel.Title.Length > 0 ? panel.Title : panel.Plan.Objective, i + 1, used)).ToList();
    }

    /// <summary>Excel sheet names: at most 31 characters, none of []:*?/\, unique within the workbook.</summary>
    internal static string SheetName(string title, int index, ISet<string> used)
    {
        var cleaned = new string(title.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            cleaned = $"Panel {index}";
        }

        var prefix = $"{index}. ";
        var name = prefix + (cleaned.Length > 31 - prefix.Length ? cleaned[..(31 - prefix.Length)].TrimEnd() : cleaned);
        var candidate = name;
        var n = 2;
        while (!used.Add(candidate))
        {
            var suffix = $" ({n++})";
            candidate = name[..Math.Min(name.Length, 31 - suffix.Length)] + suffix;
        }

        return candidate;
    }
}
