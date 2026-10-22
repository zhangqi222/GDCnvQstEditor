using System.Text;
using CnvQst.Cli;

// CnvQstEditor-cli — CnvQstLib CLI 的开发/CI 薄壳。命令实现全部在 CnvQstLib.Cli.CliApp，
// CnvQstEditor 单 EXE 的参数模式调用同一实现，保证 GUI/CLI 行为一致。
// 编码设置放这里（库内不碰 Console，便于被 GUI 宿主）。

try { Console.OutputEncoding = Encoding.UTF8; } catch { /* 重定向环境可能不支持 */ }
return CliApp.Run(args, Console.Out, Console.Error);
