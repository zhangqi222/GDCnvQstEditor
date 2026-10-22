using System.Reflection;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CnvQst;

namespace CnvQstEditor;

/// <summary>程序集版本（csproj &lt;Version&gt;）：关于对话框与状态栏共用。</summary>
internal static class AppInfo
{
    public static string Version { get; } = Resolve();

    private static string Resolve()
    {
        var asm = typeof(AppInfo).Assembly;
        string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
            return info.Split('+')[0];
        return asm.GetName().Version?.ToString(3) ?? "?";
    }
}

/// <summary>GUI 小工具（Avalonia 版）。</summary>
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
        _ = MsgBox.Show(owner, text, title, MsgButtons.Ok, warning: true);

    public static void Info(Window? owner, string text, string title) =>
        _ = MsgBox.Show(owner, text, title, MsgButtons.Ok, warning: false);
}

internal enum MsgButtons { Ok, OkCancel, YesNo, YesNoCancel }

internal enum MsgResult { Ok, Yes, No, Cancel }

/// <summary>轻量消息框（Avalonia 无内置 MessageBox）：可选中复制正文。</summary>
internal static class MsgBox
{
    public static async Task<MsgResult> Show(Window? owner, string text, string title, MsgButtons buttons, bool warning = false)
    {
        var dlg = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 380,
            MaxWidth = 720,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Padding = new Thickness(20, 16, 20, 16),
        };

        var msg = new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 680,
            Margin = new Thickness(0, 0, 0, 16),
        };
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };

        void MakeBtn(string label, MsgResult result, bool primary)
        {
            var b = new Button { Content = label, MinWidth = 88, MinHeight = 30 };
            if (primary) UiTheme.MakePrimary(b);
            b.Click += (_, _) => { dlg.Close(result); };
            btnRow.Children.Add(b);
        }

        switch (buttons)
        {
            case MsgButtons.Ok:
                MakeBtn(L.T("确定", "OK"), MsgResult.Ok, true);
                break;
            case MsgButtons.OkCancel:
                MakeBtn(L.T("确定", "OK"), MsgResult.Ok, true);
                MakeBtn(L.T("取消", "Cancel"), MsgResult.Cancel, false);
                break;
            case MsgButtons.YesNo:
                MakeBtn(L.T("是", "Yes"), MsgResult.Yes, true);
                MakeBtn(L.T("否", "No"), MsgResult.No, false);
                break;
            case MsgButtons.YesNoCancel:
                MakeBtn(L.T("是", "Yes"), MsgResult.Yes, true);
                MakeBtn(L.T("否", "No"), MsgResult.No, false);
                MakeBtn(L.T("取消", "Cancel"), MsgResult.Cancel, false);
                break;
        }

        dlg.Content = new StackPanel { Children = { msg, btnRow } };
        owner ??= (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner == null) return MsgResult.Ok;
        return await dlg.ShowDialog<MsgResult>(owner);
    }
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