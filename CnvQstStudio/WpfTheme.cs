using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;

namespace CnvQstStudio;

/// <summary>
/// MaterialDesignInXamlToolkit 主题引导：合并 BundledTheme（浅色/Indigo）与 MaterialDesign3 默认字典，
/// 并暴露少量语义色刷。表格外观（网格线/平直行/子树高亮）沿用本项目自定义模板。
/// </summary>
internal static class WpfTheme
{
    /// <summary>应用启动（含 --smoke）时调用一次。</summary>
    public static void Init(Application app)
    {
        app.Resources.MergedDictionaries.Add(new BundledTheme
        {
            BaseTheme = BaseTheme.Light,
            PrimaryColor = PrimaryColor.Indigo,
            SecondaryColor = SecondaryColor.Indigo,
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml", UriKind.Absolute),
        });
    }

    // ---- 主按钮：MaterialDesign Raised 样式 ----
    public static void MakePrimary(Button button)
    {
        button.SetResourceReference(FrameworkElement.StyleProperty, "MaterialDesignRaisedButton");
    }

    // ---- 语义色刷 ----
    public static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x24, 0x30));
    public static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    public static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xED));
    public static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09));
    public static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x0E, 0x7A, 0x4F));
    public static readonly Brush DangerBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18));
    public static readonly Brush UnknownBrush = Brushes.Gray;                       // 未命名的条件/动作
    public static readonly Brush InferredBrush = new SolidColorBrush(Color.FromRgb(230, 126, 34)); // 推断语义

    public static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");

    private static readonly Brush GridLineBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
    private static readonly Brush SelectedBrush = new SolidColorBrush(Color.FromArgb(60, 0x3F, 0x51, 0xB5)); // 选中/子树高亮（Indigo 浅底）

    /// <summary>DataGrid 统一外观：横竖网格线 + 列头右/下分隔线 + 平直选中样式（无缩进/变色错位）。
    /// 行背景支持 Highlight（子树随选中行同色）；前景恒为正文色。</summary>
    public static void ApplyGrid(DataGrid grid)
    {
        grid.GridLinesVisibility = DataGridGridLinesVisibility.All;
        grid.VerticalGridLinesBrush = GridLineBrush;
        grid.HorizontalGridLinesBrush = GridLineBrush;
        var hs = new Style(typeof(DataGridColumnHeader));
        hs.Setters.Add(new Setter(Control.BorderBrushProperty, GridLineBrush));
        hs.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        grid.ColumnHeaderStyle = hs;

        // 单元格：平直模板，前景恒黑，选中只换浅蓝背景
        var cellTpl = new ControlTemplate(typeof(DataGridCell));
        var cellBd = new FrameworkElementFactory(typeof(Border));
        cellBd.Name = "CellBd";
        cellBd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        cellBd.SetValue(Border.PaddingProperty, new Thickness(0));
        cellBd.SetValue(TextElement.ForegroundProperty, TextBrush);
        var cellCp = new FrameworkElementFactory(typeof(ContentPresenter));
        cellCp.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(DataGridCell.ContentProperty));
        cellBd.AppendChild(cellCp);
        cellTpl.VisualTree = cellBd;
        var cellSel = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        cellSel.Setters.Add(new Setter(Border.BackgroundProperty, SelectedBrush, "CellBd"));
        cellSel.Setters.Add(new Setter(TextElement.ForegroundProperty, TextBrush));
        cellTpl.Triggers.Add(cellSel);
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(DataGridCell.TemplateProperty, cellTpl));
        cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(DataGridCell.BackgroundProperty, Brushes.Transparent));
        cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        grid.CellStyle = cellStyle;

        // 行：最小模板（Border + CellsPresenter）；选中 / 子树高亮 → 浅蓝
        var rowTpl = new ControlTemplate(typeof(DataGridRow));
        var rowBd = new FrameworkElementFactory(typeof(Border));
        rowBd.Name = "DGR_Border";
        rowBd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(DataGridRow.BackgroundProperty));
        var cells = new FrameworkElementFactory(typeof(DataGridCellsPresenter));
        cells.SetValue(ItemsControl.ItemsPanelProperty, new TemplateBindingExtension(DataGridRow.ItemsPanelProperty));
        rowBd.AppendChild(cells);
        rowTpl.VisualTree = rowBd;
        var rowSel = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        rowSel.Setters.Add(new Setter(Border.BackgroundProperty, SelectedBrush, "DGR_Border"));
        rowTpl.Triggers.Add(rowSel);
        var rowHi = new DataTrigger { Binding = new Binding(nameof(HighlightableRow.Highlight)), Value = true };
        rowHi.Setters.Add(new Setter(Border.BackgroundProperty, SelectedBrush, "DGR_Border"));
        rowTpl.Triggers.Add(rowHi);
        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(DataGridRow.TemplateProperty, rowTpl));
        rowStyle.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Brushes.Transparent));
        grid.RowStyle = rowStyle;
    }

    /// <summary>支持子树高亮的行模型（DataTrigger 绑定 Highlight）。</summary>
    public interface HighlightableRow
    {
        bool Highlight { get; set; }
    }
}
