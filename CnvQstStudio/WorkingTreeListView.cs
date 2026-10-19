using System.Reflection;
using System.Windows;
using MaterialDesignThemes.Wpf;

namespace CnvQstStudio;

/// <summary>
/// 修复版 TreeListView：MaterialDesign 5.3.2 的 PrepareContainerForItemOverride 被注释，
/// 容器准备（Children 绑定/层级缩进/展开态）不会自动执行，树无法展开。
/// 这里在基类准备完成后用内部集合的 level/展开态手动驱动 TreeListViewItem 的准备逻辑。
/// </summary>
internal sealed class WorkingTreeListView : TreeListView
{
    private static readonly MethodInfo? PrepareMi =
        typeof(TreeListViewItem).GetMethod("PrepareTreeListViewItem", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly PropertyInfo? InternalItemsSourceProperty =
        typeof(TreeListView).GetProperty("InternalItemsSource", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly PropertyInfo? TreeListViewProperty =
        typeof(TreeListViewItem).GetProperty("TreeListView", BindingFlags.NonPublic | BindingFlags.Instance);

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is not TreeListViewItem tli || PrepareMi == null) return;

        var src = InternalItemsSourceProperty?.GetValue(this);
        if (src == null) { Console.WriteLine("[WTL] src null"); return; }
        int index = ItemContainerGenerator.IndexFromContainer(tli);
        if (index < 0) return;

        int level = (int)src.GetType().GetMethod("GetLevel")!.Invoke(src, new object[] { index })!;
        bool isExpanded = (bool)src.GetType().GetMethod("GetIsExpanded")!.Invoke(src, new object[] { index })!;
        PrepareMi.Invoke(tli, new[] { item, (object)this, (object)level, (object)isExpanded });

        // 默认全展开：延后到内部初始化（会把 IsExpanded 重置为 false）之后，
        // IsExpanded=true 触发 ItemExpandedChanged → 子级插入 → 子容器递归准备
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            if (TreeListViewProperty?.GetValue(tli) == null) TreeListViewProperty.SetValue(tli, this);
            try { tli.IsExpanded = true; }
            catch { /* 展开失败保持收起，用户可手动展开 */ }
        }));
    }
}
