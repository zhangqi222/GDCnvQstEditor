using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using CnvQst;
using MaterialDesignThemes.Wpf;

namespace CnvQstStudio;

/// <summary>CNV 树型列表：节点(树) | 属性 | 本地化文本 | 条件 | 动作。</summary>
internal sealed class CnvTableEditor : UserControl
{
    private DocSession _doc = null!;
    private CnvFile _cnv = null!;
    private Changed _changed = null!;
    private Action? _locRequested;
    private bool _comfortable;
    private List<CnvRow> _rows = new();

    private readonly WorkingTreeListView _list = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
    };

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }
    public event Action? SelectedChanged;

    public int? SelectedTextSlot => SelectedStep?.Index;

    public CnvTableEditor()
    {
        var header = TreeGridShared.MkHeader(new[] { (L.T("节点", "Node"), 320d), (L.T("属性", "Attrs"), 150d), (L.T("本地化文本", "Localized Text"), 0d), (L.T("条件", "Conditions"), 280d), (L.T("动作", "Actions"), 280d) });

        _list.ItemTemplate = BuildTemplate(compact: true);
        _list.ItemContainerStyle = TreeGridShared.MkContainerStyle();
        _list.SelectionChanged += (_, _) => { UpdateHighlight(); SelectedChanged?.Invoke(); };
        _list.MouseDoubleClick += OnDoubleClick;
        _list.PreviewMouseRightButtonDown += ListRightDown;
        _list.ContextMenu = BuildContextMenu();

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_list);
        Content = root;
    }

    private DataTemplate BuildTemplate(bool compact)
    {
        int maxLines = compact ? 3 : 100000;
        const string xaml = """
<HierarchicalDataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                           xmlns:md="clr-namespace:MaterialDesignThemes.Wpf;assembly=MaterialDesignThemes.Wpf"
                           ItemsSource="{Binding Children}">
  <Grid Background="Transparent">
    <Grid.Style>
      <Style TargetType="Grid">
        <Style.Triggers>
          <DataTrigger Binding="{Binding Highlight}" Value="True">
            <Setter Property="Background" Value="#263F51B5"/>
          </DataTrigger>
        </Style.Triggers>
      </Style>
    </Grid.Style>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="320"/>
      <ColumnDefinition Width="150"/>
      <ColumnDefinition Width="*"/>
      <ColumnDefinition Width="280"/>
      <ColumnDefinition Width="280"/>
    </Grid.ColumnDefinitions>
    <Border Grid.Column="1" Width="1" HorizontalAlignment="Left" Background="#E0E0E0" Margin="-2,0,0,0"/>
    <Border Grid.Column="2" Width="1" HorizontalAlignment="Left" Background="#E0E0E0" Margin="-2,0,0,0"/>
    <Border Grid.Column="3" Width="1" HorizontalAlignment="Left" Background="#E0E0E0" Margin="-2,0,0,0"/>
    <Border Grid.Column="4" Width="1" HorizontalAlignment="Left" Background="#E0E0E0" Margin="-2,0,0,0"/>
    <Border Grid.ColumnSpan="5" VerticalAlignment="Bottom" Height="1" Background="#E0E0E0"/>
    <TextBlock Tag="0" Grid.Column="0" Text="{Binding Node}" TextWrapping="Wrap" VerticalAlignment="Center" Margin="2,3,8,3" Foreground="{Binding NodeBrush}"/>
    <TextBlock Tag="1" Grid.Column="1" Text="{Binding Attrs}" TextWrapping="Wrap" VerticalAlignment="Center" Margin="2,3,8,3" Foreground="#6B7280"/>
    <TextBlock Grid.Column="2" Text="{Binding Text}" TextWrapping="Wrap" VerticalAlignment="Top" Margin="2,3,8,3" MaxHeight="__MAXH__">
      <TextBlock.Style>
        <Style TargetType="TextBlock">
          <Style.Triggers>
            <DataTrigger Binding="{Binding IsSelected, RelativeSource={RelativeSource AncestorType={x:Type md:TreeListViewItem}}}" Value="True">
              <Setter Property="MaxHeight" Value="10000"/>
            </DataTrigger>
          </Style.Triggers>
        </Style>
      </TextBlock.Style>
    </TextBlock>
    <ItemsControl Tag="3" Grid.Column="3" ItemsSource="{Binding Cond}" IsTabStop="False" Margin="2,3,8,3">
      <ItemsControl.ItemTemplate>
        <DataTemplate><TextBlock Text="{Binding Text}" Foreground="{Binding Brush}" TextWrapping="Wrap"/></DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
    <ItemsControl Tag="4" Grid.Column="4" ItemsSource="{Binding Act}" IsTabStop="False" Margin="2,3,8,3">
      <ItemsControl.ItemTemplate>
        <DataTemplate><TextBlock Text="{Binding Text}" Foreground="{Binding Brush}" TextWrapping="Wrap"/></DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </Grid>
</HierarchicalDataTemplate>
""";
        return (HierarchicalDataTemplate)XamlReader.Parse(xaml.Replace("__MAXH__", maxLines.ToString()));
    }

    private void ToggleMode()
    {
        _comfortable = !_comfortable;
        _list.ItemTemplate = BuildTemplate(_comfortable);
    }

    private ContextMenu BuildContextMenu()
    {
        var cm = new ContextMenu();
        cm.Items.Add(TreeGridShared.Mi(L.T("＋添加子节点", "+ Add Child"), AddChild));
        cm.Items.Add(TreeGridShared.Mi(L.T("－删除子树", "- Delete"), DeleteStep));
        cm.Items.Add(new Separator());
        cm.Items.Add(TreeGridShared.Mi("↑ " + L.T("上移", "Up"), () => MoveStep(-1)));
        cm.Items.Add(TreeGridShared.Mi("↓ " + L.T("下移", "Down"), () => MoveStep(1)));
        cm.Items.Add(new Separator());
        cm.Items.Add(TreeGridShared.Mi(L.T("节点属性", "Node Properties"), EditNodeProps));
        cm.Items.Add(TreeGridShared.Mi(L.LocStringTable, () => _locRequested?.Invoke()));
        cm.Items.Add(new Separator());
        cm.Items.Add(TreeGridShared.Mi(_comfortable ? L.T("平铺模式", "Flat Mode") : L.T("紧凑模式", "Compact Mode"), ToggleMode));
        return cm;
    }

    private void ListRightDown(object sender, MouseButtonEventArgs e)
    {
        var item = TreeGridShared.FindAncestor<TreeListViewItem>(e.OriginalSource as DependencyObject);
        if (item != null) item.IsSelected = true;
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        string? tag = null;
        var d = e.OriginalSource as DependencyObject;
        while (d != null)
        {
            if (d is FrameworkElement fe && fe.Tag is string t) { tag = t; break; }
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        if (tag == null) return;
        var s = SelectedStep;
        if (s == null) return;
        if (tag == "3")
        {
            if (ScriptListDialog.Show(_doc, s.Conditions, true, Window.GetWindow(this))) RefreshSelected();
        }
        else if (tag == "4")
        {
            if (ScriptListDialog.Show(_doc, s.Actions, false, Window.GetWindow(this))) RefreshSelected();
        }
        else if (tag is "0" or "1")
        {
            EditNodeProps();
        }
    }

    private void EditNodeProps()
    {
        var s = SelectedStep;
        if (s == null) return;
        if (CnvNodeDialog.Show(_doc, s, Window.GetWindow(this)))
        {
            RefreshSelected();
            _changed();
        }
    }

    private void RefreshSelected()
    {
        if (_list.SelectedItem is CnvRow r) r.Reload();
    }

    public void NotifyEdited()
    {
        _doc.Dirty = true;
        _changed();
    }

    public void Bind(DocSession doc, Changed changed)
    {
        _doc = doc;
        _cnv = doc.Cnv!;
        _changed = changed;

        var byStep = new Dictionary<CnvStep, CnvRow>();
        _rows = _cnv.Steps.Select(s => new CnvRow(this, doc, s)).ToList();
        foreach (var row in _rows) byStep[row.Step] = row;
        foreach (var row in _rows)
        {
            foreach (var childStep in row.Step.Children)
                if (byStep.TryGetValue(childStep, out var child)) child.Attach(row);
        }

        _list.ItemsSource = _rows.Where(r => r.Parent == null).ToList();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        SelectedChanged?.Invoke();
    }

    private CnvStep? SelectedStep => _list.SelectedItem is CnvRow r ? r.Step : null;

    private void UpdateHighlight()
    {
        foreach (var r in _rows) r.Highlight = false;
        if (_list.SelectedItem is CnvRow sel)
            WalkHi(sel);
        return;

        void WalkHi(CnvRow r)
        {
            r.Highlight = true;
            foreach (var c in r.Children) WalkHi(c);
        }
    }

    public void SelectStep(CnvStep s)
    {
        var row = _rows.FirstOrDefault(r => ReferenceEquals(r.Step, s));
        if (row == null) return;
        var path = new List<CnvRow>();
        for (var r = row; r != null; r = r.Parent) path.Insert(0, r);
        var gen = _list.ItemContainerGenerator;
        for (int i = 0; i < path.Count; i++)
        {
            if (gen.ContainerFromItem(path[i]) is not TreeListViewItem container) return;
            if (i == path.Count - 1)
            {
                _list.SelectedItem = path[i];
                _list.ScrollIntoView(path[i]);
            }
            else if (!container.IsExpanded)
            {
                container.IsExpanded = true;
            }
        }
    }

    private void AddChild()
    {
        var anchor = SelectedStep;
        if (anchor == null) return;
        var dlg = new CnvTypePicker { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        _cnv.InsertChild(anchor, dlg.Type);
        _changed();
        Bind(_doc, _changed);
    }

    private void DeleteStep()
    {
        var s = SelectedStep;
        if (s == null) return;
        if (s == _cnv.Root)
        {
            Gui.Warn(Window.GetWindow(this), L.T("不能删除根节点。", "The root node cannot be deleted."), L.T("删除", "Delete"));
            return;
        }
        if (!_cnv.RemoveSubtree(s)) return;
        _changed();
        Bind(_doc, _changed);
    }

    private void MoveStep(int delta)
    {
        var s = SelectedStep;
        if (s == null) return;
        _cnv.MoveSibling(s, delta);
        _changed();
        Bind(_doc, _changed);
    }
}
