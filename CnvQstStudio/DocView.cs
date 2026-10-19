using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CnvQst;

namespace CnvQstStudio;

/// <summary>
/// 单个文档视图：100% 宽度的树形表格（结构与数据结合展示）+ 页内状态行。
/// 结构表（CNV/QST）与本地化字符串表通过 <see cref="ToggleView"/> 切换。
/// </summary>
internal sealed class DocView : UserControl
{
    public DocSession Doc { get; }
    public event Action? DirtyChanged;

    private readonly CnvTableEditor _cnvTable = new() { Visibility = Visibility.Collapsed };
    private readonly QstTableEditor _qstTable = new() { Visibility = Visibility.Collapsed };
    private readonly LocalizationGrid _locGrid = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new()
    {
        Margin = new Thickness(12, 2, 12, 4),
        Foreground = WpfTheme.MutedBrush,
    };
    private readonly TextBlock _emptyHint = new()
    {
        Text = L.EmptyHint,
        Foreground = WpfTheme.MutedBrush,
        Margin = new Thickness(28),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Top,
    };
    private readonly Grid _host = new();

    public DocView(DocSession doc)
    {
        Doc = doc;

        foreach (var c in new UIElement[] { _cnvTable, _qstTable, _locGrid, _emptyHint }) _host.Children.Add(c);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_host, 0);
        Grid.SetRow(_status, 1);
        root.Children.Add(_host);
        root.Children.Add(_status);
        Content = root;

        _cnvTable.LocRequested += ToggleView;
        _qstTable.LocRequested += ToggleView;

        ShowCurrent();
        UpdateStatus();
    }

    private void ShowCurrent()
    {
        if (Doc.Cnv != null)
        {
            ShowOnly(_cnvTable);
            _cnvTable.Bind(Doc, OnEdited);
        }
        else if (Doc.Qst != null)
        {
            ShowOnly(_qstTable);
            _qstTable.Bind(Doc, OnEdited);
        }
        else
        {
            ShowOnly(_emptyHint);
        }
    }

    private void ShowOnly(UIElement target)
    {
        foreach (var c in new UIElement[] { _cnvTable, _qstTable, _locGrid, _emptyHint })
            c.Visibility = c == target ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>在 结构表 / 本地化字符串表 之间切换（右键菜单与 Ctrl+L 共用）。</summary>
    public void ToggleView()
    {
        if (_locGrid.Visibility == Visibility.Visible)
        {
            ShowCurrent();          // 从本地化表回到结构表
        }
        else
        {
            ShowOnly(_locGrid);
            _locGrid.Bind(Doc, OnEdited);
        }
        UpdateStatus();
    }

    private void OnEdited()
    {
        Doc.Dirty = true;
        DirtyChanged?.Invoke();
        UpdateStatus();
    }

    public void RefreshAll()
    {
        if (_cnvTable.Visibility == Visibility.Visible) _cnvTable.Bind(Doc, OnEdited);
        else if (_qstTable.Visibility == Visibility.Visible) _qstTable.Bind(Doc, OnEdited);
        else if (_locGrid.Visibility == Visibility.Visible) _locGrid.Bind(Doc, OnEdited);
        UpdateStatus();
    }

    public void AfterSaved()
    {
        Doc.Dirty = false;
        DirtyChanged?.Invoke();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        string prefix = Doc.Dirty ? L.StatusSaving + "   " : "";
        _status.Text = prefix + Doc.Summary + (Doc.ReadOnly ? "   ·   " + L.StatusReadonly(Doc.LoadError ?? L.T("完整性检查未过")) : "");
        _status.Foreground = Doc.ReadOnly ? WpfTheme.WarnBrush : Doc.Dirty ? WpfTheme.AccentBrush : WpfTheme.MutedBrush;
    }
}
