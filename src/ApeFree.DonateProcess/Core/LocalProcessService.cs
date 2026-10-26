using ApeFree.DonateProcess.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ApeFree.DonateProcess.Core
{
    /// <summary>
    /// 本地进程管理与操作服务实现类。
    /// 既可作为本地单机使用的进程管理核心，也可作为 Slave 节点的服务实现通过 ApeRpc 暴露给远程 Master 节点。
    /// </summary>
    public class LocalProcessService : IProcessOperationsService, IDisposable
    {
        private readonly InteractiveSessionManager _sessionManager;
        private bool _isDisposed;

        /// <summary>
        /// RPC 服务名称标识
        /// </summary>
        public string ServiceName => nameof(IProcessOperationsService);

        /// <inheritdoc />
        public event EventHandler<SessionOutputDto>? SessionOutputReceived;

        /// <inheritdoc />
        public event EventHandler<ProcessExitedEventDto>? ProcessExited;

        /// <summary>
        /// 初始化本地进程服务实例与会话管理器
        /// </summary>
        public LocalProcessService()
        {
            _sessionManager = new InteractiveSessionManager();
            _sessionManager.SessionOutputReceived += (s, e) =>
            {
                SessionOutputReceived?.Invoke(this, e);
            };
        }

        #region 进程查询

        /// <inheritdoc />
        public List<ProcessInfoDto> GetProcesses(ProcessFilterDto? filter = null)
        {
            var processes = Process.GetProcesses();
            var cmdCache = ProcessHelper.GetProcessesCommandLineCache();
            var list = new List<ProcessInfoDto>(processes.Length);

            foreach (var p in processes)
            {
                try
                {
                    cmdCache.TryGetValue(p.Id, out var cachedInfo);
                    var info = CreateProcessInfoDto(p, cachedInfo.CommandLine, cachedInfo.ExecutablePath);

                    if (filter != null)
                    {
                        if (filter.ProcessId > 0 && info.Id != filter.ProcessId)
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(filter.NameFilter))
                        {
                            var keyword = filter.NameFilter.Trim();
                            var nameMatch = info.ProcessName.Contains(keyword, StringComparison.OrdinalIgnoreCase);
                            var titleMatch = !string.IsNullOrEmpty(info.MainWindowTitle) && info.MainWindowTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase);
                            if (!nameMatch && !titleMatch)
                            {
                                continue;
                            }
                        }

                        if (filter.OnlyWithWindow == true && info.MainWindowHandle == 0)
                        {
                            continue;
                        }
                    }

                    list.Add(info);
                }
                catch
                {
                    // 忽略进程在枚举期间已退出的情况
                }
                finally
                {
                    p.Dispose();
                }
            }

            // 排序处理
            if (filter != null && !string.IsNullOrWhiteSpace(filter.SortBy))
            {
                var sortBy = filter.SortBy.ToLowerInvariant();
                list = sortBy switch
                {
                    "memory" or "workingset" => filter.Descending ? list.OrderByDescending(x => x.WorkingSet64).ToList() : list.OrderBy(x => x.WorkingSet64).ToList(),
                    "cpu" => filter.Descending ? list.OrderByDescending(x => x.TotalProcessorTime ?? TimeSpan.Zero).ToList() : list.OrderBy(x => x.TotalProcessorTime ?? TimeSpan.Zero).ToList(),
                    "name" => filter.Descending ? list.OrderByDescending(x => x.ProcessName).ToList() : list.OrderBy(x => x.ProcessName).ToList(),
                    "id" or "pid" => filter.Descending ? list.OrderByDescending(x => x.Id).ToList() : list.OrderBy(x => x.Id).ToList(),
                    "starttime" => filter.Descending ? list.OrderByDescending(x => x.StartTime ?? DateTime.MinValue).ToList() : list.OrderBy(x => x.StartTime ?? DateTime.MinValue).ToList(),
                    _ => list
                };
            }

            if (filter != null && filter.Top > 0 && list.Count > filter.Top)
            {
                list = list.Take(filter.Top).ToList();
            }

            return list;
        }

        /// <inheritdoc />
        public ProcessInfoDto? GetProcessById(int processId)
        {
            try
            {
                using var p = Process.GetProcessById(processId);
                var cmdCache = ProcessHelper.GetProcessesCommandLineCache();
                cmdCache.TryGetValue(p.Id, out var cachedInfo);
                return CreateProcessInfoDto(p, cachedInfo.CommandLine, cachedInfo.ExecutablePath);
            }
            catch
            {
                return null;
            }
        }

        private static ProcessInfoDto CreateProcessInfoDto(Process p, string? cachedCommandLine, string? cachedPath)
        {
            var dto = new ProcessInfoDto
            {
                Id = p.Id,
                ProcessName = p.ProcessName
            };

            try { dto.MainWindowTitle = p.MainWindowTitle; } catch { }
            try { dto.MainWindowHandle = p.MainWindowHandle.ToInt64(); } catch { }
            try { dto.Responding = p.Responding; } catch { }
            try { dto.WorkingSet64 = p.WorkingSet64; } catch { }
            try { dto.PeakWorkingSet64 = p.PeakWorkingSet64; } catch { }
            try { dto.VirtualMemorySize64 = p.VirtualMemorySize64; } catch { }
            try { dto.PagedMemorySize64 = p.PagedMemorySize64; } catch { }
            try { dto.StartTime = p.StartTime; } catch { }
            try { dto.TotalProcessorTime = p.TotalProcessorTime; } catch { }
            try { dto.PriorityClass = p.PriorityClass.ToString(); } catch { }
            try { dto.SessionId = p.SessionId; } catch { }
            try { dto.ThreadCount = p.Threads.Count; } catch { }
            try { dto.HandleCount = p.HandleCount; } catch { }
            try { dto.HasExited = p.HasExited; if (dto.HasExited) dto.ExitCode = p.ExitCode; } catch { }

            dto.FileName = ProcessHelper.GetProcessExecutablePath(p, cachedPath);
            dto.CommandLine = cachedCommandLine;

            return dto;
        }

        #endregion

        #region 进程启动与管理

        /// <inheritdoc />
        public ProcessStartResultDto StartProcess(ProcessStartOptionsDto options)
        {
            if (options == null || string.IsNullOrWhiteSpace(options.FileName))
            {
                return new ProcessStartResultDto
                {
                    Success = false,
                    ErrorMessage = "必须提供有效的文件路径或命令名称 FileName。"
                };
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = options.FileName,
                    Arguments = options.Arguments ?? string.Empty,
                    WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory) ? string.Empty : options.WorkingDirectory,
                    CreateNoWindow = options.CreateNoWindow,
                    UseShellExecute = options.UseShellExecute
                };

                if (!string.IsNullOrWhiteSpace(options.Verb))
                {
                    startInfo.Verb = options.Verb;
                }

                if (Enum.TryParse<ProcessWindowStyle>(options.WindowStyle, true, out var windowStyle))
                {
                    startInfo.WindowStyle = windowStyle;
                }

                if (options.EnvironmentVariables != null && !options.UseShellExecute)
                {
                    foreach (var kvp in options.EnvironmentVariables)
                    {
                        startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                    }
                }

                if (options.WaitForExit && !options.UseShellExecute)
                {
                    startInfo.RedirectStandardOutput = true;
                    startInfo.RedirectStandardError = true;
                    startInfo.StandardOutputEncoding = new UTF8Encoding(false);
                    startInfo.StandardErrorEncoding = new UTF8Encoding(false);

                    using var p = new Process { StartInfo = startInfo };
                    p.Start();

                    var outputTask = p.StandardOutput.ReadToEndAsync();
                    var errorTask = p.StandardError.ReadToEndAsync();

                    var exited = p.WaitForExit(options.WaitTimeoutMs > 0 ? options.WaitTimeoutMs : 30000);
                    if (!exited)
                    {
                        try { p.Kill(true); } catch { }
                        return new ProcessStartResultDto
                        {
                            ProcessId = p.Id,
                            Success = false,
                            ErrorMessage = $"进程执行超时 (超过 {options.WaitTimeoutMs}ms)。"
                        };
                    }

                    Task.WaitAll(outputTask, errorTask);

                    return new ProcessStartResultDto
                    {
                        ProcessId = p.Id,
                        Success = p.ExitCode == 0,
                        StartTime = DateTime.UtcNow,
                        ExitCode = p.ExitCode,
                        StandardOutput = outputTask.Result,
                        StandardError = errorTask.Result
                    };
                }
                else
                {
                    var p = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                    p.Exited += (s, e) =>
                    {
                        int exitCode = 0;
                        try { exitCode = p.ExitCode; } catch { }
                        ProcessExited?.Invoke(this, new ProcessExitedEventDto
                        {
                            ProcessId = p.Id,
                            ProcessName = options.FileName,
                            ExitCode = exitCode,
                            ExitTime = DateTime.UtcNow
                        });
                        p.Dispose();
                    };

                    var started = p.Start();
                    if (!started)
                    {
                        p.Dispose();
                        return new ProcessStartResultDto
                        {
                            Success = false,
                            ErrorMessage = "操作系统未能启动目标进程。"
                        };
                    }

                    DateTime? startTime = null;
                    try { startTime = p.StartTime; } catch { }

                    return new ProcessStartResultDto
                    {
                        ProcessId = p.Id,
                        Success = true,
                        StartTime = startTime ?? DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex)
            {
                return new ProcessStartResultDto
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <inheritdoc />
        public ProcessOperationResultDto KillProcess(ProcessKillOptionsDto options)
        {
            if (options == null)
            {
                return new ProcessOperationResultDto { Success = false, ErrorMessage = "参数 options 不能为空。" };
            }

            try
            {
                using var p = Process.GetProcessById(options.ProcessId);
                if (p.HasExited)
                {
                    return new ProcessOperationResultDto { Success = true, ProcessId = options.ProcessId, AffectedCount = 1 };
                }

                // 若非强制终止，优先尝试优雅关闭窗口
                if (!options.Force)
                {
                    if (p.CloseMainWindow())
                    {
                        if (p.WaitForExit(options.GracefulTimeoutMs > 0 ? options.GracefulTimeoutMs : 3000))
                        {
                            return new ProcessOperationResultDto { Success = true, ProcessId = options.ProcessId, AffectedCount = 1 };
                        }
                    }
                }

                // 强制终止进程（或级联子进程树）
                p.Kill(options.KillEntireProcessTree);
                p.WaitForExit(3000);

                return new ProcessOperationResultDto { Success = true, ProcessId = options.ProcessId, AffectedCount = 1 };
            }
            catch (ArgumentException)
            {
                // 目标进程本身已不存在
                return new ProcessOperationResultDto { Success = true, ProcessId = options.ProcessId, AffectedCount = 0 };
            }
            catch (Exception ex)
            {
                return new ProcessOperationResultDto
                {
                    Success = false,
                    ProcessId = options.ProcessId,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <inheritdoc />
        public ProcessOperationResultDto KillProcessByName(string processName, bool force = true, bool killEntireTree = true)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return new ProcessOperationResultDto { Success = false, ErrorMessage = "进程名不能为空。" };
            }

            var cleanName = Path.GetFileNameWithoutExtension(processName.Trim());
            var processes = Process.GetProcessesByName(cleanName);
            int count = 0;

            foreach (var p in processes)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        if (!force && p.CloseMainWindow() && p.WaitForExit(2000))
                        {
                            count++;
                        }
                        else
                        {
                            p.Kill(killEntireTree);
                            p.WaitForExit(2000);
                            count++;
                        }
                    }
                }
                catch
                {
                    // 忽略个别终止失败
                }
                finally
                {
                    p.Dispose();
                }
            }

            return new ProcessOperationResultDto
            {
                Success = true,
                AffectedCount = count
            };
        }

        /// <inheritdoc />
        public ProcessStartResultDto RestartProcess(ProcessRestartOptionsDto options)
        {
            if (options == null)
            {
                return new ProcessStartResultDto { Success = false, ErrorMessage = "参数 options 不能为空。" };
            }

            string? exePath = null;
            string? arguments = options.OverrideArguments;
            string? workingDir = options.OverrideWorkingDirectory;

            try
            {
                using var oldProcess = Process.GetProcessById(options.ProcessId);
                var cmdCache = ProcessHelper.GetProcessesCommandLineCache();
                cmdCache.TryGetValue(oldProcess.Id, out var cachedInfo);

                exePath = ProcessHelper.GetProcessExecutablePath(oldProcess, cachedInfo.ExecutablePath);

                if (string.IsNullOrWhiteSpace(arguments) && !string.IsNullOrWhiteSpace(cachedInfo.CommandLine))
                {
                    // 若原命令行中包含可执行程序路径，剔除该路径后提取纯参数
                    var fullCmd = cachedInfo.CommandLine.Trim();
                    if (!string.IsNullOrWhiteSpace(exePath) && fullCmd.StartsWith("\"" + exePath + "\"", StringComparison.OrdinalIgnoreCase))
                    {
                        arguments = fullCmd.Substring(exePath.Length + 2).Trim();
                    }
                    else if (!string.IsNullOrWhiteSpace(exePath) && fullCmd.StartsWith(exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        arguments = fullCmd.Substring(exePath.Length).Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(workingDir) && !string.IsNullOrWhiteSpace(exePath))
                {
                    workingDir = Path.GetDirectoryName(exePath);
                }

                // 尝试优雅关闭原进程
                if (!oldProcess.CloseMainWindow() || !oldProcess.WaitForExit(options.GracefulTimeoutMs > 0 ? options.GracefulTimeoutMs : 5000))
                {
                    oldProcess.Kill(options.KillEntireProcessTree);
                    oldProcess.WaitForExit(3000);
                }
            }
            catch (ArgumentException)
            {
                // 原进程已提前退出，若提供了完整路径或之前已缓存，可继续尝试启动
            }
            catch (Exception ex)
            {
                return new ProcessStartResultDto
                {
                    Success = false,
                    ErrorMessage = $"关闭原进程失败: {ex.Message}"
                };
            }

            if (string.IsNullOrWhiteSpace(exePath))
            {
                return new ProcessStartResultDto
                {
                    Success = false,
                    ErrorMessage = "未能提取到原进程可执行程序绝对路径，无法自动重启。"
                };
            }

            // 重新启动新进程
            return StartProcess(new ProcessStartOptionsDto
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = true
            });
        }

        /// <inheritdoc />
        public ProcessOperationResultDto SuspendProcess(int processId)
        {
            var success = ProcessHelper.SuspendProcess(processId, out var error);
            return new ProcessOperationResultDto
            {
                ProcessId = processId,
                Success = success,
                ErrorMessage = error,
                AffectedCount = success ? 1 : 0
            };
        }

        /// <inheritdoc />
        public ProcessOperationResultDto ResumeProcess(int processId)
        {
            var success = ProcessHelper.ResumeProcess(processId, out var error);
            return new ProcessOperationResultDto
            {
                ProcessId = processId,
                Success = success,
                ErrorMessage = error,
                AffectedCount = success ? 1 : 0
            };
        }

        #endregion

        #region 脚本与命令行执行

        /// <inheritdoc />
        public ScriptExecutionResultDto ExecutePowerShellScript(ScriptExecutionOptionsDto options)
        {
            if (options == null || string.IsNullOrWhiteSpace(options.ScriptText))
            {
                return new ScriptExecutionResultDto
                {
                    Success = false,
                    ErrorMessage = "PowerShell 脚本正文不能为空。"
                };
            }

            var sw = Stopwatch.StartNew();
            try
            {
                // 使用 UTF-16LE 将脚本编码为 Base64，利用 -EncodedCommand 执行，彻底免疫多行、引号与特殊字符的转义污染
                var scriptBytes = Encoding.Unicode.GetBytes(options.ScriptText);
                var encodedScript = Convert.ToBase64String(scriptBytes);

                var policy = string.IsNullOrWhiteSpace(options.ExecutionPolicy) ? "Bypass" : options.ExecutionPolicy;
                var arguments = $"-NoProfile -NonInteractive -ExecutionPolicy {policy} -EncodedCommand {encodedScript}";
                if (!string.IsNullOrWhiteSpace(options.Arguments))
                {
                    arguments += " " + options.Arguments;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = arguments,
                    WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory) ? Environment.CurrentDirectory : options.WorkingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false)
                };

                if (options.EnvironmentVariables != null)
                {
                    foreach (var kvp in options.EnvironmentVariables)
                    {
                        startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                    }
                }

                using var p = new Process { StartInfo = startInfo };
                p.Start();

                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();

                var timeout = options.TimeoutMs > 0 ? options.TimeoutMs : 60000;
                var exited = p.WaitForExit(timeout);
                if (!exited)
                {
                    try { p.Kill(true); } catch { }
                    return new ScriptExecutionResultDto
                    {
                        Success = false,
                        ExitCode = -1,
                        ErrorMessage = $"PowerShell 脚本执行超时 (超过 {timeout}ms)。",
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }

                Task.WaitAll(outTask, errTask);
                sw.Stop();

                return new ScriptExecutionResultDto
                {
                    ExitCode = p.ExitCode,
                    StandardOutput = outTask.Result,
                    StandardError = errTask.Result,
                    Success = p.ExitCode == 0,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new ScriptExecutionResultDto
                {
                    ExitCode = -1,
                    Success = false,
                    ErrorMessage = ex.Message,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
        }

        /// <inheritdoc />
        public ScriptExecutionResultDto ExecuteCommand(string command, string? arguments = null, string? workingDir = null, int timeoutMs = 30000)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return new ScriptExecutionResultDto { Success = false, ErrorMessage = "命令不能为空。" };
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var cmdLine = command.Trim();
                string fileName;
                string procArgs;

                if (cmdLine.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || cmdLine.Contains("\\") || cmdLine.Contains("/"))
                {
                    fileName = cmdLine;
                    procArgs = arguments ?? string.Empty;
                }
                else
                {
                    fileName = "cmd.exe";
                    var fullCmd = string.IsNullOrWhiteSpace(arguments) ? cmdLine : $"{cmdLine} {arguments}";
                    procArgs = $"/c {fullCmd}";
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = procArgs,
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDir) ? Environment.CurrentDirectory : workingDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false)
                };

                using var p = new Process { StartInfo = startInfo };
                p.Start();

                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();

                var timeout = timeoutMs > 0 ? timeoutMs : 30000;
                var exited = p.WaitForExit(timeout);
                if (!exited)
                {
                    try { p.Kill(true); } catch { }
                    return new ScriptExecutionResultDto
                    {
                        Success = false,
                        ExitCode = -1,
                        ErrorMessage = $"命令执行超时 (超过 {timeout}ms)。",
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }

                Task.WaitAll(outTask, errTask);
                sw.Stop();

                return new ScriptExecutionResultDto
                {
                    ExitCode = p.ExitCode,
                    StandardOutput = outTask.Result,
                    StandardError = errTask.Result,
                    Success = p.ExitCode == 0,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new ScriptExecutionResultDto
                {
                    ExitCode = -1,
                    Success = false,
                    ErrorMessage = ex.Message,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
        }

        #endregion

        #region 连续多轮交互式会话接口

        /// <inheritdoc />
        public InteractiveSessionDto StartInteractiveSession(int shellType = (int)ShellType.Cmd, string? workingDirectory = null, Dictionary<string, string>? environmentVariables = null)
        {
            var type = Enum.IsDefined(typeof(ShellType), (ShellType)shellType) ? (ShellType)shellType : ShellType.Cmd;
            var session = _sessionManager.CreateSession(type, workingDirectory, environmentVariables);
            return session.ToDto();
        }

        /// <inheritdoc />
        public bool SendSessionCommand(string sessionId, string command)
        {
            var session = _sessionManager.GetSession(sessionId);
            if (session == null) return false;
            return session.SendCommand(command);
        }

        /// <inheritdoc />
        public bool SendSessionInput(string sessionId, string rawInput)
        {
            var session = _sessionManager.GetSession(sessionId);
            if (session == null) return false;
            return session.SendInput(rawInput);
        }

        /// <inheritdoc />
        public SessionCommandResultDto ExecuteSessionCommandAndWait(string sessionId, string command, int timeoutMs = 15000)
        {
            var session = _sessionManager.GetSession(sessionId);
            if (session == null)
            {
                return new SessionCommandResultDto
                {
                    SessionId = sessionId,
                    Success = false,
                    ErrorMessage = $"未找到标识为 {sessionId} 的交互式会话。"
                };
            }

            return session.ExecuteCommandAndWaitAsync(command, timeoutMs).GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public List<SessionOutputDto> ReadSessionOutput(string sessionId, int maxLines = 100)
        {
            var session = _sessionManager.GetSession(sessionId);
            if (session == null) return new List<SessionOutputDto>();
            return session.ReadBufferedOutput(maxLines);
        }

        /// <inheritdoc />
        public bool CloseInteractiveSession(string sessionId)
        {
            return _sessionManager.CloseSession(sessionId);
        }

        /// <inheritdoc />
        public List<InteractiveSessionDto> ListInteractiveSessions()
        {
            return _sessionManager.ListSessions();
        }

        #endregion

        #region 系统性能指标

        /// <inheritdoc />
        public SystemPerformanceDto GetSystemPerformance()
        {
            var mem = ProcessHelper.GetMemoryStatus();
            var processes = Process.GetProcesses();
            var procCount = processes.Length;
            foreach (var p in processes) p.Dispose();

            return new SystemPerformanceDto
            {
                CpuUsagePercentage = 0.0, // 由调用方按需采样
                TotalPhysicalMemoryBytes = mem.TotalPhysical,
                AvailablePhysicalMemoryBytes = mem.AvailablePhysical,
                MemoryUsagePercentage = mem.MemoryLoad,
                ProcessCount = procCount,
                MachineName = Environment.MachineName,
                OsVersion = RuntimeInformation.OSDescription
            };
        }

        #endregion

        /// <summary>
        /// 释放本地进程服务并清理全部关联会话
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _sessionManager.Dispose();
        }
    }
}
