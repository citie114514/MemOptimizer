using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MemOpt
{
    internal sealed class MemSnapshot
    {
        public ulong Total;
        public ulong Available;
        public ulong CommitTotal;
        public ulong CommitLimit;
        public ulong SystemCache;
        public ulong Standby;
        public ulong Modified;
        public ulong Free;
        public ulong Zero;
        public uint PageSize;
        public uint LoadPercent;

        public ulong Used { get { return Total > Available ? Total - Available : 0; } }

        public static MemSnapshot Capture()
        {
            var s = new MemSnapshot();
            Native.QueryPhysical(out s.Total, out s.Available, out s.LoadPercent);
            ulong commitTotal, commitLimit, cache, pageSize;
            Native.QueryPerformance(out commitTotal, out commitLimit, out cache, out pageSize);
            s.CommitTotal = commitTotal;
            s.CommitLimit = commitLimit;
            s.SystemCache = cache;
            s.PageSize = pageSize == 0 ? (uint)Native.PageSize : (uint)pageSize;

            ulong standby, modified, free, zero;
            Native.QueryMemoryLists(out standby, out modified, out free, out zero);
            var page = (ulong)s.PageSize;
            s.Standby = standby * page;
            s.Modified = modified * page;
            s.Free = free * page;
            s.Zero = zero * page;
            return s;
        }
    }

    internal sealed class StepResult
    {
        public int Action;
        public string Key;
        public string Title;
        public bool Ok;
        public long Ms;
        public ulong FreedBytes;
        public string Error;

        public string ErrorShort
        {
            get
            {
                if (string.IsNullOrEmpty(Error)) return null;
                var text = Error.Replace("\r", " ").Replace("\n", " ");
                return text.Length <= 120 ? text : text.Substring(0, 117) + "…";
            }
        }
    }

    internal sealed class MemReport
    {
        public DateTime Time = DateTime.Now;
        public bool Elevated;
        public int Scope;
        public int DurationMs;
        public MemSnapshot Before;
        public MemSnapshot After;
        public List<StepResult> Steps = new List<StepResult>();

        public long DeltaBytes
        {
            get
            {
                if (After == null || Before == null) return 0;
                return (long)After.Available - (long)Before.Available;
            }
        }

        public bool AnySuccess
        {
            get
            {
                foreach (var s in Steps) if (s.Ok) return true;
                return false;
            }
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"tool\":\"MemOptimizer\",");
            sb.Append("\"version\":\"").Append(Program.Version).Append("\",");
            sb.Append("\"time\":\"").Append(Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append("\",");
            sb.Append("\"elevated\":").Append(Elevated ? "true" : "false").Append(',');
            sb.Append("\"scope\":").Append(Scope).Append(',');
            sb.Append("\"durationMs\":").Append(DurationMs).Append(',');
            sb.Append("\"deltaBytes\":").Append(DeltaBytes).Append(',');
            sb.Append("\"before\":").Append(SnapshotJson(Before)).Append(',');
            sb.Append("\"after\":").Append(SnapshotJson(After)).Append(',');
            sb.Append("\"steps\":[");
            for (var i = 0; i < Steps.Count; i++)
            {
                var step = Steps[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                sb.Append("\"key\":").Append(Quote(step.Key)).Append(',');
                sb.Append("\"title\":").Append(Quote(step.Title)).Append(',');
                sb.Append("\"ok\":").Append(step.Ok ? "true" : "false").Append(',');
                sb.Append("\"ms\":").Append(step.Ms).Append(',');
                sb.Append("\"freedBytes\":").Append(step.FreedBytes).Append(',');
                sb.Append("\"error\":").Append(step.Error == null ? "null" : Quote(step.ErrorShort));
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string SnapshotJson(MemSnapshot s)
        {
            if (s == null) return "null";
            return "{\"total\":" + s.Total +
                   ",\"available\":" + s.Available +
                   ",\"used\":" + s.Used +
                   ",\"commitTotal\":" + s.CommitTotal +
                   ",\"commitLimit\":" + s.CommitLimit +
                   ",\"systemCache\":" + s.SystemCache +
                   ",\"standby\":" + s.Standby +
                   ",\"modified\":" + s.Modified +
                   ",\"free\":" + s.Free +
                   ",\"pageSize\":" + s.PageSize +
                   ",\"loadPercent\":" + s.LoadPercent + "}";
        }

        private static string Quote(string value)
        {
            if (value == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public static MemReport FromJson(string json)
        {
            // 只用于读取本工具自己生成的报告，字段固定，做最小解析即可。
            var report = new MemReport();
            report.Elevated = json.Contains("\"elevated\":true");
            report.Scope = (int)ReadNumber(json, "\"scope\":");
            report.DurationMs = (int)ReadNumber(json, "\"durationMs\":");
            report.Before = ReadSnapshot(json, "\"before\":");
            report.After = ReadSnapshot(json, "\"after\":");

            var stepsIndex = json.IndexOf("\"steps\":[", StringComparison.Ordinal);
            if (stepsIndex >= 0)
            {
                var pos = stepsIndex + 9;
                while (true)
                {
                    var open = json.IndexOf('{', pos);
                    if (open < 0) break;
                    var close = FindObjectEnd(json, open);
                    if (close < 0) break;
                    var chunk = json.Substring(open, close - open + 1);
                    var step = new StepResult
                    {
                        Key = ReadString(chunk, "\"key\":"),
                        Title = ReadString(chunk, "\"title\":"),
                        Ok = chunk.Contains("\"ok\":true"),
                        Ms = (long)ReadNumber(chunk, "\"ms\":"),
                        FreedBytes = (ulong)ReadNumber(chunk, "\"freedBytes\":"),
                        Error = ReadString(chunk, "\"error\":")
                    };
                    report.Steps.Add(step);
                    pos = close + 1;
                }
            }
            return report;
        }

        private static int FindObjectEnd(string json, int start)
        {
            var depth = 0;
            for (var i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static MemSnapshot ReadSnapshot(string json, string marker)
        {
            var index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) return null;
            var start = json.IndexOf('{', index);
            var end = FindObjectEnd(json, start);
            if (start < 0 || end < 0) return null;
            var chunk = json.Substring(start, end - start + 1);
            return new MemSnapshot
            {
                Total = (ulong)ReadNumber(chunk, "\"total\":"),
                Available = (ulong)ReadNumber(chunk, "\"available\":"),
                CommitTotal = (ulong)ReadNumber(chunk, "\"commitTotal\":"),
                CommitLimit = (ulong)ReadNumber(chunk, "\"commitLimit\":"),
                SystemCache = (ulong)ReadNumber(chunk, "\"systemCache\":"),
                Standby = (ulong)ReadNumber(chunk, "\"standby\":"),
                Modified = (ulong)ReadNumber(chunk, "\"modified\":"),
                Free = (ulong)ReadNumber(chunk, "\"free\":"),
                PageSize = (uint)ReadNumber(chunk, "\"pageSize\":"),
                LoadPercent = (uint)ReadNumber(chunk, "\"loadPercent\":")
            };
        }

        private static double ReadNumber(string json, string marker)
        {
            var index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) return 0;
            var pos = index + marker.Length;
            while (pos < json.Length && (json[pos] == ' ')) pos++;
            var start = pos;
            while (pos < json.Length && (char.IsDigit(json[pos]) || json[pos] == '-' || json[pos] == '.')) pos++;
            double value;
            double.TryParse(json.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return value;
        }

        private static string ReadString(string json, string marker)
        {
            var index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) return null;
            var pos = index + marker.Length;
            while (pos < json.Length && json[pos] == ' ') pos++;
            if (pos >= json.Length || json[pos] != '"') return null;
            pos++;
            var sb = new StringBuilder();
            while (pos < json.Length)
            {
                var c = json[pos++];
                if (c == '\\' && pos < json.Length)
                {
                    var esc = json[pos++];
                    switch (esc)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append(esc); break;
                    }
                }
                else if (c == '"') break;
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
