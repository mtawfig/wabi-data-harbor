using System.Globalization;
using System.Reflection;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WabiInsight.Core.Ai;

namespace WabiInsight.Infrastructure.Reports;

/// <summary>
/// Renders a report to PDF with QuestPDF (specification §20, review C1): A4, the organization's logo on the
/// top-left of every page, page numbers, executive summary, sections with charts (SVG drawn from the panel
/// series) and tables (panel rows) and recommendations. Business content only: no methodology, query or row-count
/// details (they stay in the server's query log). EN left-to-right with Inter, AR right-to-left with IBM Plex Sans Arabic.
/// </summary>
public sealed class QuestPdfReportRenderer : IReportRenderer
{
    private const string LatinFont = "Inter";
    private const string ArabicFont = "IBM Plex Sans Arabic";
    private const string Ocean = "#0B1F3A";
    private const string Blue = "#087DF2";
    private const string Muted = "#64748B";
    private const string Border = "#E2E8F0";
    private const int MaxTableRows = 60;
    private static readonly object FontLock = new();
    private static bool _fontsRegistered;

    public QuestPdfReportRenderer()
    {
        EnsureFonts();
    }

    public byte[] RenderPdf(ReportRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rtl = request.Language == "ar";
        var font = rtl ? ArabicFont : LatinFont;
        var culture = rtl ? CultureInfo.GetCultureInfo("ar-AE") : CultureInfo.InvariantCulture;
        var content = request.Content;
        var generatedOn = request.GeneratedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.7f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily(font, LatinFont, ArabicFont).FontSize(10.5f).FontColor("#1E293B").LineHeight(1.35f));
                if (rtl)
                {
                    page.ContentFromRightToLeft();
                }

