using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CnvQst;
using CnvQst.Cli;

namespace CnvQstEditor;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    private const int AttachParentProcess = -1;

    private static int Main(string[] args)
    {
        // 无参数（或显式 gui）→ 图形界面；--smoke → 建窗即关（CI/回归用）；带参数 → CLI。
        // 未捕获异常兜底最先挂载（启动早期崩溃也能落日志）。
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);

        if (args.Length == 0 || (args.Length == 1 && args[0] == "gui"))
        {
            try
            {
                L.Load();
                CnvQst.Names.DisplayNameResolver = name => L.TypePair(name);
                ClipboardHelper.Install();   // 全局 TextBox 可靠复制（右键菜单 + Ctrl+C 无选区复制全文）

                // 直接进编辑器（v2.0.1 起取消启动器页）。
                var app = BuildApp();
                Dispatcher.UIThread.UnhandledException += (_, e) =>
                {
                    ShowFatal(e.Exception);
                    e.Handled = true;
                };

                var main = new MainWindow();
                app.Run(main);
                return 0;
            }
            catch (Exception e)
            {
                WriteCrashLog(e);
                Console.Error.WriteLine("UI 启动失败：" + e);
                return 1;
            }
        }

        if (args.Length == 1 && args[0] == "--smoke")
            return RunSmoke();

        return RunCli(args);
    }

    /// <summary>构建 Avalonia 应用（Semi.Avalonia 主题），不启动生命周期。</summary>
    private static Application BuildApp()
    {
        var builder = AppBuilder.Configure<EditorApp>().UsePlatformDetect();
        builder.SetupWithoutStarting();
        var app = builder.Instance!;
        app.Name = "CnvQstEditor";
        return app;
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
            CnvQst.Names.DisplayNameResolver = name => L.TypePair(name);
            ClipboardHelper.Install();

            var app = BuildApp();
            Exception? failure = null;
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                failure = e.Exception;
                Console.WriteLine("UI 冒烟失败：" + e.Exception);
                e.Handled = true;
            };

            // 冒烟：启动界面显示后，自动打开编辑器主窗（覆盖两窗口构建），1.2 秒后全部关闭。
            var main = new MainWindow();
            main.Activated += (_, _) =>
            {
                try
                {
                    // 冒烟增强：用内存新建文档各开一页，实际构建 CNV/QST 两个 TreeDataGrid 表格。
                    main.OpenSmokeTab(Creation.NewConversation("Smoke", "你好，冒烟测试。", "再见。"));
                    main.OpenSmokeTab(Creation.NewQuest("SmokeGroup", "SmokeQuest", "任务一", "这是冒烟测试任务。"));
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                    timer.Tick += (_, _) => { timer.Stop(); main.Close(); };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    failure = ex;
                    Console.WriteLine("UI 冒烟失败：" + ex);
                    main.Close();
                }
            };

            app.Run(main);

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

    /// <summary>未捕获异常：落日志并弹框（避免整个程序静默闪退）。</summary>
    private static string WriteCrashLog(Exception ex)
    {
        try
        {
            string log = Path.Combine(Path.GetTempPath(), "CnvQstEditor_crash.log");
            File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            return log;
        }
        catch
        {
            return "(日志写入失败)";
        }
    }

    private static void ShowFatal(Exception? ex)
    {
        if (ex == null) return;
        string log = WriteCrashLog(ex);
        try
        {
            string msg = ex.ToString();
            _ = MsgBox.Show(null,
                "程序遇到未预期的错误：\n\n" + (msg.Length > 1200 ? msg[..1200] + "…（详见 " + log + "）" : msg) + "\n\n" + log,
                "CnvQstEditor 错误", MsgButtons.Ok, warning: true);
        }
        catch
        {
            // 弹框失败也不能再引发新的崩溃
        }
    }

    private static int RunCli(string[] args)
    {
        // 设 CNVQST_NO_ATTACH=1（脚本/自动化捕获 stdout 场景）或 cli-from-parent 时不附加父控制台
        bool attached = args[0] != "cli-from-parent"
            && Environment.GetEnvironmentVariable("CNVQST_NO_ATTACH") != "1"
            && AttachConsole(AttachParentProcess);
        if (attached)
        {
            // AttachConsole 后重新连接 CONOUT$，使 Console.Out/Err 指向父控制台。
            try { Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true }); }
            catch { }
            try { Console.SetError(new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true }); }
            catch { }
        }
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        int code = CliApp.Run(args, Console.Out, Console.Error);

        if (attached)
        {
            // 命令结尾补一个换行，避免 PowerShell 提示符粘连在输出行尾。
            Console.Out.WriteLine();
            FreeConsole();
        }
        return code;
    }
}
