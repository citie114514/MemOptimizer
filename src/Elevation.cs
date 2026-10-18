using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;

namespace MemOpt
{
    internal static class Elevation
    {
        private static bool? _isAdmin;

        public static bool IsAdmin
        {
            get
            {
                if (_isAdmin.HasValue) return _isAdmin.Value;
                try
                {
                    var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    _isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch
                {
                    _isAdmin = false;
                }
                return _isAdmin.Value;
            }
        }

        /// <summary>
        /// 以管理员身份启动自身执行一次静默整理，读取子进程写出的 JSON 报告。
        /// 已提权时不会递归（参数带 --elevated）。
        /// </summary>
        public static MemReport RunElevated(int scope, out string error)
        {
            error = null;
            var temp = Path.Combine(Path.GetTempPath(), "memopt-" + Guid.NewGuid().ToString("N") + ".json");
            var args = "--memory --elevated --silent --scope=" + scope + " --report=\"" + temp + "\"";
            try
            {
                var info = new ProcessStartInfo(Application.ExecutablePath, args)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var process = Process.Start(info))
                {
                    if (process == null)
                    {
                        error = "无法启动提权进程";
                        return null;
                    }
                    process.WaitForExit();
                }
            }
            catch (Win32Exception ex)
            {
                error = ex.NativeErrorCode == 1223 ? "已取消管理员授权" : "提权失败：" + ex.Message;
                return null;
            }
            catch (Exception ex)
            {
                error = "提权失败：" + ex.Message;
                return null;
            }

            if (!File.Exists(temp))
            {
                error = "提权进程未生成结果（可能被拦截）";
                return null;
            }

            try
            {
                return MemReport.FromJson(File.ReadAllText(temp));
            }
            catch (Exception ex)
            {
                error = "读取结果失败：" + ex.Message;
                return null;
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }

        public static int RelaunchForConsole(CliOptions options)
        {
            var report = RunElevated(options.Scope, out var error);
            if (report == null)
            {
                ConsoleHelper.Write("内存优化未执行：" + (error ?? "未知错误"));
                return 1;
            }

            ConsoleHelper.Write(Summary.Build(report));
            if (!string.IsNullOrEmpty(options.ReportPath))
            {
                try { File.WriteAllText(options.ReportPath, report.ToJson(), System.Text.Encoding.UTF8); }
                catch (Exception ex) { ConsoleHelper.Write("写入报告失败：" + ex.Message); }
            }
            return report.AnySuccess ? 0 : 1;
        }
    }

    internal static class Summary
    {
        public static string Build(MemReport report)
        {
            if (report == null) return "没有结果";

            var lines = new System.Text.StringBuilder();
            var delta = report.DeltaBytes;
            if (delta > 0)
            {
                lines.AppendLine("内存优化结束：释放 " + Format.Bytes(delta) +
                                 "（可用 " + Format.Bytes(report.Before.Available) + " → " + Format.Bytes(report.After.Available) + "）");
            }
            else
            {
                lines.AppendLine("内存优化结束：本次没有可释放的页（可用 " + Format.Bytes(report.After.Available) +
                                 "，系统本身足够空闲）");
            }
            lines.AppendLine("耗时 " + (report.DurationMs / 1000.0).ToString("0.0") + " s" +
                             (report.Elevated ? " · 管理员" : " · 普通权限"));

            foreach (var step in report.Steps)
            {
                lines.Append("  ").Append(step.Ok ? "✓ " : "✗ ").Append(step.Title);
                if (step.Ok) lines.Append("  ").Append(step.Ms).Append(" ms");
                else lines.Append("  ").Append(step.ErrorShort);
                lines.AppendLine();
            }
            return lines.ToString().TrimEnd();
        }
    }
}
