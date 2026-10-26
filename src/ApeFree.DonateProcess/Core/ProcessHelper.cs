using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace ApeFree.DonateProcess.Core
{
    /// <summary>
    /// 进程与系统底层交互帮助类
    /// </summary>
    public static class ProcessHelper
    {
        private const int PROCESS_SUSPEND_RESUME = 0x0800;
        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtResumeProcess(IntPtr processHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, [Out] char[] lpExeName, ref int lpdwSize);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        /// <summary>
        /// 获取所有运行中进程的命令行与路径缓存（通过 WMI 批量获取，显著提升查询效率）
        /// </summary>
        public static Dictionary<int, (string? CommandLine, string? ExecutablePath)> GetProcessesCommandLineCache()
        {
            var dict = new Dictionary<int, (string? CommandLine, string? ExecutablePath)>();
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return dict;
            }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine, ExecutablePath FROM Win32_Process");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    try
                    {
                        if (obj["ProcessId"] is uint or int)
                        {
                            var pid = Convert.ToInt32(obj["ProcessId"]);
                            var cmd = obj["CommandLine"]?.ToString();
                            var exe = obj["ExecutablePath"]?.ToString();
                            dict[pid] = (cmd, exe);
                        }
                    }
                    catch
                    {
                        // 忽略个别对象的字段读取异常
                    }
                }
            }
            catch
            {
                // 若 WMI 服务受限，不阻碍主流程
            }

            return dict;
        }

        /// <summary>
        /// 安全获取进程可执行程序绝对路径
        /// </summary>
        public static string? GetProcessExecutablePath(Process process, string? cachedPath = null)
        {
            if (!string.IsNullOrWhiteSpace(cachedPath))
            {
                return cachedPath;
            }

            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                // 当权限不足无法通过 MainModule 访问时，使用 QueryFullProcessImageName 尝试
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
                    if (handle != IntPtr.Zero)
                    {
                        try
                        {
                            var buffer = new char[1024];
                            int size = buffer.Length;
                            if (QueryFullProcessImageName(handle, 0, buffer, ref size))
                            {
                                return new string(buffer, 0, size);
                            }
                        }
                        finally
                        {
                            CloseHandle(handle);
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 挂起（暂停）进程
        /// </summary>
        public static bool SuspendProcess(int processId, out string? errorMessage)
        {
            errorMessage = null;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                errorMessage = "挂起进程目前仅支持 Windows 平台。";
                return false;
            }

            var handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, processId);
            if (handle == IntPtr.Zero)
            {
                errorMessage = $"无法打开进程句柄 (PID: {processId})，错误码: {Marshal.GetLastWin32Error()}";
                return false;
            }

            try
            {
                var status = NtSuspendProcess(handle);
                if (status != 0)
                {
                    errorMessage = $"NtSuspendProcess 调用失败，NTSTATUS: 0x{status:X8}";
                    return false;
                }
                return true;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>
        /// 恢复执行被挂起的进程
        /// </summary>
        public static bool ResumeProcess(int processId, out string? errorMessage)
        {
            errorMessage = null;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                errorMessage = "恢复进程目前仅支持 Windows 平台。";
                return false;
            }

            var handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, processId);
            if (handle == IntPtr.Zero)
            {
                errorMessage = $"无法打开进程句柄 (PID: {processId})，错误码: {Marshal.GetLastWin32Error()}";
                return false;
            }

            try
            {
                var status = NtResumeProcess(handle);
                if (status != 0)
                {
                    errorMessage = $"NtResumeProcess 调用失败，NTSTATUS: 0x{status:X8}";
                    return false;
                }
                return true;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>
        /// 获取系统物理内存与负载情况
        /// </summary>
        public static (long TotalPhysical, long AvailablePhysical, double MemoryLoad) GetMemoryStatus()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    return ((long)memStatus.ullTotalPhys, (long)memStatus.ullAvailPhys, memStatus.dwMemoryLoad);
                }
            }

            // 跨平台备用近似值
            var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return (total, total / 2, 50.0);
        }
    }
}
