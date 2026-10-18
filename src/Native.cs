using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MemOpt
{
    /// <summary>
    /// 与 PCL2-CE 的 NtInterop / MemSwapWorks 对齐的内核接口封装。
    /// 所有系统级动作都走 ntdll 的 NtSetSystemInformation，不结束进程、不写业务数据。
    /// </summary>
    internal static class Native
    {
        // SYSTEM_INFORMATION_CLASS
        public const int SystemMemoryListInformation = 80;
        public const int SystemFileCacheInformationEx = 81;
        public const int SystemCombinePhysicalMemoryInformation = 130;
        public const int SystemRegistryReconciliationInformation = 155;

        // SYSTEM_MEMORY_LIST_COMMAND
        public const int MemoryEmptyWorkingSets = 2;
        public const int MemoryFlushModifiedList = 3;
        public const int MemoryPurgeStandbyList = 4;
        public const int MemoryPurgeLowPriorityStandbyList = 5;

        // SE_PRIVILEGE
        public const uint SeIncreaseQuotaPrivilege = 5;
        public const uint SeProfileSingleProcessPrivilege = 13;

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int infoClass, IntPtr info, int infoLength);

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int infoLength, out int returnLength);

        [DllImport("ntdll.dll")]
        private static extern uint RtlNtStatusToDosError(int status);

        [DllImport("ntdll.dll")]
        private static extern int RtlAdjustPrivilege(uint privilege, [MarshalAs(UnmanagedType.U1)] bool enable,
            [MarshalAs(UnmanagedType.U1)] bool currentThread, [MarshalAs(UnmanagedType.U1)] out bool enabled);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetPerformanceInfo(ref PERFORMANCE_INFORMATION info, uint size);

        [DllImport("kernel32.dll")]
        private static extern void GetSystemInfo(ref SYSTEM_INFO info);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PERFORMANCE_INFORMATION
        {
            public uint Size;
            public UIntPtr CommitTotal;
            public UIntPtr CommitLimit;
            public UIntPtr CommitPeak;
            public UIntPtr PhysicalTotal;
            public UIntPtr PhysicalAvailable;
            public UIntPtr SystemCache;
            public UIntPtr KernelTotal;
            public UIntPtr KernelPaged;
            public UIntPtr KernelNonpaged;
            public UIntPtr PageSize;
            public uint HandleCount;
            public uint ProcessCount;
            public uint ThreadCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_INFO
        {
            public ushort ProcessorArchitecture;
            public ushort Reserved;
            public uint PageSize;
            public IntPtr MinimumApplicationAddress;
            public IntPtr MaximumApplicationAddress;
            public IntPtr ActiveProcessorMask;
            public uint NumberOfProcessors;
            public uint ProcessorType;
            public uint AllocationGranularity;
            public ushort ProcessorLevel;
            public ushort ProcessorRevision;
        }

        public static int PageSize
        {
            get
            {
                var info = new SYSTEM_INFO();
                GetSystemInfo(ref info);
                return info.PageSize <= 0 ? 4096 : (int)info.PageSize;
            }
        }

        public static void EnableMemoryPrivileges()
        {
            // 失败不抛：后面每个动作会以 NTSTATUS 明确报错，便于逐条展示。
            try { bool ignored; RtlAdjustPrivilege(SeProfileSingleProcessPrivilege, true, false, out ignored); }
            catch { }
            try { bool ignored; RtlAdjustPrivilege(SeIncreaseQuotaPrivilege, true, false, out ignored); }
            catch { }
        }

        public static void ThrowIfError(int status, string what)
        {
            if (status == 0) return;
            var win32 = (int)RtlNtStatusToDosError(status);
            throw new Win32Exception(win32, what + "（NTSTATUS 0x" + status.ToString("X8") + "）");
        }

        public static void SetMemoryListCommand(int command)
        {
            var buffer = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(buffer, command);
                var status = NtSetSystemInformation(SystemMemoryListInformation, buffer, 4);
                ThrowIfError(status, "NtSetSystemInformation(SystemMemoryListInformation=" + command + ")");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_FILECACHE_INFORMATION
        {
            public UIntPtr CurrentSize;
            public UIntPtr PeakSize;
            public UIntPtr PageFaultCount;
            public UIntPtr MinimumWorkingSet;
            public UIntPtr MaximumWorkingSet;
            public UIntPtr CurrentSizeIncludingTransitionInPages;
            public UIntPtr PeakSizeIncludingTransitionInPages;
            public UIntPtr TransitionRePurposeCount;
            public UIntPtr Flags;
        }

        public static void FlushFileCache()
        {
            var info = new SYSTEM_FILECACHE_INFORMATION
            {
                MinimumWorkingSet = new UIntPtr(uint.MaxValue),
                MaximumWorkingSet = new UIntPtr(uint.MaxValue)
            };
            var size = Marshal.SizeOf(typeof(SYSTEM_FILECACHE_INFORMATION));
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, buffer, false);
                var status = NtSetSystemInformation(SystemFileCacheInformationEx, buffer, size);
                ThrowIfError(status, "NtSetSystemInformation(SystemFileCacheInformationEx)");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public static void RegistryReconciliation()
        {
            var status = NtSetSystemInformation(SystemRegistryReconciliationInformation, IntPtr.Zero, 0);
            ThrowIfError(status, "NtSetSystemInformation(SystemRegistryReconciliationInformation)");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_COMBINE_INFORMATION_EX
        {
            public IntPtr Handle;
            public UIntPtr PagesCombined;
            public uint Flags;
        }

        public static void CombinePhysicalMemory()
        {
            var info = new MEMORY_COMBINE_INFORMATION_EX();
            var size = Marshal.SizeOf(typeof(MEMORY_COMBINE_INFORMATION_EX));
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, buffer, false);
                var status = NtSetSystemInformation(SystemCombinePhysicalMemoryInformation, buffer, size);
                ThrowIfError(status, "NtSetSystemInformation(SystemCombinePhysicalMemoryInformation)");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public static void QueryPhysical(out ulong total, out ulong available, out uint loadPercent)
        {
            var mem = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (!GlobalMemoryStatusEx(ref mem)) throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalMemoryStatusEx");
            total = mem.TotalPhys;
            available = mem.AvailPhys;
            loadPercent = mem.MemoryLoad;
        }

        public static void QueryPerformance(out ulong commitTotal, out ulong commitLimit, out ulong systemCache, out ulong pageSize)
        {
            var info = new PERFORMANCE_INFORMATION();
            info.Size = (uint)Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION));
            if (!GetPerformanceInfo(ref info, info.Size)) throw new Win32Exception(Marshal.GetLastWin32Error(), "GetPerformanceInfo");
            commitTotal = (ulong)info.CommitTotal.ToUInt64() * info.PageSize.ToUInt64();
            commitLimit = (ulong)info.CommitLimit.ToUInt64() * info.PageSize.ToUInt64();
            systemCache = (ulong)info.SystemCache.ToUInt64() * info.PageSize.ToUInt64();
            pageSize = info.PageSize.ToUInt64();
        }

        /// <summary>
        /// 读取待命/已修改/空闲页数量。结构随系统版本增长，只读取前 22 个 SIZE_T。
        /// </summary>
        public static void QueryMemoryLists(out ulong standby, out ulong modified, out ulong free, out ulong zero)
        {
            standby = modified = free = zero = 0;
            var size = IntPtr.Size * 22;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                for (var i = 0; i < size; i++) Marshal.WriteByte(buffer, i, 0);
                int returned;
                var status = NtQuerySystemInformation(SystemMemoryListInformation, buffer, size, out returned);
                if (status != 0) return;

                Func<int, ulong> read = index =>
                {
                    var p = IntPtr.Add(buffer, index * IntPtr.Size);
                    return IntPtr.Size == 8 ? (ulong)Marshal.ReadInt64(p) : (uint)Marshal.ReadInt32(p);
                };

                zero = read(0);
                free = read(1);
                modified = read(2);
                // 3 = modified-no-write, 4 = bad
                for (var i = 5; i < 13; i++) standby += read(i);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
