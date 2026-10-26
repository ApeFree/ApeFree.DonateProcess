using ApeFree.ApeRpc.Attributes;
using System;
using System.Collections.Generic;

namespace ApeFree.DonateProcess.Contracts
{
    /// <summary>
    /// 远程进程操作与控制服务契约。
    /// 由 Slave 端实现并暴露，Master 端通过 ApeRpc 动态代理获取其实例并调用。
    /// </summary>
    public interface IProcessOperationsService
    {
        /// <summary>
        /// 交互式 Shell 会话实时流式输出事件（stdout / stderr / 系统提示）。
        /// 当 Slave 端后台 Shell 产生任何字符输出时，主动推送至 Master 端。
        /// </summary>
        [RpcEvent(IsForcedSubscription = true)]
        event EventHandler<SessionOutputDto> SessionOutputReceived;

        /// <summary>
        /// 进程退出通知事件
        /// </summary>
        [RpcEvent]
        event EventHandler<ProcessExitedEventDto> ProcessExited;

        /// <summary>
        /// 获取当前运行的进程列表（支持按名称、PID、窗口、性能等进行筛选与排序）
        /// </summary>
        /// <param name="filter">可选的过滤与排序选项</param>
        /// <returns>进程信息列表</returns>
        [RpcTimeout(30000)]
        List<ProcessInfoDto> GetProcesses(ProcessFilterDto? filter = null);

        /// <summary>
        /// 查询指定 PID 的进程详细信息
        /// </summary>
        /// <param name="processId">进程标识符</param>
        /// <returns>进程详细数据对象，若不存在则返回 null</returns>
        [RpcTimeout(10000)]
        ProcessInfoDto? GetProcessById(int processId);

        /// <summary>
        /// 启动一个新进程
        /// </summary>
        /// <param name="options">启动选项（程序路径、参数、工作目录、环境变量、窗口样式等）</param>
        /// <returns>进程启动结果（含 PID 或错误提示）</returns>
        [RpcTimeout(60000)]
        ProcessStartResultDto StartProcess(ProcessStartOptionsDto options);

        /// <summary>
        /// 关闭或终止指定进程
        /// </summary>
        /// <param name="options">终止配置选项（优雅关闭或强制 Kill、是否杀死子进程树）</param>
        /// <returns>操作执行结果</returns>
        [RpcTimeout(15000)]
        ProcessOperationResultDto KillProcess(ProcessKillOptionsDto options);

        /// <summary>
        /// 按进程名批量终止所有匹配的进程
        /// </summary>
        /// <param name="processName">进程名称（无需扩展名，如 "notepad"）</param>
        /// <param name="force">是否强制杀死</param>
        /// <param name="killEntireTree">是否级联终止子进程树</param>
        /// <returns>操作执行结果（含终止数量）</returns>
        [RpcTimeout(20000)]
        ProcessOperationResultDto KillProcessByName(string processName, bool force = true, bool killEntireTree = true);

        /// <summary>
        /// 重启指定进程（探测原进程路径与参数后优雅关闭/强制杀死并重新启动）
        /// </summary>
        /// <param name="options">重启配置选项</param>
        /// <returns>新进程启动结果（含新 PID）</returns>
        [RpcTimeout(30000)]
        ProcessStartResultDto RestartProcess(ProcessRestartOptionsDto options);

        /// <summary>
        /// 挂起（暂停）指定进程
        /// </summary>
        /// <param name="processId">目标进程 ID</param>
        /// <returns>操作结果</returns>
        [RpcTimeout(10000)]
        ProcessOperationResultDto SuspendProcess(int processId);

        /// <summary>
        /// 恢复执行挂起状态的进程
        /// </summary>
        /// <param name="processId">目标进程 ID</param>
        /// <returns>操作结果</returns>
        [RpcTimeout(10000)]
        ProcessOperationResultDto ResumeProcess(int processId);

        /// <summary>
        /// 执行一次性 PowerShell 脚本并返回完整的输出、错误与退出码
        /// </summary>
        /// <param name="options">PowerShell 脚本配置</param>
        /// <returns>脚本执行结果</returns>
        [RpcTimeout(180000)]
        ScriptExecutionResultDto ExecutePowerShellScript(ScriptExecutionOptionsDto options);

