using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace WabiInsight.Infrastructure.Reports;

/// <summary>
/// Draws a chart into a QuestPDF document (PDF studies, and the PNG charts of Word and Excel files): the shapes as SVG and
/// every label with the document's own text engine, which shapes Arabic and lays it out right to left — the SVG renderer
/// joins Arabic letters but places them left to right, so Arabic labels drawn inside the SVG read backwards. The chart is
/// drawn at its own size (one chart unit is one point), without scaling.
/// </summary>
public static class ChartComposer
{
    private const string LatinFont = "Inter";
    private const string ArabicFont = "IBM Plex Sans Arabic";

    /// <summary>How far the baseline sits below the top of a label's line box, as a share of the font size (matches the SVG text it replaces).</summary>
    internal const float BaselineRatio = 0.8f;

    public static void Draw(IContainer container, ChartSvg chart)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(chart);
        QuestPdfReportRenderer.EnsureFonts();
        container.Width((float)chart.Width).Height((float)chart.Height).ContentFromLeftToRight().Layers(layers =>
        {
            layers.PrimaryLayer().Svg(chart.ShapesSvg);
            foreach (var label in chart.Texts.Where(t => t.Text.Length > 0))
            {
                layers.Layer().Element(c => Label(c, label));
            }
        });
    }

    private static void Label(IContainer container, ChartText label)
    {
        var size = (float)label.FontSize;
        var width = (float)(TextMetrics.Width(label.Text, label.FontSize) * 1.25 + label.FontSize * 2);
        var height = size * 1.6f;
        var left = label.Anchor switch
        {
            "middle" => (float)label.X - width / 2,
            "end" => (float)label.X - width,
            _ => (float)label.X,
        };
        var top = (float)label.Y - size * BaselineRatio;

        if (label.Rotation != 0)
        {
            // The box turns about its own centre c; the label must turn about its anchor a (as SVG's rotate(angle a)):
            // moving the box by R(c − a) − (c − a) gives the same result.
            var angle = label.Rotation * Math.PI / 180;
            var dx = left + width / 2 - label.X;
            var dy = top + height / 2 - label.Y;
            left += (float)(dx * Math.Cos(angle) - dy * Math.Sin(angle) - dx);
            top += (float)(dx * Math.Sin(angle) + dy * Math.Cos(angle) - dy);
        }

        // Rotate turns its content about the centre of the content's own size, so the fixed-size box goes inside it.
        var box = container.OffsetX(left).OffsetY(top).Unconstrained();
        if (label.Rotation != 0)
        {
            box = box.Rotate((float)label.Rotation);
        }

        box = box.Width(width).Height(height);

        var rightToLeft = IsRightToLeft(label.Text);
        if (rightToLeft)
        {
            box = box.ContentFromRightToLeft();
        }

        box.Text(text =>
        {
            switch (label.Anchor)
            {
                case "middle":
                    text.AlignCenter();
                    break;
                case "end":
                    text.AlignRight();
                    break;
                default:
                    text.AlignLeft();
                    break;
            }

            var span = text.Span(label.Text).FontFamily(LatinFont, ArabicFont).FontSize(size).FontColor(label.Color).LineHeight(1);
            if (rightToLeft)
            {
                span.DirectionFromRightToLeft();
            }
        });
    }

    /// <summary>True when the first letter of <paramref name="text"/> belongs to a right-to-left script (Arabic, Hebrew).</summary>
    internal static bool IsRightToLeft(string text)
    {
        foreach (var c in text)
        {
            if (c is (>= '֐' and <= 'ࣿ') or (>= 'יִ' and <= '﷿') or (>= 'ﹰ' and <= '﻿'))
            {
                return true;
            }

            if (char.IsLetter(c))
            {
                return false;
            }
        }

        return false;
    }
}
