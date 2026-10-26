using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace CnvQstEditor;

/// <summary>
/// AvaloniaEdit 公共工厂：全项目所有文本编辑器（底部编辑器 + 四类只读视图）统一在此配置——
/// 等宽字体、自动换行（禁横向滚动）、滚动到底即止（不滚出文档末尾）、奇偶行交叉着色
/// （含行号与节点名 margin）、右键复制/全选（Win32 同步剪贴板，规避 Avalonia 异步剪贴板
/// 在菜单回调中失效的问题）。底色统一 UiTheme.EditorBrush（与窗口背景接近但不同）。
/// </summary>
internal static class UiEditors
{
    private static readonly FontFamily CodeFont = new("Consolas");

    /// <summary>底部编辑器（可编辑；行号 + 交叉着色；自动换行）。</summary>
    public static TextEditor Editable() => Build(isReadOnly: false, margin: null);

    /// <summary>只读视图编辑器。margin=null 时为纯行号；本地化合并视图传 RowMargin 实例。</summary>
    public static TextEditor ReadOnly(RowMargin? margin = null) => Build(isReadOnly: true, margin: margin);

    private static TextEditor Build(bool isReadOnly, RowMargin? margin)
    {
        var editor = new TextEditor
        {
            IsReadOnly = isReadOnly,
            FontFamily = CodeFont,
            FontSize = 12,
            WordWrap = true,                                             // 所有视图仅竖向滚动
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = UiTheme.EditorBrush,
            Padding = new Thickness(4, 2, 4, 2),
        };
        editor.Options.AllowScrollBelowDocument = false;              // 滚动到最后一行即止
        editor.TextArea.Foreground = UiTheme.TextBrush;
        editor.TextArea.Background = UiTheme.EditorBrush;
        // 奇偶交叉着色（TextArea 部分）；margin 部分由 RowMargin 自绘
        editor.TextArea.TextView.BackgroundRenderers.Add(new ZebraBackgroundRenderer());
        // 行号/节点名列一律用 RowMargin（自带行背景着色与换行对齐），不用内置行号
        margin ??= new RowMargin();
        editor.TextArea.LeftMargins.Insert(0, margin);
        // 主题切换 → 刷实例已自动换色，但 TextView 层需显式失效才重绘（margin/render 均在此）
        UiTheme.ThemeChanged += () => editor.TextArea.TextView.InvalidateVisual();
        AttachCopyMenu(editor);
        return editor;
    }

    /// <summary>
    /// 右键菜单：复制 / 全选。复制走 Win32 同步剪贴板（ClipboardHelper），与表格复制同一套实现；
    /// Ctrl+C 仍走 AvaloniaEdit 内建 Copy 命令。
    /// </summary>
    private static void AttachCopyMenu(TextEditor editor)
    {
        var copy = new MenuItem { Header = L.Copy };
        copy.Click += (_, _) => { if (editor.SelectedText.Length > 0) ClipboardHelper.SetText(editor.SelectedText); };
        var all = new MenuItem { Header = L.T("Select all") };
        all.Click += (_, _) => editor.SelectAll();
        editor.TextArea.ContextMenu = new ContextMenu { Items = { copy, all } };
    }
}

/// <summary>
/// 奇偶行交叉着色（TextArea 背景）：偶数文档行铺 zebra 色、奇数行铺编辑器底色
/// （与 RowMargin 的行号列同规则）。自动换行时按视觉行逐条铺色（同一文档行的续行同色，保证整行连片）。
/// </summary>
internal sealed class ZebraBackgroundRenderer : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext ctx)
    {
        if (!textView.VisualLinesValid) return;
        // WordWrap 模式下内容不会超过视口宽度，用控件宽度铺满即可
        double width = textView.Bounds.Width;
        foreach (var line in textView.VisualLines)
        {
            if (line.TextLines.Count == 0) continue;
            if (line.FirstDocumentLine.LineNumber % 2 == 0)
            {
                double y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop)
                           - textView.VerticalOffset;
                ctx.FillRectangle(UiTheme.ZebraBrush, new Rect(0, y, width, line.Height));
            }
        }
    }
}

/// <summary>一行文档在 margin 中的静态标注（全部为装载时确定的固定内容）。</summary>
internal sealed record RowLabel(int Number, string Label = "", IBrush? LabelBrush = null, string Desc = "", string Advice = "");

/// <summary>
/// 左侧标注列：行号（+ 交叉着色背景），可选 节点名（按可视化编辑器同款类型着色）/ 节点描述 / 槽位建议。
/// 纯行号模式（未 SetRows 或 rows 为 null）也正常显示行号（JSON / DUMP / 底部编辑器），
/// 宽度随文档行数位数自动增长——与 AvaloniaEdit 内置 LineNumberMargin 相同地订阅
/// VisualLinesChanged / LineCountChanged 触发重新测量与重绘。
/// 自动换行时：背景每个视觉行都铺（连片），行号与各列文本只画文档行的第一个视觉行。
/// </summary>
internal sealed class RowMargin : AbstractMargin
{
    private const double EmSize = 12;
    private const double PadLeft = 6;
    private const double Gap = 10;

