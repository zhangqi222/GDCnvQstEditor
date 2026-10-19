using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CnvQst;
using CnvQst.Cli;

namespace CnvQstStudio;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    private const int AttachParentProcess = -1;

    [STAThread]
    private static int Main(string[] args)
    {
        // 无参数（或显式 gui）→ 图形界面；--smoke → 建窗即关（CI/回归用）；带参数 → CLI。
        if (args.Length == 0 || (args.Length == 1 && args[0] == "gui"))
        {
            L.Load();
            CnvQst.Names.DisplayNameResolver = name => L.Type(name, name);

            // 两层 UI：先启动界面（编辑器 / 批量转换），选择后进入对应窗口。
            var app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
            WpfTheme.Init(app);
            app.DispatcherUnhandledException += (_, e) =>
            {
                ShowFatal(e.Exception);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);

            var launcher = new LauncherWindow();
            app.Run(launcher);
            return 0;
        }

        if (args.Length == 1 && args[0] == "--smoke")
            return RunSmoke();

        if (args.Length == 2 && args[0] == "--treedbg")
            return RunTreeDebug(args[1]);

        if (args.Length == 1 && args[0] is "--help" or "-h" or "help")
        {
            args = ["help"];
        }

        return RunCli(args);
    }

    /// <summary>
    /// UI 冒烟测试：建主窗口（含全部控件与主题/语言初始化）、1.2 秒后自动关闭。
    /// 不弹异常框、只把异常打到 stdout 并返回 1，便于脚本化回归。
    /// </summary>
    private static int RunSmoke()
    {
        bool attached = AttachConsole(AttachParentProcess);
        if (attached)
        {
            try { Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true }); }
            catch { }
        }
        try
        {
            L.Load();
            CnvQst.Names.DisplayNameResolver = name => L.Type(name, name);

            var app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
            WpfTheme.Init(app);
            Exception? failure = null;
            app.DispatcherUnhandledException += (_, e) =>
            {
                failure = e.Exception;
                Console.WriteLine("UI 冒烟失败：" + e.Exception);
                e.Handled = true;
            };

            // 冒烟：启动界面显示后，自动打开编辑器主窗（覆盖两窗口构建），1.2 秒后全部关闭。
            var launcher = new LauncherWindow();
            launcher.Activated += (_, _) =>
            {
                try
                {
                    var editor = new MainWindow();
                    editor.Show();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                    timer.Tick += (_, _) => { timer.Stop(); editor.Close(); launcher.Close(); };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    failure = ex;
                    Console.WriteLine("UI 冒烟失败：" + ex);
                    launcher.Close();
                }
            };

            app.Run(launcher);

            if (failure != null)
            {
                if (attached) FreeConsole();
                return 1;
            }
            Console.WriteLine($"UI 冒烟通过（语言 {L.Code} / {L.DisplayName}，可用语言 {L.Languages.Count} 种）。");
            if (attached) { Console.Out.WriteLine(); FreeConsole(); }
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("UI 冒烟异常：" + e);
            if (attached) FreeConsole();
            return 1;
        }
    }

    /// <summary>临时诊断：绑定 qst 文件后检查树形模板内 spacer 宽度绑定是否生效。</summary>
    private static int RunTreeDebug(string qstPath)
    {
        bool attached = AttachConsole(AttachParentProcess);
        if (attached)
        {
            try { Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true }); }
            catch { }
        }
        try
        {
            L.Load();
            var app = new Application();
            WpfTheme.Init(app);
            app.DispatcherUnhandledException += (_, e) => { Console.WriteLine("ERR: " + e.Exception); e.Handled = true; };
            var win = new Window { Width = 1200, Height = 700 };
            var editor = new QstTableEditor();
            win.Content = editor;
            var doc = DocSession.Open(qstPath);
            editor.Bind(doc, () => { });
            win.Loaded += (_, _) =>
            {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    Walk(win, "");
                    win.Close();
                };
                timer.Start();
            };

            app.Run(win);

            if (attached) FreeConsole();
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("TREEDBG 异常：" + e);
            if (attached) FreeConsole();
            return 1;
        }

        static void Walk(DependencyObject d, string path)
        {
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (child is MaterialDesignThemes.Wpf.TreeListViewItem item && item.DataContext is QstRow row)
                {
                    var t = typeof(MaterialDesignThemes.Wpf.TreeListViewItem);
                    var tl = t.GetField("TreeListView", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(item);
                    var cp = item.Template?.FindName("PART_ContentPresenter", item);
                    var presenterTemplate = cp?.GetType().GetProperty("Template")?.GetValue(cp) != null;
                    Console.WriteLine($"{path}TreeItem Level={item.Level} IsExpanded={item.IsExpanded} HasItems={item.HasItems} TreeListView={(tl != null)} PresenterTemplate={presenterTemplate} Node={row.Node}");
                }
                Walk(child, path + "/");
            }
        }
    }

    /// <summary>未捕获异常：落日志并弹框（避免整个程序静默闪退），用户点“确定”后继续操作。</summary>    /// <summary>未捕获异常：落日志并弹框（避免整个程序静默闪退），用户点“确定”后继续操作。</summary>
    private static void ShowFatal(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            string log = Path.Combine(Path.GetTempPath(), "CnvQstStudio_crash.log");
            File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            string msg = ex.ToString();
            MessageBox.Show(
                "程序遇到未预期的错误：\n\n" + (msg.Length > 1200 ? msg[..1200] + "…（详见 " + log + "）" : msg) + "\n\n" + log,
                "CnvQstStudio 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // 日志/弹框失败也不能再引发新的崩溃
        }
    }

    private static int RunCli(string[] args)
    {
        bool attached = args[0] != "cli-from-parent" && AttachConsole(AttachParentProcess);
        if (attached)
        {
            // AttachConsole 后重新连接 CONOUT$，使 Console.Out/Err 指向父控制台。
            try { Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true }); }
            catch { }
            try { Console.SetError(new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true }); }
            catch { }
        }
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        int code;
        if (args[0] == "help")
        {
            Console.Error.WriteLine(CliApp.Usage);
            code = 2;
        }
        else
        {
            code = CliApp.Run(args, Console.Out, Console.Error);
        }

        if (attached)
        {
            // 命令结尾补一个换行，避免 PowerShell 提示符粘连在输出行尾。
            Console.Out.WriteLine();
            FreeConsole();
        }
        return code;
    }
}
