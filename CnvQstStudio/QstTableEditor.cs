using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using CnvQst;
using MaterialDesignThemes.Wpf;

namespace CnvQstStudio;

/// <summary>QST 树型列表：节点(树) | Objective ID | 本地化文本 | 条件 | 动作。</summary>
internal sealed class QstTableEditor : UserControl
{
    private DocSession _doc = null!;
    private QstFile _qst = null!;
    private Changed _changed = null!;
    private Action? _locRequested;

    private readonly WorkingTreeListView _list = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
    };

    private List<QstRow> _rows = new();

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }
    public event Action? SelectedChanged;

    public DocSession Doc => _doc;
    public int? SelectedTextSlot => SelectedRow?.Owner.TextSlot;

    public QstTableEditor()
    {
        var header = TreeGridShared.MkHeader(new[] { (L.T("节点", "Node"), 340d), ("Objective ID", 110d), (L.T("本地化文本", "Localized Text"), 0d), (L.T("条件", "Conditions"), 270d), (L.T("动作", "Actions"), 270d) });

        _list.ItemTemplate = BuildTemplate();
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

    private DataTemplate BuildTemplate()
    {
        const string xaml = """
<HierarchicalDataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
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
      <ColumnDefinition Width="340"/>
      <ColumnDefinition Width="110"/>
      <ColumnDefinition Width="*"/>
      <ColumnDefinition Width="270"/>
      <ColumnDefinition Width="270"/>
    </Grid.ColumnDefinitions>
    <Border Grid.Column="1" Width="1" HorizontalAlignment="Left" Background="#E0E0E0" Margin="-2,0,0,0"/>
    <Border Grid.Column="2" Width="1" HorizontalAlignment="Left" Background="#E0E0E0" Margin="-2,0,0,0"/>
    <Border Grid.Column="3" Width="1" HorizontalAlignment="Left" Background="#E0E0E0" Margin="-2,0,0,0"/>
    <Border Grid.Column="4" Width="1" HorizontalAlignment="Left" Background="#E0E0E0" Margin="-2,0,0,0"/>
    <Border Grid.ColumnSpan="5" VerticalAlignment="Bottom" Height="1" Background="#E0E0E0"/>
    <StackPanel Grid.Column="0" Margin="2,3,8,3">
      <TextBlock Tag="0" Text="{Binding Node}" TextWrapping="Wrap" Foreground="{Binding NodeBrush}"/>
      <TextBlock Text="{Binding SubText}" FontStyle="Italic" FontSize="11" Foreground="#6B7280"
                 Visibility="{Binding SubTextVisibility}" Margin="0,1,0,0"/>
    </StackPanel>
    <TextBlock Tag="1" Grid.Column="1" Text="{Binding IdText}" VerticalAlignment="Top" Margin="2,3,8,3" Foreground="#6B7280"/>
    <TextBlock Grid.Column="2" Text="{Binding Text}" TextWrapping="Wrap" VerticalAlignment="Top" Margin="2,3,8,3" MaxHeight="90"/>
    <ItemsControl Tag="2" Grid.Column="3" ItemsSource="{Binding Cond}" IsTabStop="False" Margin="2,3,8,3">
      <ItemsControl.ItemTemplate>
        <DataTemplate><TextBlock Text="{Binding Text}" Foreground="{Binding Brush}" TextWrapping="Wrap"/></DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
    <ItemsControl Tag="3" Grid.Column="4" ItemsSource="{Binding Act}" IsTabStop="False" Margin="2,3,8,3">
      <ItemsControl.ItemTemplate>
        <DataTemplate><TextBlock Text="{Binding Text}" Foreground="{Binding Brush}" TextWrapping="Wrap"/></DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </Grid>
</HierarchicalDataTemplate>
""";
        return (HierarchicalDataTemplate)XamlReader.Parse(xaml);
    }

    private ContextMenu BuildContextMenu()
    {
        var cm = new ContextMenu();
        cm.Items.Add(TreeGridShared.Mi(L.T("＋任务"), AddTask));
        cm.Items.Add(TreeGridShared.Mi(L.T("＋目标"), AddObjective));
        cm.Items.Add(TreeGridShared.Mi(L.T("＋完成事件"), AddEvent));
        cm.Items.Add(TreeGridShared.Mi(L.T("＋接受事件"), AddIncentive));
        cm.Items.Add(new Separator());
        cm.Items.Add(TreeGridShared.Mi(L.T("－删除所选", "- Delete"), DeleteRow));
        cm.Items.Add(new Separator());
        cm.Items.Add(TreeGridShared.Mi(L.T("任务属性", "Quest Props"), EditQuestProps));
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
        var sel = SelectedRow;
        if (sel == null) return;

        if (tag == "2")
        {
            if (ScriptListDialog.Show(_doc, sel.Owner.Conditions, true, Window.GetWindow(this))) sel.Reload();
        }
        else if (tag == "3")
        {
            if (ScriptListDialog.Show(_doc, sel.Owner.Actions, false, Window.GetWindow(this))) sel.Reload();
        }
        else if (tag is "0" or "1")
        {
            EditFields();
        }
    }

    private void EditFields()
    {
        var sel = SelectedRow;
        if (sel == null) return;
        switch (sel.Owner)
        {
            case QstRootOwner:
            case QstQuestOwner:
                EditQuestProps();
                break;
            case QstTaskRow or QstEventRow or QstObjectiveRow:
                if (QstFieldsDialog.Show(sel.Owner, Window.GetWindow(this)))
                {
                    sel.Reload();
                    _changed();
                }
                break;
        }
    }

    private QstRow? SelectedRow => _list.SelectedItem is QstRow r ? r : null;

    private void EditQuestProps()
    {
        if (!QuestPropsDialog.Show(_qst, Window.GetWindow(this))) return;
        _changed();
        Bind(_doc, _changed);
    }

    public void NotifyEdited()
    {
        _doc.Dirty = true;
        _changed();
    }

    public void Bind(DocSession doc, Changed changed)
    {
        _doc = doc;
        _qst = doc.Qst!;
        _changed = changed;

        // 层级对齐官方 QuestEditor：
        //   组名（根，红） → 任务名（旗） → task [N] 标题/描述 → OnAccept / Objectives / OnComplete 组 → 事件/目标行
        var rows = new List<QstRow>();
        var rootRow = new QstRow(this, new QstRootOwner(_qst));
        rows.Add(rootRow);
        var questRow = new QstRow(this, new QstQuestOwner(_qst));
        rows.Add(questRow);
        questRow.Attach(rootRow);

        int no = 1;
        foreach (var t in _qst.Tasks)
        {
            int titleSlot = _qst.TaskTitleSlot(no - 1);
            var taskRow = new QstRow(this, new QstTaskRow(_qst, t, no - 1));
            rows.Add(taskRow);
            taskRow.Attach(questRow);

            var acceptRow = new QstRow(this, new QstGroupOwner(
                "▶ " + L.F("OnAccept（接受事件 ×{0}）", t.Incentives.Count), WpfTheme.AccentBrush));
            rows.Add(acceptRow);
            acceptRow.Attach(taskRow);
            for (int k = 0; k < t.Incentives.Count; k++)
            {
                var r = new QstRow(this, new QstEventRow(_qst, t, t.Incentives[k], titleSlot + 2 + k, k));
                rows.Add(r);
                r.Attach(acceptRow);
            }

            var objRow = new QstRow(this, new QstGroupOwner(
                "✅ " + L.F("Objectives（目标 ×{0}）", t.Objectives.Count), WpfTheme.OkBrush));
            rows.Add(objRow);
            objRow.Attach(taskRow);
            for (int k = 0; k < t.Objectives.Count; k++)
            {
                int slot = titleSlot + 2 + t.Incentives.Count + k;   // 串表槽：标题+描述之后依序排目标
                var r = new QstRow(this, new QstObjectiveRow(_qst, t, t.Objectives[k], slot));
                rows.Add(r);
                r.Attach(objRow);
            }

            var compRow = new QstRow(this, new QstGroupOwner(
                "🏁 " + L.F("OnComplete（完成事件 ×{0}）", t.Events.Count), WpfTheme.OkBrush));
            rows.Add(compRow);
            compRow.Attach(taskRow);
            for (int k = 0; k < t.Events.Count; k++)
            {
                var r = new QstRow(this, new QstEventRow(_qst, t, t.Events[k], titleSlot + 2 + t.Incentives.Count + t.Objectives.Count + k, k));
                rows.Add(r);
                r.Attach(compRow);
            }
            no++;
        }

        _rows = rows;
        _list.ItemsSource = _rows.Where(r => r.Parent == null).ToList();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        SelectedChanged?.Invoke();
    }

    private void UpdateHighlight()
    {
        foreach (var r in _rows) r.Highlight = false;
        if (_list.SelectedItem is QstRow sel)
            WalkHi(sel);
        return;

        void WalkHi(QstRow r)
        {
            r.Highlight = true;
            foreach (var c in r.Children) WalkHi(c);
        }
    }

    private QstTask ActiveTask()
    {
        for (var r = SelectedRow; r != null; r = r.Parent)
            if (r.Owner is QstTaskRow tr) return tr.T;
        return _qst.Tasks.FirstOrDefault()!;
    }

    public void SelectOwner(object owner)
    {
        var row = _rows.FirstOrDefault(r => ReferenceEquals(r.Owner.OwnerObject, owner));
        if (row == null) return;
        var path = new List<QstRow>();
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

    // 结构改动统一走 QstFile 的增删方法：它们会按公式 2+2T+O+I+E 重建本地化表。
    private void AddTask()
    {
        _qst.AddTask();
        _changed();
        Bind(_doc, _changed);
    }

    private void AddObjective()
    {
        var t = ActiveTask();
        if (t == null) return;
        _qst.AddObjective(t);
        _changed();
        Bind(_doc, _changed);
    }

    private void AddEvent()
    {
        var t = ActiveTask();
        if (t == null) return;
        _qst.AddEvent(t, incentive: false);
        _changed();
        Bind(_doc, _changed);
    }

    private void AddIncentive()
    {
        var t = ActiveTask();
        if (t == null) return;
        _qst.AddEvent(t, incentive: true);
        _changed();
        Bind(_doc, _changed);
    }

    private void DeleteRow()
    {
        var sel = SelectedRow;
        if (sel == null) return;
        switch (sel.Owner)
        {
            case QstTaskRow tr:
                if (!_qst.RemoveTask(tr.T)) return;
                break;
            case QstEventRow er:
                if (!_qst.RemoveEvent(er.OwnerTask, er.E)) return;
                break;
            case QstObjectiveRow or:
                if (!_qst.RemoveObjective(or.OwnerTask, or.O)) return;
                break;
            default:
                Gui.Warn(Window.GetWindow(this), L.T("该行是结构分组（组名 / 任务名 / OnAccept / Objectives / OnComplete），不能删除；请删除其下的具体行。"), L.T("删除", "Delete"));
                return;
        }
        _changed();
        Bind(_doc, _changed);
    }
}