                page.Header().Element(header => Header(header, request, content.Title, rtl));
                page.Content().PaddingTop(8).Column(column =>
                {
                    column.Spacing(10);
                    column.Item().PaddingBottom(4).Column(title =>
                    {
                        title.Item().Text(content.Title.Length > 0 ? content.Title : "Report").FontSize(24).SemiBold().FontColor(Ocean);
                        if (content.Subtitle.Length > 0)
                        {
                            title.Item().Text(content.Subtitle).FontSize(12.5f).FontColor(Muted);
                        }

                        title.Item().PaddingTop(4).Text(t =>
                        {
                            t.DefaultTextStyle(s => s.FontSize(9.5f).FontColor(Muted));
                            t.Span(rtl ? "أُعدّ لـ " : "Prepared for ").SemiBold();
                            t.Span(request.PreparedFor);
                            t.Span("   ·   ");
                            t.Span(rtl ? "المصدر: " : "Source: ").SemiBold();
                            t.Span(request.ApplicationName);
                            t.Span("   ·   ");
                            t.Span(rtl ? "أُنشئ في " : "Generated ").SemiBold();
                            t.Span(generatedOn);
                        });
                    });

                    if (content.Summary.Length > 0)
                    {
                        column.Item().Background("#F1F7FF").Border(1).BorderColor("#D6E8FF").Padding(12).Column(box =>
                        {
                            box.Item().PaddingBottom(4).Text(rtl ? "الملخص التنفيذي" : "Executive summary").FontSize(13).SemiBold().FontColor(Ocean);
                            Paragraphs(box, content.Summary);
                        });
                    }

                    if (content.KeyFindings.Count > 0)
                    {
                        column.Item().Column(findings =>
                        {
                            findings.Item().PaddingBottom(2).Text(rtl ? "أبرز النتائج" : "Key findings").FontSize(13).SemiBold().FontColor(Ocean);
                            foreach (var finding in content.KeyFindings)
                            {
                                findings.Item().Row(row =>
                                {
                                    row.ConstantItem(14).Text("•").FontColor(Blue).SemiBold();
                                    row.RelativeItem().Text(finding);
                                });
                            }
                        });
                    }

                    if (content.Introduction.Length > 0)
                    {
                        column.Item().Column(intro =>
                        {
                            intro.Item().PaddingTop(6).PaddingBottom(2).Text(rtl ? "المقدمة" : "Introduction").FontSize(15).SemiBold().FontColor(Ocean);
                            Paragraphs(intro, content.Introduction);
                        });
                    }

                    var sectionNumber = 0;
                    foreach (var section in content.Sections)
                    {
                        sectionNumber++;
                        var number = sectionNumber;
                        column.Item().Column(sec =>
                        {
                            sec.Item().PaddingTop(6).PaddingBottom(2).Text($"{number.ToString(culture)}. {StripNumbering(section.Title)}").FontSize(15).SemiBold().FontColor(Ocean);
                            Paragraphs(sec, section.Body);
                            var kpis = section.Panels.Where(i => request.Panels[i].Plan.Visualization?.Type == "kpi" && request.Panels[i].Result.Rows.Count > 0).ToList();
                            if (kpis.Count > 0)
                            {
                                sec.Item().PaddingTop(4).Row(row =>
                                {
                                    row.Spacing(8);
                                    foreach (var i in kpis.Take(5))
                                    {
                                        var panel = request.Panels[i];
                                        var numeric = panel.Result.Columns.ToList().FindIndex(c => c.IsNumeric);
                                        var value = panel.Result.Rows[0][numeric >= 0 ? numeric : 0];
                                        row.RelativeItem().Border(1).BorderColor(Border).Background("#F8FBFF").Padding(8).Column(kpi =>
                                        {
                                            kpi.Item().Text(FormatValue(value, isNumeric: true, culture)).FontSize(18).SemiBold().FontColor(Blue);
                                            kpi.Item().Text(panel.Title.Length > 0 ? panel.Title : panel.Plan.Objective).FontSize(8.5f).FontColor(Muted);
                                        });
                                    }
                                });
                            }

                            foreach (var i in section.Panels.Where(i => !kpis.Contains(i)))
                            {
                                var panel = request.Panels[i];
                                var type = panel.Plan.Visualization?.Type ?? "table";
                                var isChart = type is "bar" or "line" or "pie" && panel.Result.Series.Count > 0;
                                sec.Item().PaddingTop(6).Column(figure =>
                                {
                                    figure.Item().PaddingBottom(2).Text($"{(rtl ? (isChart ? "شكل" : "جدول") : (isChart ? "Figure" : "Table"))} {number.ToString(culture)}.{(section.Panels.IndexOf(i) + 1).ToString(culture)} — {(panel.Title.Length > 0 ? panel.Title : panel.Plan.Objective)}").FontSize(10).SemiBold().FontColor(Ocean);
                                    if (isChart)
                                    {
                                        figure.Item().Border(1).BorderColor(Border).Padding(6).Svg(SvgChartBuilder.Build(type, panel.Result.Series, 520, type == "pie" ? 220 : 240));
                                    }
                                    else
                                    {
                                        figure.Item().Element(c => Table(c, panel, culture));
                                    }

                                });
                            }
                        });
                    }

                    if (content.Recommendations.Count > 0)
                    {
                        column.Item().Column(recs =>
                        {
                            recs.Item().PaddingTop(6).PaddingBottom(2).Text(rtl ? "التوصيات" : "Recommendations").FontSize(15).SemiBold().FontColor(Ocean);
                            var n = 0;
                            foreach (var rec in content.Recommendations)
                            {
                                n++;
                                recs.Item().PaddingTop(4).Row(row =>
                                {
                                    row.ConstantItem(22).Text(n.ToString(culture) + ".").SemiBold().FontColor(Blue);
                                    row.RelativeItem().Column(c =>
                                    {
                                        c.Item().Text(rec.Title).SemiBold();
                                        Paragraphs(c, rec.Detail);
                                    });
                                });
                            }
                        });
                    }

                });
                page.Footer().Element(footer => Footer(footer, generatedOn, rtl));
            });
        });
        return document.GeneratePdf();
    }

    private static void Header(IContainer container, ReportRenderRequest request, string title, bool rtl)
    {
        container.PaddingBottom(6).BorderBottom(1).BorderColor(Border).Row(row =>
        {
            var logo = request.Branding.Logo;
            if (logo is { Length: > 0 })
            {
                var cell = row.ConstantItem(150).Height(34).AlignLeft().AlignMiddle();
                if (string.Equals(request.Branding.LogoContentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
                {
                    cell.Svg(System.Text.Encoding.UTF8.GetString(logo));
                }
                else
                {
                    cell.Image(logo).FitHeight();
                }
            }
            else
            {
                row.ConstantItem(150).AlignMiddle().Text(request.OrganizationName).FontSize(12).SemiBold().FontColor(Ocean);
            }

            row.RelativeItem().AlignMiddle().Column(c =>
            {
                c.Item().AlignRight().Text(title.Length > 0 ? title : "Report").FontSize(9).FontColor(Muted);
                c.Item().AlignRight().Text(rtl ? "سرّي — للاستخدام الداخلي" : "Confidential — internal use").FontSize(8).FontColor(Muted);
            });
        });
    }

    private static void Footer(IContainer container, string generatedOn, bool rtl)
    {
        container.PaddingTop(6).BorderTop(1).BorderColor(Border).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                t.Span(rtl ? "أُنشئ بواسطة Wabi Insight · " : "Generated by Wabi Insight · ");
                t.Span(generatedOn);
            });
            row.ConstantItem(90).AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                t.Span(rtl ? "صفحة " : "Page ");
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });
    }

    private static void Paragraphs(ColumnDescriptor column, string text)
    {
        foreach (var paragraph in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            column.Item().PaddingBottom(3).Text(paragraph).Justify();
        }
    }

    private static void Table(IContainer container, AnalysisPanel panel, CultureInfo culture)
    {
        var columns = panel.Result.Columns;
        container.Border(1).BorderColor(Border).Table(table =>
        {
            table.ColumnsDefinition(definition =>
            {
                foreach (var column in columns)
                {
                    if (column.IsNumeric)
                    {
                        definition.RelativeColumn(0.8f);
                    }
                    else
                    {
                        definition.RelativeColumn(1.2f);
                    }
                }
            });
            table.Header(header =>
            {
                foreach (var column in columns)
                {
                    var cell = header.Cell().Background("#F8FAFC").BorderBottom(1).BorderColor(Border).Padding(5);
                    (column.IsNumeric ? cell.AlignRight() : cell).Text(Label(column.Name)).FontSize(8.5f).SemiBold().FontColor(Muted);
                }
            });
            var rowIndex = 0;
            foreach (var row in panel.Result.Rows.Take(MaxTableRows))
            {
                var background = rowIndex++ % 2 == 1 ? "#FBFCFE" : "#FFFFFF";
                for (var i = 0; i < columns.Count; i++)
                {
                    var cell = table.Cell().Background(background).BorderBottom(1).BorderColor("#F1F5F9").Padding(5);
                    (columns[i].IsNumeric ? cell.AlignRight() : cell).Text(FormatValue(row[i], columns[i].IsNumeric, culture)).FontSize(9);
                }
            }
        });
    }

    internal static string FormatValue(object? value, bool isNumeric, CultureInfo culture)
    {
        if (value is null)
        {
            return "—";
        }

        if (isNumeric && ResultCalculator.ToDouble(value) is { } number && value is not string)
        {
            return Math.Abs(number - Math.Round(number)) < 1e-9 ? number.ToString("#,0", culture) : number.ToString("#,0.##", culture);
        }

        var text = ResultCalculator.Format(value);
        return text.Length > 60 ? text[..59] + "…" : text;
    }

    /// <summary>The model sometimes numbers its own titles ("3. Monthly traffic"); the renderer numbers sections itself.</summary>
    internal static string StripNumbering(string title) => System.Text.RegularExpressions.Regex.Replace(title, @"^\s*(\d+|[IVXivx]+)[.)]\s*(\d+[.)]\s*)?", string.Empty).Trim();

    internal static string Label(string name)
    {
        var words = name.Replace('_', ' ').Trim();
        return words.Length == 0 ? name : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static void EnsureFonts()
    {
        lock (FontLock)
        {
            if (_fontsRegistered)
            {
                return;
            }

            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is not null)
                {
                    FontManager.RegisterFontFromStream(stream);
                }
            }

            _fontsRegistered = true;
        }
    }
}
