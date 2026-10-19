using System.Windows;
using System.Windows.Input;
using CnvQst;

namespace CnvQstStudio;

/// <summary>GUI 小工具（WPF 版）。</summary>
internal static class Gui
{
    /// <summary>u32 显示：大数值按 hex（多为 hash/任务标识），小数值按十进制。</summary>
    public static string U32Text(uint v) => v >= 0x10000 ? "0x" + v.ToString("X") : v.ToString();

    public static uint ParseU32(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToUInt32(s[2..], 16);
        return string.IsNullOrEmpty(s) ? 0 : uint.Parse(s);
    }

    public static string Describe(ScriptRecord r) => Dump.Describe(r);

    public static void Warn(Window? owner, string text, string title) =>
        MessageBox.Show(owner, text, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void Info(Window? owner, string text, string title) =>
        MessageBox.Show(owner, text, title, MessageBoxButton.OK, MessageBoxImage.Information);
}

/// <summary>无参数命令（菜单 / KeyBinding 用）。</summary>
internal sealed class RelayCommand(Action execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}

/// <summary>编辑变化回调载体：刷新脏标记/状态栏/树节点。</summary>
internal delegate void Changed();
