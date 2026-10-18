using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace MemOpt
{
    internal static class Format
    {
        public static string Bytes(double bytes)
        {
            if (bytes >= 1024d * 1024 * 1024) return (bytes / 1024d / 1024 / 1024).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 100d * 1024 * 1024) return (bytes / 1024d / 1024).ToString("0", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= 1024d * 1024) return (bytes / 1024d / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= 1024d) return (bytes / 1024d).ToString("0", CultureInfo.InvariantCulture) + " KB";
            return bytes.ToString("0", CultureInfo.InvariantCulture) + " B";
        }

        public static string Gb(double bytes)
        {
            return (bytes / 1024d / 1024 / 1024).ToString("0.00", CultureInfo.InvariantCulture);
        }

        public static string Signed(double bytes)
        {
            var sign = bytes > 0 ? "+" : bytes < 0 ? "-" : "";
            return sign + Bytes(Math.Abs(bytes));
        }
    }

    internal static class ConsoleHelper
    {
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        private static bool? _hasConsole;

        public static bool HasConsole
        {
            get
            {
                if (_hasConsole.HasValue) return _hasConsole.Value;
                // 没有父控制台时不做 AllocConsole，避免静默模式下闪出黑窗。
                var ok = AttachConsole(AttachParentProcess);
                if (ok)
                {
                    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
                }
                _hasConsole = ok;
                return ok;
            }
        }

        public static bool Write(string text)
        {
            if (!HasConsole) return false;
            try { Console.WriteLine(text); } catch { }
            return true;
        }
    }

    internal sealed class CliOptions
    {
        public bool Help;
        public bool Memory;
        public bool Silent;
        public bool NoElevate;
        public bool Elevated;
        public bool Autorun;
        public int Scope = Optimizer.DefaultScope;
        public string ReportPath;

        public static CliOptions Parse(string[] args)
        {
            var o = new CliOptions();
            for (var i = 0; i < args.Length; i++)
            {
                var raw = args[i] ?? "";
                var arg = raw.Trim();
                var lower = arg.ToLowerInvariant();

                switch (lower)
                {
                    case "--help":
                    case "-h":
                    case "/?":
                    case "-?":
                        o.Help = true;
                        break;
                    case "--memory":
                    case "-m":
                    case "--run":
                        o.Memory = true;
                        break;
                    case "--silent":
                    case "-q":
                        o.Silent = true;
                        break;
                    case "--no-elevate":
                        o.NoElevate = true;
                        break;
                    case "--elevated":
                        o.Elevated = true;
                        break;
                    case "--autorun":
                        o.Autorun = true;
                        break;
                    default:
                        if (lower.StartsWith("--scope=")) o.Scope = ParseScope(arg.Substring(8));
                        else if (lower == "--scope" && i + 1 < args.Length) o.Scope = ParseScope(args[++i]);
                        else if (lower.StartsWith("--report=")) o.ReportPath = StripQuotes(arg.Substring(9));
                        else if (lower == "--report" && i + 1 < args.Length) o.ReportPath = StripQuotes(args[++i]);
                        break;
                }
            }
            return o;
        }

        private static string StripQuotes(string value)
        {
            return value == null ? null : value.Trim().Trim('"');
        }

        private static int ParseScope(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Optimizer.DefaultScope;
            value = value.Trim();
            int parsed;
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed)) return parsed & Optimizer.AllScope;
            }
            else if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed & Optimizer.AllScope;
            }
            return Optimizer.DefaultScope;
        }
    }
}