        /// <summary>
        /// 执行单次操作系统命令行指令（如 cmd /c 或指定可执行程序）并等待输出
        /// </summary>
        /// <param name="command">程序或命令</param>
        /// <param name="arguments">参数</param>
        /// <param name="workingDir">工作目录</param>
        /// <param name="timeoutMs">执行超时时间（毫秒）</param>
        /// <returns>命令行执行结果</returns>
        [RpcTimeout(60000)]
        ScriptExecutionResultDto ExecuteCommand(string command, string? arguments = null, string? workingDir = null, int timeoutMs = 30000);

        #region 连续多轮交互式会话接口 (CMD / PowerShell Continuous Dialogue)

        /// <summary>
        /// 创建并启动一个新的连续多轮对话交互式 Shell 会话（CMD 或 PowerShell）
        /// </summary>
        /// <param name="shellType">会话类型标识（1 为 Cmd, 2 为 PowerShell，见 <see cref="ShellType"/>）</param>
        /// <param name="workingDirectory">会话初始工作目录</param>
        /// <param name="environmentVariables">可选环境变量</param>
        /// <returns>创建的会话元数据</returns>
        [RpcTimeout(15000)]
        InteractiveSessionDto StartInteractiveSession(int shellType = (int)ShellType.Cmd, string? workingDirectory = null, Dictionary<string, string>? environmentVariables = null);

        /// <summary>
        /// 向指定的连续多轮会话发送单条命令行指令（自动追加换行）
        /// 产生的新增输出将通过 <see cref="SessionOutputReceived"/> 事件实时流式回传或通过读取缓冲区获取
        /// </summary>
        /// <param name="sessionId">会话标识符</param>
        /// <param name="command">要发送执行的命令文本</param>
        /// <returns>是否成功发送到输入流</returns>
        [RpcTimeout(10000)]
        bool SendSessionCommand(string sessionId, string command);

        /// <summary>
        /// 向连续会话标准输入写入原始文本（例如在出现 [Y/N] 交互提示时回复）
        /// </summary>
        /// <param name="sessionId">会话标识符</param>
        /// <param name="rawInput">待写入的原始输入字符串</param>
        /// <returns>是否成功写入输入流</returns>
        [RpcTimeout(10000)]
        bool SendSessionInput(string sessionId, string rawInput);

        /// <summary>
        /// 在连续会话中执行一条指令，并同步等待本轮指令执行完毕返回输出结果
        /// </summary>
        /// <param name="sessionId">会话标识符</param>
        /// <param name="command">要执行的命令</param>
        /// <param name="timeoutMs">本轮等待完成的超时时间（毫秒）</param>
        /// <returns>本轮指令产生的输出和状态</returns>
        [RpcTimeout(120000)]
        SessionCommandResultDto ExecuteSessionCommandAndWait(string sessionId, string command, int timeoutMs = 15000);

        /// <summary>
        /// 从会话缓冲区中拉取最新的输出记录
        /// </summary>
        /// <param name="sessionId">会话标识符</param>
        /// <param name="maxLines">最大获取行数</param>
        /// <returns>输出行数据列表</returns>
        [RpcTimeout(10000)]
        List<SessionOutputDto> ReadSessionOutput(string sessionId, int maxLines = 100);

        /// <summary>
        /// 关闭并销毁指定的连续会话，终止其关联的后台宿主进程
        /// </summary>
        /// <param name="sessionId">会话标识符</param>
        /// <returns>是否成功关闭销毁</returns>
        [RpcTimeout(10000)]
        bool CloseInteractiveSession(string sessionId);

        /// <summary>
        /// 查询当前所有正在运行中的交互式 Shell 会话列表
        /// </summary>
        /// <returns>会话元数据列表</returns>
        [RpcTimeout(10000)]
        List<InteractiveSessionDto> ListInteractiveSessions();

        #endregion

        /// <summary>
        /// 获取主机基础运行负荷与硬件性能指标（CPU占用率、总物理内存、空闲内存、总进程数等）
        /// </summary>
        /// <returns>系统运行指标数据</returns>
        [RpcTimeout(10000)]
        SystemPerformanceDto GetSystemPerformance();
    }

    /// <summary>
    /// 进程操作服务便捷扩展方法
    /// </summary>
    public static class ProcessOperationsServiceExtensions
    {
        /// <summary>
        /// 使用强类型枚举启动交互式会话
        /// </summary>
        public static InteractiveSessionDto StartInteractiveSession(this IProcessOperationsService service, ShellType shellType, string? workingDirectory = null, Dictionary<string, string>? environmentVariables = null)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            return service.StartInteractiveSession((int)shellType, workingDirectory, environmentVariables);
        }
    }
}