    private readonly Typeface _typeface = new(new FontFamily("Consolas"));
    private List<RowLabel> _rows = [];
    private int _digits = 1;
    private double _labelW, _descW, _adviceW;
    /// <summary>Consolas 12 的基线高：行号与正文行按基线对齐（TextTop 受 TextView 默认字体度量影响会偏上 1-2px）。</summary>
    private readonly double _baseline;

    public RowMargin()
    {
        _baseline = MakeText("0", UiTheme.RowNumberBrush).Baseline;
        UiTheme.ThemeChanged += () => { InvalidateMeasure(); InvalidateVisual(); TextView?.InvalidateVisual(); };
    }

    /// <summary>装载行标注；rows 为 null 时清空为纯行号（行号仍显示）。</summary>
    public void SetRows(IReadOnlyList<RowLabel>? rows)
    {
        _rows = rows?.ToList() ?? [];
        _labelW = _descW = _adviceW = 0;
        foreach (var r in _rows)
        {
            if (r.Label.Length > 0) _labelW = Math.Max(_labelW, MakeText(r.Label, r.LabelBrush ?? UiTheme.TextBrush).Width);
            if (r.Desc.Length > 0) _descW = Math.Max(_descW, MakeText(r.Desc, UiTheme.RowDescBrush).Width);
            if (r.Advice.Length > 0) _adviceW = Math.Max(_adviceW, MakeText(r.Advice, UiTheme.RowAdviceBrush).Width);
        }
        UpdateDigits();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        base.OnTextViewChanged(oldTextView, newTextView);   // 基类已订阅 VisualLinesChanged → InvalidateVisual
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnDocumentChanged(TextDocument oldDocument, TextDocument newDocument)
    {
        base.OnDocumentChanged(oldDocument, newDocument);
        if (newDocument != null)
            TextDocumentWeakEventManager.LineCountChanged.AddHandler(newDocument, OnLineCountChanged);
        if (oldDocument != null)
            TextDocumentWeakEventManager.LineCountChanged.RemoveHandler(oldDocument, OnLineCountChanged);
        UpdateDigits();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnLineCountChanged(object? sender, EventArgs e) => UpdateDigits();

    /// <summary>行号位数 = max(文档行数, 标注行数) 的十进制位数（位数变化才重新测量）。</summary>
    private void UpdateDigits()
    {
        int count = Math.Max(Document?.LineCount ?? 1, _rows.Count);
        int len = Math.Max(1, count.ToString(CultureInfo.CurrentCulture).Length);
        if (len != _digits)
        {
            _digits = len;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double widest = NumberWidth() + PadLeft * 2;
        if (_labelW > 0) widest += Gap + _labelW;
        if (_descW > 0) widest += Gap + _descW;
        if (_adviceW > 0) widest += Gap + _adviceW;
        return new Size(widest, 0);
    }

    public override void Render(DrawingContext ctx)
    {
        var tv = TextView;
        if (tv is not { VisualLinesValid: true }) return;
        double totalWidth = Bounds.Width;
        foreach (var visual in tv.VisualLines)
        {
            if (visual.TextLines.Count == 0) continue;
            int number = visual.FirstDocumentLine.LineNumber;
            bool even = number % 2 == 0;
            // 背景：铺整个视觉行（VisualTop 起）；文字：与正文同一基线（VisualYPosition.Baseline）
            double top = visual.VisualTop - tv.VerticalOffset;
            double baseY = visual.GetTextLineVisualYPosition(visual.TextLines[0], VisualYPosition.Baseline)
                           - tv.VerticalOffset - _baseline;
            // 行背景：整个 margin 宽（含换行续行），与 TextArea 的 ZebraBackgroundRenderer 同奇偶规则
            if (even)
                ctx.FillRectangle(UiTheme.ZebraBrush, new Rect(0, top, totalWidth, visual.Height));

            int index = number - 1;
            if ((uint)index >= (uint)_rows.Count)
            {
                DrawNumber(ctx, number, baseY);
                continue;
            }
            var row = _rows[index];
            double x = DrawNumber(ctx, number, baseY);
            if (_labelW > 0 && row.Label.Length > 0)
            {
                ctx.DrawText(MakeText(row.Label, row.LabelBrush ?? UiTheme.TextBrush), new Point(x + Gap, baseY));
                x += Gap + _labelW;
            }
            if (_descW > 0 && row.Desc.Length > 0)
            {
                ctx.DrawText(MakeText(row.Desc, UiTheme.RowDescBrush), new Point(x + Gap, baseY));
                x += Gap + _descW;
            }
            if (_adviceW > 0 && row.Advice.Length > 0)
                ctx.DrawText(MakeText(row.Advice, UiTheme.RowAdviceBrush), new Point(x + Gap, baseY));
        }
    }

    /// <summary>画行号（始终显示，含纯行号模式），返回行号列右缘 x。</summary>
    private double DrawNumber(DrawingContext ctx, int number, double y)
    {
        var text = MakeText(number.ToString(CultureInfo.CurrentCulture).PadLeft(_digits), UiTheme.RowNumberBrush);
        ctx.DrawText(text, new Point(PadLeft, y));
        return PadLeft + NumberWidth();
    }

    private double NumberWidth() => _digits * 7.2 + 2;   // Consolas 12pt 数字近似宽（避免每次 Measure）

    private FormattedText MakeText(string text, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, EmSize, brush);
}
