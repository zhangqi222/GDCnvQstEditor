using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GDCnvQstEditor;

/// <summary>
/// Win32 同步剪贴板（CF_UNICODETEXT）。
///
/// 之前用 Avalonia IClipboard.SetTextAsync / GetTextAsync：在右键菜单回调、Ctrl+C tunnel
/// 等场景实测写入静默失效（粘贴出来的是旧内容），异常被吞无法定位——改用 Win32 同步 API
/// （OpenClipboard 带重试 + EmptyClipboard + SetClipboardData），写入/读取同步完成，
/// 行为与记事本等原生程序一致。
/// </summary>
internal static class ClipboardHelper
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(nint hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetClipboardData(uint uFormat, nint hMem);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetClipboardData(uint uFormat);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint uFlags, nint dwBytes);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint hMem);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint hMem);

    /// <summary>同步写入文本；成功返回 true（OpenClipboard 最多重试 5 次 × 20ms）。</summary>
    public static bool SetText(string text)
    {
        if (text.Length == 0) return true;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!OpenClipboard(nint.Zero)) { Thread.Sleep(20); continue; }
            try
            {
                EmptyClipboard();
                nint h = GlobalAlloc(GMEM_MOVEABLE, (text.Length + 1) * 2);
                if (h == nint.Zero) return false;
                nint p = GlobalLock(h);
                if (p == nint.Zero) { GlobalFree(h); return false; }
                Marshal.Copy(text.ToCharArray(), 0, p, text.Length);
                Marshal.WriteInt16(p, text.Length * 2, 0);   // NUL 终止符
                GlobalUnlock(h);
                if (SetClipboardData(CF_UNICODETEXT, h) == nint.Zero)
                {
                    GlobalFree(h);   // 系统未接管，自行释放
                    return false;
                }
                return true;         // 成功后 hMem 归系统所有，不再释放
            }
            finally { CloseClipboard(); }
        }
        return false;
    }

    /// <summary>同步读取文本；无文本或未打开返回 null。</summary>
    public static string? GetText()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!OpenClipboard(nint.Zero)) { Thread.Sleep(20); continue; }
            try
            {
                nint h = GetClipboardData(CF_UNICODETEXT);
                if (h == nint.Zero) return null;
                nint p = GlobalLock(h);
                if (p == nint.Zero) return null;
                try { return Marshal.PtrToStringUni(p); }
                finally { GlobalUnlock(h); }
            }
            finally { CloseClipboard(); }
        }
        return null;
    }

    // ----------------------------------------------------------------------
    // TextBox 可靠复制（全局）
    //
    // Semi TextBox 主题自带的右键菜单/Ctrl+C 只复制"选中文本"，无选区时菜单项禁用、
    // 快捷键无动作——编辑弹窗内的串字段等表现为"无法复制"。
    // Install() 以类级路由（TextBox 附加到可视树时）为全程序所有 TextBox 统一启用：
    // 自建右键菜单（复制/粘贴/全选）+ tunnel 拦截 Ctrl+C，无选区 → 复制全文。
    // ----------------------------------------------------------------------

    private static bool _installed;

    /// <summary>在应用启动早期调用一次：为此后创建的所有 TextBox（含动态弹窗）启用可靠复制。</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        // 类级路由：任何 TextBox 触发 Loaded（每次进入可视树）时启用；Enable 幂等
        Control.LoadedEvent.AddClassHandler<TextBox>((box, _) => Enable(box));
    }

    /// <summary>为单个 TextBox 启用可靠复制（幂等）。</summary>
    public static void Enable(TextBox box)
    {
        if (box.ContextMenu?.Tag is "reliable") return;   // 已启用
        box.ContextFlyout = null;   // 关闭 Semi 默认 MenuFlyout（其"复制"在无选区时禁用）
        var copy = new MenuItem { Header = L.T("Copy text") };
        var paste = new MenuItem { Header = L.T("Paste") };
        var all = new MenuItem { Header = L.T("Select all") };
        copy.Click += (_, _) => CopyText(box);
        paste.Click += (_, _) => PasteText(box);
        all.Click += (_, _) => box.SelectAll();
        var menu = new ContextMenu { Tag = "reliable" };
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(all);
        menu.Opened += (_, _) =>
        {
            copy.IsEnabled = !string.IsNullOrEmpty(box.Text);
            paste.IsEnabled = !box.IsReadOnly && !string.IsNullOrEmpty(GetText());
        };
        box.ContextMenu = menu;

        box.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Handled || e.Key != Key.C || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            CopyText(box);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    /// <summary>复制 TextBox 文本：有选区复制选中，无选区复制全文。</summary>
    public static void CopyText(TextBox box)
    {
        string text = box.SelectedText.Length > 0 ? box.SelectedText : box.Text ?? "";
        if (text.Length > 0) SetText(text);
    }

    /// <summary>粘贴到光标处 / 替换选区。</summary>
    public static void PasteText(TextBox box)
    {
        string? text = GetText();
        if (string.IsNullOrEmpty(text)) return;
        if (box.SelectedText.Length > 0)
        {
            box.SelectedText = text;    // 替换选区（TextBox 内建行为）
        }
        else
        {
            string cur = box.Text ?? "";
            int i = Math.Clamp(box.CaretIndex, 0, cur.Length);
            box.Text = cur.Insert(i, text);
            box.CaretIndex = i + text.Length;
        }
    }
}
