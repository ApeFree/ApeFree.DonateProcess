using ApeFree.DonateProcess.Common;
using ApeFree.DonateProcess.Contracts;
using ApeFree.DonateProcess.Core;
using ApeFree.DonateProcess.Master;
using STTech.BytesIO.P2P;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ApeFree.DonateProcess.WinCli
{
    internal class Program
    {
        private enum RunMode
        {
            None,
            Master,
            Slave,
            P2PServer,
            Local
        }

        private static async Task<int> Main(string[] args)
        {
            var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0.0";
            Console.Title = $"ApeFree.DonateProcess.WinCli v{appVersion} - 远程进程管理工具";

            if (args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a.Equals("-?", StringComparison.OrdinalIgnoreCase) || (a.Equals("-h", StringComparison.OrdinalIgnoreCase) && !args.Any(x => x.Equals("-s", StringComparison.OrdinalIgnoreCase) || x.Equals("--slave", StringComparison.OrdinalIgnoreCase)))))
            {
                PrintOverallHelp();
                return 0;
            }

            var isP2P = args.Any(a => a.Equals("-p2p", StringComparison.OrdinalIgnoreCase) || a.Equals("--p2p", StringComparison.OrdinalIgnoreCase));
            var mode = DetectMode(args, out var filteredArgs);

            if (mode == RunMode.None)
            {
                PrintMainBanner(appVersion);
                mode = PromptInteractiveMode();
                if (mode == RunMode.None)
                {
                    return 1;
                }
            }

            return mode switch
            {
                RunMode.Master => await RunMasterAsync(filteredArgs, appVersion, isP2P),
                RunMode.Slave => await RunSlaveAsync(filteredArgs, appVersion, isP2P),
                RunMode.P2PServer => await RunP2PServerAsync(filteredArgs, appVersion),
                RunMode.Local => await RunLocalAsync(filteredArgs, appVersion),
                _ => 1
            };
        }

        #region 模式检测与交互选择

        private static RunMode DetectMode(string[] args, out string[] remainingArgs)
        {
            var argList = args.ToList();
            RunMode mode = RunMode.None;

            for (int i = 0; i < argList.Count; i++)
            {
                var arg = argList[i].ToLowerInvariant();
                if (arg == "-m" || arg == "--master")
                {
                    mode = RunMode.Master;
                    argList.RemoveAt(i);
                    break;
                }
                else if (arg == "-s" || arg == "--slave")
                {
                    mode = RunMode.Slave;
                    argList.RemoveAt(i);
                    break;
                }
                else if (arg == "--p2p-server" || arg == "-server" || arg == "--relay-server")
                {
                    mode = RunMode.P2PServer;
                    argList.RemoveAt(i);
                    break;
                }
                else if (arg == "-l" || arg == "--local")
                {
                    mode = RunMode.Local;
                    argList.RemoveAt(i);
                    break;
                }
            }

            remainingArgs = argList.ToArray();
            return mode;
        }

        private static RunMode PromptInteractiveMode()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================== 请选择运行模式 ===================");
            Console.ResetColor();
            Console.WriteLine("  1. Master 远程控制端 (通过 TCP / P2P 连接 Slave 并管理远程进程与执行会话)");
            Console.WriteLine("  2. Slave 节点端      (作为共享节点，将本机进程执行与多轮会话能力共享给 Master)");
            Console.WriteLine("  3. P2P 中继服务端    (作为公网节点提供 Libp2p 路由与双向数据流中继转发)");
            Console.WriteLine("  4. 本地单机控制台    (无需网络直接管理本机进程、连续多轮 CMD、PowerShell)");
            Console.WriteLine("======================================================");
            Console.Write("请输入选项 (1/2/3/4, 默认 1): ");
            var input = Console.ReadLine()?.Trim();

            return input switch
            {
                "2" => RunMode.Slave,
                "3" => RunMode.P2PServer,
                "4" => RunMode.Local,
                _ => RunMode.Master
            };
        }

        private static void PrintMainBanner(string version)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine($"  ApeFree.DonateProcess.WinCli v{version}");
            Console.WriteLine("  远程主动捐赠式进程管理与连续多轮命令行交互调度平台");
            Console.WriteLine("==================================================================");
            Console.ResetColor();
        }

        private static void PrintOverallHelp()
        {
            Console.WriteLine(@"用法:
  ApeFree.DonateProcess.WinCli.exe [模式参数] [选项...]

运行模式:
  -m, --master              以 Master 远程管理端模式运行 (默认)
  -s, --slave               以 Slave 共享端模式运行
  --p2p-server              以 P2P 引导与中继服务器模式运行
  -l, --local               以本地单机进程交互控制台模式运行

常用选项:
  -p2p, --p2p               启用 P2P (Libp2p) 去中心化中继穿透
  --server <地址>           指定 P2P 服务端地址 (例如 127.0.0.1:9832)
  -p, --port <端口>         指定网络通信端口
  -h, --host <主机>         指定连接目标主机或监听地址
  --help, -h                显示帮助说明

示例:
  # Master 端通过 P2P 中继连接 Slave
  ApeFree.DonateProcess.WinCli.exe -m -p2p --server 127.0.0.1:9832

  # Slave 端通过 P2P 中继分享本机进程能力
  ApeFree.DonateProcess.WinCli.exe -s -p2p --server 127.0.0.1:9832

  # 启动公网 P2P 中继服务端
  ApeFree.DonateProcess.WinCli.exe --p2p-server -p 9832

  # 启动本地单机进程与连续 CMD 交互控制台
  ApeFree.DonateProcess.WinCli.exe -l
");
        }

        #endregion

        #region Master 运行逻辑

        private static async Task<int> RunMasterAsync(string[] args, string appVersion, bool isP2P)
        {
            Console.Title = $"ApeFree.DonateProcess.WinCli v{appVersion} - [Master 远程控制端]";
            PrintMainBanner(appVersion);

            var options = new ProcessMasterOptions { UseP2P = isP2P };

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i].ToLowerInvariant();
                if ((arg == "--server" || arg == "-server") && i + 1 < args.Length)
                {
                    options.P2PServerAddress = args[++i];
                    options.UseP2P = true;
                }
                else if ((arg == "-p" || arg == "--port") && i + 1 < args.Length && int.TryParse(args[++i], out var p))
                {
                    options.ListenPort = p;
                }
                else if ((arg == "-h" || arg == "--host") && i + 1 < args.Length)
                {
                    options.ListenHost = args[++i];
                }
            }

            if (options.UseP2P && string.IsNullOrWhiteSpace(options.P2PServerAddress))
            {
                Console.Write("请输入 P2P 中继服务器地址 (默认 127.0.0.1:9832): ");
                var srv = Console.ReadLine()?.Trim();
                options.P2PServerAddress = string.IsNullOrWhiteSpace(srv) ? "127.0.0.1:9832" : srv;
            }

            using var masterNode = new ProcessMasterNode(options);

            masterNode.SessionOutputReceived += (s, e) =>
            {
                // 实时会话输出（非交互阻塞模式下打印）
            };

            masterNode.ProcessExited += (s, e) =>
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"\n[通知] 远端进程已退出: [PID: {e.ProcessId}] {e.ProcessName} (退出码: {e.ExitCode})");
                Console.ResetColor();
            };

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==> 正在启动 Master 控制端 ...");
            Console.ResetColor();

            if (options.UseP2P)
            {
                Console.WriteLine($"    模式: P2P 去中心化中继 (中继服务器: {options.P2PServerAddress})");
            }
            else
            {
                Console.WriteLine($"    模式: TCP 监听端口 {options.ListenPort}");
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            await masterNode.StartAsync(cts.Token);

            if (options.UseP2P && !string.IsNullOrWhiteSpace(masterNode.LocalPeerId))
            {
                Console.WriteLine($"    Master PeerId: {masterNode.LocalPeerId}");
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[*] 正在等待 Slave 节点接入 ...");
            Console.ResetColor();

            while (!masterNode.HasActiveSlave && !cts.IsCancellationRequested)
            {
                await Task.Delay(500);
            }

            if (cts.IsCancellationRequested) return 0;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[+] 远端 Slave 节点已就绪！进入进程管理与交互式控制台。\n");
            Console.ResetColor();

            await RunInteractiveDashboardAsync(masterNode.RemoteService!, isRemote: true, cts.Token);
            return 0;
        }

        #endregion

        #region Local 本地单机运行逻辑

        private static async Task<int> RunLocalAsync(string[] args, string appVersion)
        {
            Console.Title = $"ApeFree.DonateProcess.WinCli v{appVersion} - [本地单机控制台]";
            PrintMainBanner(appVersion);

            using var localService = new LocalProcessService();
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[+] 本地进程管理服务已初始化。\n");
            Console.ResetColor();

            await RunInteractiveDashboardAsync(localService, isRemote: false, cts.Token);
            return 0;
        }

        #endregion

        #region Slave 运行逻辑

        private static async Task<int> RunSlaveAsync(string[] args, string appVersion, bool isP2P)
        {
            return await ApeFree.DonateProcess.WinSlaveCli.Program.RunAsync(args, appVersion, isP2P);
        }

        #endregion

        #region P2P 服务端运行逻辑

        private static async Task<int> RunP2PServerAsync(string[] args, string appVersion)
        {
            Console.Title = $"ApeFree.DonateProcess.WinCli v{appVersion} - [P2P 服务端] 引导与数据中继节点";
            PrintMainBanner(appVersion);

            string host = "0.0.0.0";
            int port = 9832;
            string? seed = null;

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i].ToLowerInvariant();
                if ((arg == "-p" || arg == "--port") && i + 1 < args.Length && int.TryParse(args[++i], out var p))
                {
                    port = p;
                }
                else if ((arg == "-h" || arg == "--host" || arg == "-i" || arg == "--ip") && i + 1 < args.Length)
                {
                    host = args[++i];
                }
                else if (arg == "--seed" && i + 1 < args.Length)
                {
                    seed = args[++i];
                }
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================== P2P 服务端配置 ===================");
            Console.ResetColor();
            Console.WriteLine($"  程序版本:      v{appVersion}");
            Console.WriteLine($"  绑定监听端口:  {host}:{port}");
            Console.WriteLine($"  数据中继转发:  已启用 (Transparent Relay & Bridging)");
            Console.WriteLine("======================================================");

            using var server = new BootstrapServer();
            server.Options.ListenAddress = host;
            server.Options.TcpPort = port;
            server.Options.EnableRelay = false;
            server.Options.EnableDht = true;
            server.Options.EnableMdns = true;

            string effectiveSeed = !string.IsNullOrWhiteSpace(seed) ? seed : P2PAddressHelper.DefaultServerSeed;
            server.Options.IdentitySeed = P2PAddressHelper.ParseSeed(effectiveSeed);

            using var relayManager = new P2PRelayManager();

            server.PeerConnected += (s, e) =>
            {
                var c = e.Client;
                relayManager.RegisterClient(c);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[+] 对等节点接入: {c.RemoteAddress} (当前活动对等节点: {relayManager.ActivePeerCount})");
                Console.ResetColor();
            };

            server.PeerDisconnected += (s, e) =>
            {
                var c = e.Client;
                relayManager.UnregisterClient(c);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[-] 对等节点离开: {c.RemoteAddress} (当前活动对等节点: {relayManager.ActivePeerCount})");
                Console.ResetColor();
            };

            await server.StartAsync();

            var peerId = server.LocalPeerId;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[+] P2P 引导与中继服务端已就绪！");
            Console.WriteLine($"    服务端 PeerId: {peerId}");
            Console.WriteLine($"    Multiaddr:     /ip4/{host}/tcp/{port}/p2p/{peerId}");
            Console.ResetColor();
            Console.WriteLine("\n按 Ctrl+C 退出服务端。\n");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException) { }

            Console.WriteLine("服务端正在退出...");
            return 0;
        }

        #endregion

        #region 交互式控制台命令面板 (Dashboard)

        private static async Task RunInteractiveDashboardAsync(IProcessOperationsService service, bool isRemote, CancellationToken cancellationToken)
        {
            PrintDashboardHelp(isRemote);

            while (!cancellationToken.IsCancellationRequested)
            {
                Console.ForegroundColor = isRemote ? ConsoleColor.Magenta : ConsoleColor.Cyan;
                Console.Write(isRemote ? "[RemoteProcess]> " : "[LocalProcess]> ");
                Console.ResetColor();

                var line = Console.ReadLine()?.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var cmd = parts[0].ToLowerInvariant();
                var arg = parts.Length > 1 ? parts[1].Trim() : string.Empty;

                try
                {
                    switch (cmd)
                    {
                        case "list" or "ls" or "ps-list":
                            HandleListProcesses(service, arg);
                            break;

                        case "start":
                            HandleStartProcess(service, arg);
                            break;

                        case "kill":
                            HandleKillProcess(service, arg);
                            break;

                        case "kill-name":
                            HandleKillByName(service, arg);
                            break;

                        case "restart":
                            HandleRestartProcess(service, arg);
                            break;

                        case "suspend":
                            HandleSuspendProcess(service, arg);
                            break;

                        case "resume":
                            HandleResumeProcess(service, arg);
                            break;

                        case "ps":
                            HandleExecutePowerShell(service, arg);
                            break;

                        case "cmd":
                            await EnterInteractiveContinuousShellAsync(service, ShellType.Cmd, isRemote, cancellationToken);
                            break;

                        case "powershell" or "pwsh":
                            await EnterInteractiveContinuousShellAsync(service, ShellType.PowerShell, isRemote, cancellationToken);
                            break;

                        case "perf":
                            HandleShowPerf(service);
                            break;

                        case "help" or "?":
                            PrintDashboardHelp(isRemote);
                            break;

                        case "clear" or "cls":
                            Console.Clear();
                            break;

                        case "exit" or "quit":
                            return;

                        default:
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"未知命令: '{cmd}'，输入 'help' 查看帮助。");
                            Console.ResetColor();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[错误] 执行异常: {ex.Message}");
                    Console.ResetColor();
                }
            }
        }

        private static void PrintDashboardHelp(bool isRemote)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("======================= 可用操作命令 =======================");
            Console.ResetColor();
            Console.WriteLine("  list [关键字] [条数]    查看进程列表 (例如: list chrome 10)");
            Console.WriteLine("  start <程序> [参数]     启动进程 (例如: start notepad.exe)");
            Console.WriteLine("  kill <PID>              终止进程及子进程树 (例如: kill 1234)");
            Console.WriteLine("  kill-name <进程名>      按名称批量终止进程 (例如: kill-name notepad)");
            Console.WriteLine("  restart <PID>           重启进程 (例如: restart 1234)");
            Console.WriteLine("  suspend <PID>           挂起进程 (例如: suspend 1234)");
            Console.WriteLine("  resume <PID>            恢复进程 (例如: resume 1234)");
            Console.WriteLine("  ps <脚本命令>           执行一次性 PowerShell 脚本 (例如: ps Get-Date)");
            Console.WriteLine("  cmd                     🌟 进入连续多轮对话的交互式 CMD 终端！");
            Console.WriteLine("  powershell              🌟 进入连续多轮对话的交互式 PowerShell 终端！");
            Console.WriteLine("  perf                    查看系统性能指标 (CPU、内存、进程数)");
            Console.WriteLine("  clear                   清屏");
            Console.WriteLine("  exit                    退出控制台");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("============================================================");
            Console.ResetColor();
        }

        private static void HandleListProcesses(IProcessOperationsService service, string arg)
        {
            var filter = new ProcessFilterDto
            {
                SortBy = "Memory",
                Descending = true,
                Top = 30
            };

            if (!string.IsNullOrWhiteSpace(arg))
            {
                var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                filter.NameFilter = parts[0];
                if (parts.Length > 1 && int.TryParse(parts[1], out var top))
                {
                    filter.Top = top;
                }
            }

            var list = service.GetProcesses(filter);

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine(string.Format("{0,-8} {1,-24} {2,10} {3,12} {4,-10} {5}", "PID", "名称", "内存(MB)", "CPU耗时", "状态", "主窗口标题"));
            Console.WriteLine(new string('-', 85));
            Console.ResetColor();

            foreach (var p in list)
            {
                var memMB = (p.WorkingSet64 / 1024.0 / 1024.0).ToString("F1");
                var cpu = p.TotalProcessorTime.HasValue ? p.TotalProcessorTime.Value.ToString(@"hh\:mm\:ss") : "-";
                var status = p.Responding ? "正常" : "无响应";
                var title = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? "" : (p.MainWindowTitle.Length > 28 ? p.MainWindowTitle.Substring(0, 25) + "..." : p.MainWindowTitle);

                Console.WriteLine(string.Format("{0,-8} {1,-24} {2,10} {3,12} {4,-10} {5}", p.Id, p.ProcessName, memMB, cpu, status, title));
            }
            Console.WriteLine();
        }

        private static void HandleStartProcess(IProcessOperationsService service, string arg)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                Console.WriteLine("用法: start <程序文件名或绝对路径> [参数]");
                return;
            }

            var parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var file = parts[0];
            var procArgs = parts.Length > 1 ? parts[1] : null;

            var res = service.StartProcess(new ProcessStartOptionsDto
            {
                FileName = file,
                Arguments = procArgs,
                UseShellExecute = true
            });

            if (res.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[+] 成功启动进程: [PID: {res.ProcessId}] {file}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[-] 启动失败: {res.ErrorMessage}");
                Console.ResetColor();
            }
        }

        private static void HandleKillProcess(IProcessOperationsService service, string arg)
        {
            if (!int.TryParse(arg, out var pid))
            {
                Console.WriteLine("用法: kill <PID>");
                return;
            }

            var res = service.KillProcess(new ProcessKillOptionsDto
            {
                ProcessId = pid,
                Force = true,
                KillEntireProcessTree = true
            });

            if (res.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[+] 成功终止进程 PID: {pid}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[-] 终止失败: {res.ErrorMessage}");
                Console.ResetColor();
            }
        }

        private static void HandleKillByName(IProcessOperationsService service, string arg)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                Console.WriteLine("用法: kill-name <进程名>");
                return;
            }

            var res = service.KillProcessByName(arg, force: true, killEntireTree: true);
            if (res.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[+] 成功批量终止 {res.AffectedCount} 个名为 '{arg}' 的进程。");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[-] 操作失败: {res.ErrorMessage}");
                Console.ResetColor();
            }
        }

        private static void HandleRestartProcess(IProcessOperationsService service, string arg)
        {
            if (!int.TryParse(arg, out var pid))
            {
                Console.WriteLine("用法: restart <PID>");
                return;
            }

            Console.WriteLine($"[*] 正在分析原进程并重启 (PID: {pid}) ...");
            var res = service.RestartProcess(new ProcessRestartOptionsDto
            {
                ProcessId = pid,
                GracefulTimeoutMs = 3000,
                KillEntireProcessTree = true
            });

            if (res.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[+] 进程已成功重启，新分配 PID: {res.ProcessId}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[-] 重启失败: {res.ErrorMessage}");
                Console.ResetColor();
            }
        }

        private static void HandleSuspendProcess(IProcessOperationsService service, string arg)
        {
            if (!int.TryParse(arg, out var pid))
            {
                Console.WriteLine("用法: suspend <PID>");
                return;
            }

            var res = service.SuspendProcess(pid);
            if (res.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[+] 进程 PID: {pid} 已挂起 (暂停执行)。");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[-] 挂起失败: {res.ErrorMessage}");
                Console.ResetColor();
            }
        }

        private static void HandleResumeProcess(IProcessOperationsService service, string arg)
        {
            if (!int.TryParse(arg, out var pid))
            {
                Console.WriteLine("用法: resume <PID>");
                return;
            }

            var res = service.ResumeProcess(pid);
            if (res.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[+] 进程 PID: {pid} 已恢复执行。");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[-] 恢复失败: {res.ErrorMessage}");
                Console.ResetColor();
            }
        }

        private static void HandleExecutePowerShell(IProcessOperationsService service, string arg)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                Console.WriteLine("用法: ps <PowerShell 脚本或命令>");
                return;
            }

            Console.WriteLine($"[*] 正在执行 PowerShell 脚本 ...");
            var res = service.ExecutePowerShellScript(new ScriptExecutionOptionsDto
            {
                ScriptText = arg,
                ExecutionPolicy = "Bypass",
                TimeoutMs = 60000
            });

            if (!string.IsNullOrEmpty(res.StandardOutput))
            {
                Console.WriteLine(res.StandardOutput);
            }

            if (!string.IsNullOrEmpty(res.StandardError))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(res.StandardError);
                Console.ResetColor();
            }

            Console.ForegroundColor = res.Success ? ConsoleColor.DarkGray : ConsoleColor.Yellow;
            Console.WriteLine($"[ExitCode: {res.ExitCode}, 耗时: {res.DurationMs}ms]");
            Console.ResetColor();
        }

        private static void HandleShowPerf(IProcessOperationsService service)
        {
            var perf = service.GetSystemPerformance();
            var totalGB = (perf.TotalPhysicalMemoryBytes / 1024.0 / 1024.0 / 1024.0).ToString("F1");
            var freeGB = (perf.AvailablePhysicalMemoryBytes / 1024.0 / 1024.0 / 1024.0).ToString("F1");

            Console.WriteLine();
            Console.WriteLine($"  主机名称:    {perf.MachineName}");
            Console.WriteLine($"  系统平台:    {perf.OsVersion}");
            Console.WriteLine($"  总运行进程:  {perf.ProcessCount}");
            Console.WriteLine($"  物理内存:    总共 {totalGB} GB / 剩余 {freeGB} GB (使用率: {perf.MemoryUsagePercentage:F1}%)");
            Console.WriteLine();
        }

        #endregion

        #region 连续多轮对话的交互式 CMD / PowerShell 终端实现

        private static async Task EnterInteractiveContinuousShellAsync(IProcessOperationsService service, ShellType shellType, bool isRemote, CancellationToken cancellationToken)
        {
            var shellName = shellType == ShellType.PowerShell ? "PowerShell" : "CMD";
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"==> 正在创建连续多轮对话的交互式 {shellName} 会话 (保持目录与上下文变量) ...");
            Console.ResetColor();

            var session = service.StartInteractiveSession(shellType);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[+] 会话已建立 (SessionId: {session.SessionId}, PID: {session.ProcessId})");
            Console.WriteLine($"    输入指令直接执行，上下文变量及工作目录将在轮次间保留；");
            Console.WriteLine($"    输入 'exit' 或按 Ctrl+D 退出当前连续会话返回控制面板。\n");
            Console.ResetColor();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.Write($"[{shellName}]> ");
                    Console.ResetColor();

                    var input = Console.ReadLine();
                    if (input == null) break;

                    var trimmed = input.Trim();
                    if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    // 同步执行单轮指令并等待本轮执行完成标记
                    var turnResult = service.ExecuteSessionCommandAndWait(session.SessionId, input, 30000);

                    if (!string.IsNullOrEmpty(turnResult.Output))
                    {
                        Console.Write(turnResult.Output);
                        if (!turnResult.Output.EndsWith("\n")) Console.WriteLine();
                    }

                    if (!string.IsNullOrEmpty(turnResult.Error))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write(turnResult.Error);
                        if (!turnResult.Error.EndsWith("\n")) Console.WriteLine();
                        Console.ResetColor();
                    }

                    if (!turnResult.Success && !string.IsNullOrEmpty(turnResult.ErrorMessage))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[提示] {turnResult.ErrorMessage}");
                        Console.ResetColor();
                    }
                }
            }
            finally
            {
                service.CloseInteractiveSession(session.SessionId);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[-] 已退出并销毁连续 {shellName} 会话。\n");
                Console.ResetColor();
            }

            await Task.CompletedTask;
        }

        #endregion
    }
}
