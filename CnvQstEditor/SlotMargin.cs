using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace CnvQstEditor;

/// <summary>
/// 只读视图的行号槽：左侧绘制“行号 + 槽位名”（如“1 说话人名（根节点）”）。
/// 行号列按最大行数右对齐；槽位名左对齐。换行关闭时一条文档行对应一条视觉行。
/// </summary>
internal sealed class SlotMargin : AbstractMargin
{
    private static readonly IBrush NumberBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
    private static readonly IBrush RoleBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A));
    private readonly Typeface _typeface = new(new FontFamily("Consolas"));
    private const double EmSize = 12;
    private const double PadLeft = 6;
    private const double Gap = 8;
    private string[] _labels = [];
    private int _digits = 1;

    public void SetLabels(IReadOnlyList<string> labels)
    {
        _labels = labels.ToArray();
        _digits = Math.Max(1, _labels.Length.ToString(CultureInfo.CurrentCulture).Length);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double widest = 0;
        for (int i = 0; i < _labels.Length; i++)
        {
            var number = MakeText((i + 1).ToString(CultureInfo.CurrentCulture).PadLeft(_digits), NumberBrush);
            var role = MakeText(_labels[i], RoleBrush);
            widest = Math.Max(widest, number.Width + Gap + role.Width);
        }
        return new Size(_labels.Length == 0 ? 0 : widest + PadLeft * 2, 0);
    }

    public override void Render(DrawingContext context)
    {
        var textView = TextView;
        if (textView is not { VisualLinesValid: true } || _labels.Length == 0) return;
        foreach (var line in textView.VisualLines)
        {
            int index = line.FirstDocumentLine.LineNumber - 1;
            if ((uint)index >= (uint)_labels.Length || line.TextLines.Count == 0) continue;
            var number = MakeText((index + 1).ToString(CultureInfo.CurrentCulture).PadLeft(_digits), NumberBrush);
            var role = MakeText(_labels[index], RoleBrush);
            double y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop)
                - textView.VerticalOffset;
            context.DrawText(number, new Point(PadLeft, y));
            context.DrawText(role, new Point(PadLeft + number.Width + Gap, y));
        }
    }

    private FormattedText MakeText(string text, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, EmSize, brush);
}
