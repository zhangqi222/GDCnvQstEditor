using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
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
        // 无参数（或显式 gui）→ 图形界面；带参数 → CLI（接管父进程控制台）。
        if (args.Length == 0 || (args.Length == 1 && args[0] == "gui"))
        {
            L.Load();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ShowFatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.Run(new MainForm());
            return 0;
        }

        if (args.Length == 1 && args[0] is "--help" or "-h" or "help")
        {
            args = ["help"];
        }

        return RunCli(args);
    }

    /// <summary>未捕获异常：落日志并弹框（避免整个程序静默闪退），用户点“确定”后续联操作。</summary>
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
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
