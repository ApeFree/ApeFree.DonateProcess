using System;
using System.Collections.Generic;

namespace ApeFree.DonateProcess.Contracts
{
    /// <summary>
    /// 进程信息数据传输对象
    /// </summary>
    public class ProcessInfoDto
    {
        /// <summary>
        /// 进程标识符 (PID)
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 进程名称（不包含扩展名）
        /// </summary>
        public string ProcessName { get; set; } = string.Empty;

        /// <summary>
        /// 主窗口标题
        /// </summary>
        public string MainWindowTitle { get; set; } = string.Empty;

        /// <summary>
        /// 主窗口句柄
        /// </summary>
        public long MainWindowHandle { get; set; }

        /// <summary>
        /// 进程是否正在响应
        /// </summary>
        public bool Responding { get; set; } = true;

        /// <summary>
        /// 物理工作集内存占用（字节）
        /// </summary>
        public long WorkingSet64 { get; set; }

        /// <summary>
        /// 峰值工作集内存（字节）
        /// </summary>
        public long PeakWorkingSet64 { get; set; }

        /// <summary>
        /// 虚拟内存大小（字节）
        /// </summary>
        public long VirtualMemorySize64 { get; set; }

        /// <summary>
        /// 分页内存大小（字节）
        /// </summary>
        public long PagedMemorySize64 { get; set; }

        /// <summary>
        /// 进程启动时间
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 进程消耗的 CPU 总处理器时间
        /// </summary>
        public TimeSpan? TotalProcessorTime { get; set; }

        /// <summary>
        /// 可执行文件绝对路径（如果当前权限允许获取）
        /// </summary>
        public string? FileName { get; set; }

        /// <summary>
        /// 完整命令行启动参数（如果当前权限允许获取）
        /// </summary>
        public string? CommandLine { get; set; }

        /// <summary>
        /// 优先级类别（Normal, High, Idle, RealTime, AboveNormal, BelowNormal）
        /// </summary>
        public string? PriorityClass { get; set; }

        /// <summary>
        /// 终端会话 ID
        /// </summary>
        public int SessionId { get; set; }

        /// <summary>
        /// 线程总数
        /// </summary>
        public int ThreadCount { get; set; }

        /// <summary>
        /// 打开的句柄总数
        /// </summary>
        public int HandleCount { get; set; }

        /// <summary>
        /// 是否已退出
        /// </summary>
        public bool HasExited { get; set; }

        /// <summary>
        /// 退出代码（若未退出或不适用则为 0）
        /// </summary>
        public int ExitCode { get; set; }
    }

    /// <summary>
    /// 进程列表查询过滤条件
    /// </summary>
    public class ProcessFilterDto
    {
        /// <summary>
        /// 进程名或标题过滤关键字（支持通配符或模糊包含）
        /// </summary>
        public string? NameFilter { get; set; }

        /// <summary>
        /// 精确匹配进程 ID（0 表示不限）
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// 是否仅返回包含桌面主窗口的进程
        /// </summary>
        public bool? OnlyWithWindow { get; set; }

        /// <summary>
        /// 限制返回前 N 项记录（0 表示不限）
        /// </summary>
        public int Top { get; set; }

        /// <summary>
        /// 排序依据（Memory, Cpu, Name, Id, StartTime）
        /// </summary>
        public string? SortBy { get; set; }

        /// <summary>
        /// 是否降序排序
        /// </summary>
        public bool Descending { get; set; } = true;
    }

    /// <summary>
    /// 启动进程配置选项
    /// </summary>
    public class ProcessStartOptionsDto
    {
        /// <summary>
        /// 可执行程序文件路径或系统命令名称（例如 "notepad.exe", "cmd.exe"）
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// 命令行参数
        /// </summary>
        public string? Arguments { get; set; }

        /// <summary>
        /// 进程工作目录
        /// </summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// 是否不创建新窗口
        /// </summary>
        public bool CreateNoWindow { get; set; }

        /// <summary>
        /// 是否使用操作系统 Shell 启动
        /// </summary>
        public bool UseShellExecute { get; set; }

        /// <summary>
        /// 启动谓词（例如 "runas" 申请管理员提权）
        /// </summary>
        public string? Verb { get; set; }

