using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MemOpt
{
    internal static class Program
    {
        public const string Version = "1.0.0";

        public const string HelpText =
@"内存优化 · MemOptimizer " + Version + @"
基于 PCL2-CE (MemSwapWorks / NtInterop) 的独立实现，调用 NT 内核内存整理接口。

用法：
  MemOptimizer.exe                      打开图形界面
  MemOptimizer.exe --memory             执行一次整理（需要时会请求管理员权限）
  MemOptimizer.exe --memory --silent    静默执行，不弹结果窗
  MemOptimizer.exe --scope=15           指定动作位掩码（默认 15）
  MemOptimizer.exe --report=path.json   把结果写入 JSON
  MemOptimizer.exe --help               显示本帮助

动作位（与 PCL2-CE 的 MemSwapScope 一致）：
  1  清空工作集 EmptyWorkingSets        2  刷新文件缓存 FlushFileCache
  4  写回已修改页 FlushModifiedList     8  清理待命列表 PurgeStandbyList
  16 低优先级待命                       32 注册表重整
  64 合并物理内存

示例：
  MemOptimizer.exe --memory --silent --report=D:\mem.json";

        [STAThread]
        private static int Main(string[] args)
        {
            var options = CliOptions.Parse(args);

            if (options.Help)
            {
                if (!ConsoleHelper.Write(HelpText))
                {
                    MessageBox.Show(HelpText, "内存优化 · 帮助", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }

            if (options.Memory) return RunMemory(options);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(options));
            return 0;
        }

        private static int RunMemory(CliOptions options)
        {
            // 普通权限下自动提权执行；--elevated 防止递归。
            if (!Elevation.IsAdmin && !options.Elevated && !options.NoElevate)
                return Elevation.RelaunchForConsole(options);

            var report = Optimizer.Run(options.Scope, null);

            if (!string.IsNullOrEmpty(options.ReportPath))
            {
                try { File.WriteAllText(options.ReportPath, report.ToJson(), Encoding.UTF8); }
                catch (Exception ex) { ConsoleHelper.Write("写入报告失败：" + ex.Message); }
            }

            var text = Summary.Build(report);
            if (options.Silent) ConsoleHelper.Write(text);
            else MessageBox.Show(text, "内存优化 · 结果", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return report.AnySuccess ? 0 : 1;
        }
    }
}
