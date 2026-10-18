using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MemOpt
{
    internal static class Optimizer
    {
        // 位值与 PCL2-CE 的 MemSwapScope 保持一致，--scope 可直接复用。
        public const int EmptyWorkingSets = 1;
        public const int FlushFileCache = 2;
        public const int FlushModifiedList = 4;
        public const int PurgeStandbyList = 8;
        public const int PurgeLowPriorityStandbyList = 16;
        public const int RegistryReconciliation = 32;
        public const int CombinePhysicalMemory = 64;

        public const int DefaultScope = EmptyWorkingSets | FlushFileCache | FlushModifiedList | PurgeStandbyList;
        public const int AllScope = 127;

        internal sealed class ActionInfo
        {
            public int Bit;
            public string Key;
            public string Title;
            public string Desc;
            public bool DefaultOn;
        }

        // 执行顺序与 PCL2-CE MemSwapService._OnMemorySwap 一致。
        public static readonly ActionInfo[] Actions =
        {
            new ActionInfo { Bit = EmptyWorkingSets, Key = "EmptyWorkingSets", Title = "清空工作集", Desc = "系统级裁掉常驻页 · 最核心", DefaultOn = true },
            new ActionInfo { Bit = FlushFileCache, Key = "FlushFileCache", Title = "刷新文件缓存", Desc = "收缩系统文件缓存", DefaultOn = true },
            new ActionInfo { Bit = FlushModifiedList, Key = "FlushModifiedList", Title = "写回已修改页", Desc = "脏页落盘后释放物理页", DefaultOn = true },
            new ActionInfo { Bit = PurgeStandbyList, Key = "PurgeStandbyList", Title = "清理待命列表", Desc = "释放已缓存物理页 · 效果最明显", DefaultOn = true },
            new ActionInfo { Bit = PurgeLowPriorityStandbyList, Key = "PurgeLowPriorityStandbyList", Title = "低优先级待命", Desc = "温和版待命页清理", DefaultOn = false },
            new ActionInfo { Bit = RegistryReconciliation, Key = "RegistryReconciliation", Title = "注册表重整", Desc = "Win10+ 内核接口 · 实验", DefaultOn = false },
            new ActionInfo { Bit = CombinePhysicalMemory, Key = "CombinePhysicalMemory", Title = "合并物理内存", Desc = "页合并 · 多为空操作", DefaultOn = false }
        };

        public static ActionInfo FindAction(int bit)
        {
            foreach (var a in Actions) if (a.Bit == bit) return a;
            return null;
        }

        public static MemReport Run(int scope, Action<StepResult> onStep = null)
        {
            var report = new MemReport
            {
                Scope = scope,
                Elevated = Elevation.IsAdmin,
                Time = DateTime.Now
            };
            var total = Stopwatch.StartNew();
            Native.EnableMemoryPrivileges();
            report.Before = MemSnapshot.Capture();

            foreach (var action in Actions)
            {
                if ((scope & action.Bit) == 0) continue;

                var step = new StepResult { Action = action.Bit, Key = action.Key, Title = action.Title };
                ulong beforeAvail = 0, afterAvail = 0;
                var watch = Stopwatch.StartNew();
                try
                {
                    Native.QueryPhysical(out _, out beforeAvail, out _);
                    Execute(action.Bit);
                    step.Ok = true;
                }
                catch (Exception ex)
                {
                    step.Ok = false;
                    step.Error = ex.Message;
                }
                watch.Stop();
                step.Ms = watch.ElapsedMilliseconds;

                try
                {
                    Thread.Sleep(120);
                    Native.QueryPhysical(out _, out afterAvail, out _);
                    if (afterAvail > beforeAvail) step.FreedBytes = afterAvail - beforeAvail;
                }
                catch { }

                report.Steps.Add(step);
                if (onStep != null) onStep(step);
            }

            // 给内存管理器一点时间把页归位，再取最终快照。
            Thread.Sleep(350);
            report.After = MemSnapshot.Capture();
            total.Stop();
            report.DurationMs = (int)total.ElapsedMilliseconds;
            return report;
        }

        private static void Execute(int bit)
        {
            switch (bit)
            {
                case EmptyWorkingSets:
                    Native.SetMemoryListCommand(Native.MemoryEmptyWorkingSets);
                    break;
                case FlushFileCache:
                    Native.FlushFileCache();
                    break;
                case FlushModifiedList:
                    Native.SetMemoryListCommand(Native.MemoryFlushModifiedList);
                    break;
                case PurgeStandbyList:
                    Native.SetMemoryListCommand(Native.MemoryPurgeStandbyList);
                    break;
                case PurgeLowPriorityStandbyList:
                    Native.SetMemoryListCommand(Native.MemoryPurgeLowPriorityStandbyList);
                    break;
                case RegistryReconciliation:
                    Native.RegistryReconciliation();
                    break;
                case CombinePhysicalMemory:
                    Native.CombinePhysicalMemory();
                    break;
                default:
                    throw new ArgumentOutOfRangeException("bit");
            }
        }
    }
}
