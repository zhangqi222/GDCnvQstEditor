using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CnvQst;

namespace CnvQstStudio;

/// <summary>单个文档视图：左结构树 + 右平铺表格编辑器（CNV/QST）/ 本地化表切换 + 页内状态行。</summary>
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
    private readonly TreeView _tree = new();

    public DocView(DocSession doc)
    {
        Doc = doc;

        foreach (var c in new UIElement[] { _cnvTable, _qstTable, _locGrid, _emptyHint }) _host.Children.Add(c);

        // 左：结构树（与官方编辑器一致的层级观感）；右：平铺表格 / 本地化表
        var treeHost = new Border { Padding = new Thickness(10, 8, 2, 8), Child = _tree };
        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(treeHost, 0);
        Grid.SetColumn(split, 2);
        var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 1);
        split.Children.Add(treeHost);
        split.Children.Add(splitter);
        split.Children.Add(_host);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(split, 0);
        Grid.SetRow(_status, 1);
        root.Children.Add(split);
        root.Children.Add(_status);
        Content = root;

        _cnvTable.LocRequested += ShowLoc;
        _qstTable.LocRequested += ShowLoc;
        _tree.SelectedItemChanged += TreeAfterSelect;

        BuildTree();
        ShowCurrent();
        UpdateStatus();
    }

    /// <summary>重建结构树（打开 / 结构改动 / 语言切换后）。</summary>
    private void BuildTree()
    {
        var rootInfo = new TreeNodeInfo();
        Doc.FillTree(rootInfo);
        _tree.Items.Clear();
        foreach (var child in rootInfo.Children) _tree.Items.Add(MakeNode(child));
        // 根级与次级默认展开（与 WinForms 版一致）
        foreach (TreeViewItem rootItem in _tree.Items)
        {
            rootItem.IsExpanded = true;
            foreach (TreeViewItem sub in rootItem.Items) sub.IsExpanded = true;
        }
    }

    private static TreeViewItem MakeNode(TreeNodeInfo n)
    {
        var item = new TreeViewItem { Header = n.Text, Tag = n };
        foreach (var c in n.Children) item.Items.Add(MakeNode(c));
        return item;
    }

    private void TreeAfterSelect(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem item) return;
        var info = (TreeNodeInfo)item.Tag;
        if (info.Tag is string tag && tag == DocSession.TagLoc) { ShowLoc(); return; }
        if (_locGrid.Visibility == Visibility.Visible) ShowCurrent();   // 从本地化表回到结构表
        switch (info.Tag)
        {
            case CnvStep step: _cnvTable.SelectStep(step); break;
            case QstTask t: _qstTable.SelectOwner(t); break;
            case QstObjective o: _qstTable.SelectOwner(o); break;
            case QstEvent ev: _qstTable.SelectOwner(ev); break;
        }
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

    /// <summary>切换到本地化字符串表页。</summary>
    public void ShowLoc()
    {
        ShowOnly(_locGrid);
        _locGrid.Bind(Doc, OnEdited);
    }

    private void OnEdited()
    {
        Doc.Dirty = true;
        DirtyChanged?.Invoke();
        UpdateStatus();
    }

    public void RefreshAll()
    {
        BuildTree();
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
