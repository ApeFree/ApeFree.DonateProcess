using ApeFree.DonateProcess.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ApeFree.DonateProcess.Core
{
    /// <summary>
    /// 连续多轮对话交互式 Shell 会话实例
    /// </summary>
    public class InteractiveSession : IDisposable
    {
        private readonly object _syncRoot = new();
        private readonly Process _process;
        private readonly StreamWriter _stdin;
        private readonly ConcurrentQueue<SessionOutputDto> _outputBuffer = new();
        private readonly int _maxBufferLines = 2000;
        private bool _isDisposed;

        // 当前单轮同步执行的等待上下文
        private TaskCompletionSource<SessionCommandResultDto>? _activeTurnTcs;
        private string? _activeTurnMarker;
        private StringBuilder? _activeTurnOutput;
        private StringBuilder? _activeTurnError;
        private Stopwatch? _activeTurnStopwatch;

        /// <summary>
        /// 会话唯一标识符
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// 会话 Shell 类型
        /// </summary>
        public ShellType ShellType { get; }

        /// <summary>
        /// 初始工作目录
        /// </summary>
        public string WorkingDirectory { get; }

        /// <summary>
        /// 关联的后台宿主进程 PID
        /// </summary>
        public int ProcessId => _process.Id;

        /// <summary>
        /// 会话创建时间
        /// </summary>
        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        /// <summary>
        /// 最近一次交互操作时间
        /// </summary>
        public DateTime LastActivityAt { get; private set; } = DateTime.UtcNow;

        /// <summary>
        /// 当前会话是否仍然存活
        /// </summary>
        public bool IsActive => !_isDisposed && !_process.HasExited;

        /// <summary>
        /// 实时输出事件回调
        /// </summary>
        public event EventHandler<SessionOutputDto>? OutputReceived;

        /// <summary>
        /// 会话进程退出事件
        /// </summary>
        public event EventHandler<EventArgs>? Exited;

        /// <summary>
        /// 创建并启动交互式会话
        /// </summary>
        public InteractiveSession(string sessionId, ShellType shellType, string? workingDirectory, Dictionary<string, string>? environmentVariables)
        {
            SessionId = sessionId;
            ShellType = shellType;
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;

            string fileName;
            string arguments;

            switch (shellType)
            {
                case ShellType.PowerShell:
                    fileName = "powershell.exe";
                    arguments = "-NoLogo -NoExit -ExecutionPolicy Bypass -Command -";
                    break;
                case ShellType.Cmd:
                default:
                    fileName = "cmd.exe";
                    arguments = "/Q /K";
                    break;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };

            if (environmentVariables != null)
            {
                foreach (var kvp in environmentVariables)
                {
                    startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
            }

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.Exited += (s, e) =>
            {
                RecordOutput($"[会话已终止，退出码: {_process.ExitCode}]", StreamType.SystemInfo);
                Exited?.Invoke(this, EventArgs.Empty);
            };

            _process.Start();
            _stdin = _process.StandardInput;
            _stdin.AutoFlush = true;

            // 启动异步输出与错误流捕获
            StartReadingStream(_process.StandardOutput, StreamType.StandardOutput);
            StartReadingStream(_process.StandardError, StreamType.StandardError);

            // 若为 CMD 会话，初始化设置 UTF-8 代码页以消除中文乱码
            if (shellType == ShellType.Cmd)
            {
                _stdin.WriteLine("chcp 65001 >nul");
            }
        }

        private void StartReadingStream(StreamReader reader, StreamType streamType)
        {
            Task.Run(async () =>
            {
                try
                {
                    while (!_isDisposed)
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) break;
                        HandleStreamLine(line, streamType);
                    }
                }
                catch
                {
                    // 流关闭或进程退出正常抛出
                }
            });
        }

        private void HandleStreamLine(string line, StreamType streamType)
        {
            LastActivityAt = DateTime.UtcNow;

            lock (_syncRoot)
            {
                // 检测是否匹配当前等待的指令完成标记
                if (_activeTurnTcs != null && !string.IsNullOrEmpty(_activeTurnMarker))
                {
                    if (line.Contains(_activeTurnMarker))
                    {
                        // 解析退出代码
                        int? exitCode = null;
                        var match = Regex.Match(line, Regex.Escape(_activeTurnMarker) + @"\s*(-?\d+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var code))
                        {
                            exitCode = code;
                        }

                        var result = new SessionCommandResultDto
                        {
                            SessionId = SessionId,
                            Output = _activeTurnOutput?.ToString() ?? string.Empty,
                            Error = _activeTurnError?.ToString() ?? string.Empty,
                            ExitCode = exitCode ?? 0,
                            Success = exitCode == null || exitCode == 0,
                            DurationMs = _activeTurnStopwatch?.ElapsedMilliseconds ?? 0
                        };

                        var tcs = _activeTurnTcs;
                        _activeTurnTcs = null;
                        _activeTurnMarker = null;
                        _activeTurnOutput = null;
                        _activeTurnError = null;
                        _activeTurnStopwatch = null;

                        tcs.TrySetResult(result);
                        return;
                    }

                    if (streamType == StreamType.StandardOutput)
                    {
                        _activeTurnOutput?.AppendLine(line);
                    }
                    else if (streamType == StreamType.StandardError)
                    {
                        _activeTurnError?.AppendLine(line);
                    }
                }
            }

            RecordOutput(line, streamType);
        }

        private void RecordOutput(string text, StreamType streamType)
        {
            var dto = new SessionOutputDto
            {
                SessionId = SessionId,
                Text = text,
                StreamType = streamType,
                Timestamp = DateTime.UtcNow
            };

            _outputBuffer.Enqueue(dto);
            while (_outputBuffer.Count > _maxBufferLines)
            {
                _outputBuffer.TryDequeue(out _);
            }

            OutputReceived?.Invoke(this, dto);
        }

        /// <summary>
        /// 发送单条命令（追加回车换行）
        /// </summary>
        public bool SendCommand(string command)
        {
            if (!IsActive) return false;
            try
            {
                lock (_syncRoot)
                {
                    LastActivityAt = DateTime.UtcNow;
                    _stdin.WriteLine(command);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 发送原始文本输入（不额外追加换行，适用于响应交互提示字符）
        /// </summary>
        public bool SendInput(string rawInput)
        {
            if (!IsActive) return false;
            try
            {
                lock (_syncRoot)
                {
                    LastActivityAt = DateTime.UtcNow;
                    _stdin.Write(rawInput);
                    _stdin.Flush();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 在当前连续会话中执行一条指令，并同步等待本轮完成
        /// </summary>
        public async Task<SessionCommandResultDto> ExecuteCommandAndWaitAsync(string command, int timeoutMs = 15000)
        {
            if (!IsActive)
            {
                return new SessionCommandResultDto
                {
                    SessionId = SessionId,
                    Success = false,
                    ErrorMessage = "会话未启动或已终止。"
                };
            }

            var marker = $"__APE_DONE_{Guid.NewGuid():N}__";
            var tcs = new TaskCompletionSource<SessionCommandResultDto>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_syncRoot)
            {
                _activeTurnTcs = tcs;
                _activeTurnMarker = marker;
                _activeTurnOutput = new StringBuilder();
                _activeTurnError = new StringBuilder();
                _activeTurnStopwatch = Stopwatch.StartNew();

                LastActivityAt = DateTime.UtcNow;
                _stdin.WriteLine(command);

                // 发送探测标记命令
                if (ShellType == ShellType.PowerShell)
                {
                    _stdin.WriteLine($"Write-Output \"{marker} $LASTEXITCODE\"");
                }
                else
                {
                    _stdin.WriteLine($"echo {marker} %errorlevel%");
                }
            }

            using var cts = new CancellationTokenSource(timeoutMs);
            cts.Token.Register(() =>
            {
                lock (_syncRoot)
                {
                    if (_activeTurnTcs == tcs)
                    {
                        var partialOutput = _activeTurnOutput?.ToString() ?? string.Empty;
                        var partialError = _activeTurnError?.ToString() ?? string.Empty;
                        _activeTurnTcs = null;
                        _activeTurnMarker = null;

                        tcs.TrySetResult(new SessionCommandResultDto
                        {
                            SessionId = SessionId,
                            Output = partialOutput,
                            Error = partialError,
                            Success = false,
                            ErrorMessage = $"指令执行超时 (超过 {timeoutMs}ms)。",
                            DurationMs = timeoutMs
                        });
                    }
                }
            });

            return await tcs.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// 从缓冲区拉取最新输出
        /// </summary>
        public List<SessionOutputDto> ReadBufferedOutput(int maxLines = 100)
        {
            LastActivityAt = DateTime.UtcNow;
            var list = _outputBuffer.ToList();
            if (list.Count > maxLines)
            {
                list = list.Skip(list.Count - maxLines).ToList();
            }
            return list;
        }

        /// <summary>
        /// 转换为 DTO
        /// </summary>
        public InteractiveSessionDto ToDto()
        {
            return new InteractiveSessionDto
            {
                SessionId = SessionId,
                ShellType = ShellType,
                WorkingDirectory = WorkingDirectory,
                ProcessId = ProcessId,
                IsActive = IsActive,
                CreatedAt = CreatedAt,
                LastActivityAt = LastActivityAt
            };
        }

        /// <summary>
        /// 关闭会话并释放资源
        /// </summary>
        /// <summary>
        /// 释放会话管理器资源并终止所有活动会话
        /// </summary>
        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                try
                {
                    if (!_process.HasExited)
                    {
                        _stdin.WriteLine("exit");
                        if (!_process.WaitForExit(1000))
                        {
                            _process.Kill(true);
                        }
                    }
                }
                catch
                {
                    // 忽略进程杀死异常
                }
                finally
                {
                    _stdin.Dispose();
                    _process.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// 连续多轮交互式会话管理器
    /// </summary>
    public class InteractiveSessionManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, InteractiveSession> _sessions = new();
        private readonly Timer _cleanupTimer;
        private bool _isDisposed;

        /// <summary>
        /// 会话实时输出全局广播事件
        /// </summary>
        public event EventHandler<SessionOutputDto>? SessionOutputReceived;

        /// <summary>
        /// 初始化会话管理器实例并启动空闲回收计时器
        /// </summary>
        public InteractiveSessionManager()
        {
            // 每 2 分钟清理空闲超过 30 分钟的废弃会话
            _cleanupTimer = new Timer(CleanupIdleSessions, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
        }

        /// <summary>
        /// 创建并启动新会话
        /// </summary>
        public InteractiveSession CreateSession(ShellType shellType = ShellType.Cmd, string? workingDirectory = null, Dictionary<string, string>? environmentVariables = null)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(InteractiveSessionManager));

            var sessionId = Guid.NewGuid().ToString("N");
            var session = new InteractiveSession(sessionId, shellType, workingDirectory, environmentVariables);

            session.OutputReceived += (s, e) =>
            {
                SessionOutputReceived?.Invoke(this, e);
            };

            session.Exited += (s, e) =>
            {
                // 标记并在必要时移出会话列表
            };

            _sessions[sessionId] = session;
            return session;
        }

        /// <summary>
        /// 获取指定会话
        /// </summary>
        public InteractiveSession? GetSession(string sessionId)
        {
            _sessions.TryGetValue(sessionId, out var session);
            return session;
        }

        /// <summary>
        /// 关闭指定会话
        /// </summary>
        public bool CloseSession(string sessionId)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                session.Dispose();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 获取所有运行中的会话列表
        /// </summary>
        public List<InteractiveSessionDto> ListSessions()
        {
            return _sessions.Values.Select(s => s.ToDto()).ToList();
        }

        private void CleanupIdleSessions(object? state)
        {
            if (_isDisposed) return;
            var timeoutThreshold = DateTime.UtcNow.AddMinutes(-30);

            foreach (var kvp in _sessions)
            {
                if (!kvp.Value.IsActive || kvp.Value.LastActivityAt < timeoutThreshold)
                {
                    if (_sessions.TryRemove(kvp.Key, out var session))
                    {
                        session.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// 释放会话管理器资源并终止所有活动会话
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _cleanupTimer.Dispose();
            foreach (var session in _sessions.Values)
            {
                session.Dispose();
            }
            _sessions.Clear();
        }
    }
}
