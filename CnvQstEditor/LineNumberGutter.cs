using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CnvQstEditor;

/// <summary>
/// 多行 TextBox 的行号槽：读取 TextPresenter 的实际排版（含自动换行），
/// 每个逻辑行只在首个视觉行位置画一次编号；滚动/换行/缩放由 LayoutUpdated 与
/// ScrollViewer.ScrollChanged 驱动重绘。
/// </summary>
internal sealed class LineNumberGutter : Control
{
    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA));
    private static readonly IPen BorderPen = new Pen(UiTheme.GridLineBrush, 1);

    private TextBox? _box;
    private TextPresenter? _presenter;
    private ScrollViewer? _scroll;

    public LineNumberGutter()
    {
        ClipToBounds = true;
        MinWidth = 44;
    }

    public void Attach(TextBox box)
    {
        _box = box;
        box.LayoutUpdated += (_, _) =>
        {
            EnsureParts();
            InvalidateVisual();
        };
        box.AttachedToVisualTree += (_, _) =>
        {
            EnsureParts();
            InvalidateVisual();
        };
        if (box.IsAttachedToVisualTree()) EnsureParts();
    }

    private void EnsureParts()
    {
        if (_box == null) return;
        if (_presenter == null || !_presenter.IsAttachedToVisualTree())
            _presenter = _box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();

        var scroll = _box.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (!ReferenceEquals(scroll, _scroll))
        {
            if (_scroll != null) _scroll.ScrollChanged -= OnScrollChanged;
            _scroll = scroll;
            if (_scroll != null) _scroll.ScrollChanged += OnScrollChanged;
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Background, new Rect(Bounds.Size));
        context.DrawLine(BorderPen,
            new Point(Bounds.Width - 0.5, 0), new Point(Bounds.Width - 0.5, Bounds.Height));

        EnsureParts();
        if (_box == null || _presenter?.TextLayout is not { } layout) return;
        var origin = _presenter.TranslatePoint(new Point(0, 0), this);
        if (origin == null) return;

        var typeface = new Typeface(_presenter.FontFamily, _presenter.FontStyle,
            _presenter.FontWeight, _presenter.FontStretch);
        // 按逻辑行首偏移直接命中排版位置：自动换行产生的视觉行天然被跳过，
        // 也避免依赖 TextLine 的文本索引语义。
        foreach (var (offset, no) in LogicalLineStarts(_box.Text ?? ""))
        {
            var caret = layout.HitTestTextPosition(offset);
            double top = origin.Value.Y + caret.Y;
            if (top + caret.Height <= 0 || top >= Bounds.Height) continue;

            var ft = new FormattedText(no.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, _presenter.FontSize, UiTheme.MutedBrush);
            double x = Math.Max(2.0, Bounds.Width - 8 - ft.Width);
            double ty = top + (caret.Height - ft.Height) / 2;
            context.DrawText(ft, new Point(x, ty));
        }
    }

    /// <summary>逻辑行首字符偏移 → 行号（1 起）；自动换行产生的视觉行不在其中。</summary>
    private static Dictionary<int, int> LogicalLineStarts(string text)
    {
        var map = new Dictionary<int, int> { [0] = 1 };
        int no = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            no++;
            map[i + 1] = no;
        }
        return map;
    }
}