        /// <summary>
        /// 窗口样式（Normal, Hidden, Minimized, Maximized）
        /// </summary>
        public string WindowStyle { get; set; } = "Normal";

        /// <summary>
        /// 附加环境变量字典
        /// </summary>
        public Dictionary<string, string>? EnvironmentVariables { get; set; }

        /// <summary>
        /// 是否同步等待进程退出并捕获输出
        /// </summary>
        public bool WaitForExit { get; set; }

        /// <summary>
        /// 等待进程退出的超时时间（毫秒，默认 30000ms）
        /// </summary>
        public int WaitTimeoutMs { get; set; } = 30000;
    }

    /// <summary>
    /// 进程启动结果
    /// </summary>
    public class ProcessStartResultDto
    {
        /// <summary>
        /// 启动成功时分配的进程 ID
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// 是否启动成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 错误异常信息（若启动失败）
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 进程启动时间
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 若启用了 WaitForExit，记录进程退出码
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// 若启用了 WaitForExit 且重定向输出，记录的标准输出内容
        /// </summary>
        public string? StandardOutput { get; set; }

        /// <summary>
        /// 若启用了 WaitForExit 且重定向输出，记录的标准错误内容
        /// </summary>
        public string? StandardError { get; set; }
    }

    /// <summary>
    /// 关闭或终止进程配置选项
    /// </summary>
    public class ProcessKillOptionsDto
    {
        /// <summary>
        /// 目标进程 ID
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// 是否强制终止（若为 false，将优先发送正常关闭窗口请求）
        /// </summary>
        public bool Force { get; set; } = true;

        /// <summary>
        /// 是否级联终止整个子进程树
        /// </summary>
        public bool KillEntireProcessTree { get; set; } = true;

        /// <summary>
        /// 在强制杀死前尝试优雅关闭的等待超时时间（毫秒，默认 3000ms）
        /// </summary>
        public int GracefulTimeoutMs { get; set; } = 3000;
    }

    /// <summary>
    /// 进程通用操作结果
    /// </summary>
    public class ProcessOperationResultDto
    {
        /// <summary>
        /// 操作是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 错误异常信息
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 操作关联的进程 ID
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// 实际受影响的进程数量
        /// </summary>
        public int AffectedCount { get; set; }
    }

    /// <summary>
    /// 进程重启配置选项
    /// </summary>
    public class ProcessRestartOptionsDto
    {
        /// <summary>
        /// 目标原进程 ID
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// 覆盖启动参数（若为空则沿用原进程启动参数）
        /// </summary>
        public string? OverrideArguments { get; set; }

        /// <summary>
        /// 覆盖工作目录（若为空则沿用原进程工作目录或所在目录）
        /// </summary>
        public string? OverrideWorkingDirectory { get; set; }

        /// <summary>
        /// 优雅等待原进程退出的超时时间（毫秒，默认 5000ms）
        /// </summary>
        public int GracefulTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 是否杀死原进程的所有子进程树
        /// </summary>
        public bool KillEntireProcessTree { get; set; } = true;
    }

    /// <summary>
    /// 脚本执行配置选项（PowerShell / Shell）
    /// </summary>
    public class ScriptExecutionOptionsDto
    {
        /// <summary>
        /// 脚本正文内容或命令行
        /// </summary>
        public string ScriptText { get; set; } = string.Empty;

        /// <summary>
        /// 附加参数
        /// </summary>
        public string? Arguments { get; set; }

        /// <summary>
        /// 脚本执行的工作目录
        /// </summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// PowerShell 执行策略（默认 "Bypass"）
        /// </summary>
        public string ExecutionPolicy { get; set; } = "Bypass";

        /// <summary>
        /// 执行超时时间（毫秒，默认 60000ms）
        /// </summary>
        public int TimeoutMs { get; set; } = 60000;

        /// <summary>
        /// 自定义环境变量
        /// </summary>
        public Dictionary<string, string>? EnvironmentVariables { get; set; }
    }

    /// <summary>
    /// 脚本执行结果
    /// </summary>
    public class ScriptExecutionResultDto
    {
        /// <summary>
        /// 脚本退出码（0 通常表示正常成功）
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// 捕获的标准输出内容
        /// </summary>
        public string StandardOutput { get; set; } = string.Empty;

        /// <summary>
        /// 捕获的标准错误内容
        /// </summary>
        public string StandardError { get; set; } = string.Empty;

        /// <summary>
        /// 是否执行成功（ExitCode == 0 且无未捕获异常）
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 执行耗时（毫秒）
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// 错误或异常消息
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 交互式会话 Shell 类型
    /// </summary>
    public enum ShellType
    {
        /// <summary>
        /// Windows 命令提示符 (cmd.exe)
        /// </summary>
        Cmd = 1,

        /// <summary>
        /// Windows PowerShell (powershell.exe)
        /// </summary>
        PowerShell = 2,

        /// <summary>
        /// 自定义 Shell 终端
        /// </summary>
        Custom = 3
    }

    /// <summary>
    /// 交互式 Shell 会话信息传输对象
    /// </summary>
    public class InteractiveSessionDto
    {
        /// <summary>
        /// 唯一会话标识符
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 会话采用的 Shell 类型
        /// </summary>
        public ShellType ShellType { get; set; }

        /// <summary>
        /// 当前工作目录
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// 关联的后台 Shell 宿主进程 PID
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// 会话当前是否处于活动运行中
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 会话创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 最近一次交互操作时间
        /// </summary>
        public DateTime LastActivityAt { get; set; }
    }

    /// <summary>
    /// 输出流通道类型
    /// </summary>
    public enum StreamType
    {
        /// <summary>
        /// 标准输出 (stdout)
        /// </summary>
        StandardOutput = 1,

        /// <summary>
        /// 标准错误 (stderr)
        /// </summary>
        StandardError = 2,

        /// <summary>
        /// 系统控制提示信息
        /// </summary>
        SystemInfo = 3
    }

    /// <summary>
    /// 交互式 Shell 实时输出事件数据对象
    /// </summary>
    public class SessionOutputDto : EventArgs
    {
        /// <summary>
        /// 会话标识符
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 文本内容
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// 输出通道类别
        /// </summary>
        public StreamType StreamType { get; set; }

        /// <summary>
        /// 输出时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 交互式会话中单轮同步执行指令的返回结果
    /// </summary>
    public class SessionCommandResultDto
    {
        /// <summary>
        /// 会话标识符
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 本轮指令产生的标准输出
        /// </summary>
        public string Output { get; set; } = string.Empty;

        /// <summary>
        /// 本轮指令产生的标准错误
        /// </summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>
        /// 本轮指令返回的退出码（若支持探测，如 CMD %ERRORLEVEL%）
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// 是否顺利完成本轮执行
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 错误异常信息（若执行超时或异常）
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 本轮执行耗时（毫秒）
        /// </summary>
        public long DurationMs { get; set; }
    }

    /// <summary>
    /// 系统基础运行负荷与性能指标
    /// </summary>
    public class SystemPerformanceDto
    {
        /// <summary>
        /// CPU 总体占用百分比（0.0 ~ 100.0）
        /// </summary>
        public double CpuUsagePercentage { get; set; }

        /// <summary>
        /// 物理总内存容量（字节）
        /// </summary>
        public long TotalPhysicalMemoryBytes { get; set; }

        /// <summary>
        /// 可用空闲物理内存（字节）
        /// </summary>
        public long AvailablePhysicalMemoryBytes { get; set; }

        /// <summary>
        /// 物理内存占用百分比（0.0 ~ 100.0）
        /// </summary>
        public double MemoryUsagePercentage { get; set; }

        /// <summary>
        /// 当前系统总运行进程数
        /// </summary>
        public int ProcessCount { get; set; }

        /// <summary>
        /// 机器名称
        /// </summary>
        public string MachineName { get; set; } = string.Empty;

        /// <summary>
        /// 操作系统版本信息
        /// </summary>
        public string OsVersion { get; set; } = string.Empty;
    }

    /// <summary>
    /// 进程退出事件传输对象
    /// </summary>
    public class ProcessExitedEventDto : EventArgs
    {
        /// <summary>
        /// 退出的进程 ID
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// 进程名称
        /// </summary>
        public string ProcessName { get; set; } = string.Empty;

        /// <summary>
        /// 退出代码
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// 退出时间
        /// </summary>
        public DateTime ExitTime { get; set; } = DateTime.UtcNow;
    }
}
