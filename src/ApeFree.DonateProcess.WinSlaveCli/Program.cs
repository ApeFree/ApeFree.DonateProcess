using ApeFree.DonateProcess.Common;
using ApeFree.DonateProcess.Contracts;
using ApeFree.DonateProcess.Slave;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ApeFree.DonateProcess.WinSlaveCli
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0.0";
            return await RunAsync(args, appVersion, false);
        }

        public static async Task<int> RunAsync(string[] args, string appVersion, bool isP2P)
        {
            Console.Title = $"ApeFree.DonateProcess.WinSlaveCli v{appVersion} - 远程进程执行共享节点";

            PrintBanner(appVersion);

            if (args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase) || a.Equals("-?", StringComparison.OrdinalIgnoreCase)))
            {
                PrintHelp();
                return 0;
            }

            var options = ParseOptions(args);
            if (options == null)
            {
                options = PromptInteractiveOptions();
                if (options == null) return 1;
            }
            if (isP2P)
            {
                options.TransportMode = NetworkTransportMode.P2P;
                options.UseP2P = true;
            }

            return await RunWithOptionsAsync(options, appVersion);
        }

        public static async Task<int> RunWithOptionsAsync(ProcessSlaveOptions options, string appVersion)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==> 正在启动 Slave 进程管理共享节点 ...");
            Console.ResetColor();

            if (options.TransportMode == NetworkTransportMode.P2P)
            {
                Console.WriteLine($"    模式: P2P (Libp2p 去中心化中继穿透)");
                if (!string.IsNullOrWhiteSpace(options.P2PServerAddress))
                {
                    Console.WriteLine($"    P2P 中继服务器: {options.P2PServerAddress}");
                }
            }
            else if (options.TransportMode == NetworkTransportMode.Listen)
            {
                Console.WriteLine($"    模式: TCP 本地服务端监听 (正向受控服务)");
                Console.WriteLine($"    监听地址: {options.ListenHost}:{options.ListenPort}");
                Console.WriteLine($"    多端并发: 完全允许");
            }
            else
            {
                Console.WriteLine($"    模式: TCP 主动连出 (反向穿透模式)");
                Console.WriteLine($"    目标 Master 地址: {options.TargetHost}:{options.TargetPort}");
            }

            using var slaveNode = new ProcessSlaveNode(options);

            slaveNode.Connected += (s, e) =>
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[+] 已成功连接到 Master 端！本机进程执行与多轮会话服务已就绪。");
                Console.ResetColor();
            };

            slaveNode.Disconnected += (s, e) =>
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[-] 与 Master 端连接已断开，正在后台持续自动重连 ...");
                Console.ResetColor();
            };

            slaveNode.ClientConnected += (s, client) =>
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[+] Master 客户端已接入: {client} (当前在线 Master 数: {slaveNode.ConnectedClientCount})");
                Console.ResetColor();
            };

            slaveNode.ClientDisconnected += (s, client) =>
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[-] Master 客户端已断开: {client} (当前在线 Master 数: {slaveNode.ConnectedClientCount})");
                Console.ResetColor();
            };

            var initialSuccess = await slaveNode.StartAsync(cts.Token);
            if (initialSuccess)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(options.TransportMode == NetworkTransportMode.Listen
                    ? "[+] 服务端监听已就绪，正在等待 Master 客户端接入 ..."
                    : "[+] 初始连接成功。");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("[*] 初始连接未就绪，后台重连循环已启动，将在网络可用时自动接入 ...");
                Console.ResetColor();
            }

            if (options.TransportMode == NetworkTransportMode.P2P && !string.IsNullOrWhiteSpace(slaveNode.LocalPeerId))
            {
                Console.WriteLine($"    本机 PeerId: {slaveNode.LocalPeerId}");
            }

            Console.WriteLine("\n按 Ctrl+C 退出 Slave 节点程序。\n");

            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
            }

            Console.WriteLine("正在退出并清理资源 ...");
            return 0;
        }

        private static void PrintBanner(string version)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine($"  ApeFree.DonateProcess.WinSlaveCli v{version}");
            Console.WriteLine("  主动式远程进程执行与多轮命令行会话共享客户端");
            Console.WriteLine("==================================================================");
            Console.ResetColor();
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"用法:
  ApeFree.DonateProcess.WinSlaveCli.exe [选项]

网络模式选项:
  -c, --connect             作为客户端主动连入 Master（默认模式）
  -l, --listen              作为服务端被动监听（正向受控模式，等待 Master 连入）
  -h, --host <主机>         Master 端 IP 地址或域名 (默认: 127.0.0.1)
  -p, --port <端口>         通信端口或本地监听端口 (默认 TCP: 9822, P2P: 9832)

P2P 选项:
  -p2p, --p2p               启用 P2P (Libp2p) 去中心化中继穿透模式
  --server <地址>           公网 P2P 引导与中继服务器地址 (例如: 127.0.0.1:9832)
  --seed <口令>             本地节点私钥派生种子 (确保生成固定 PeerId)
  --master-seed <口令>      目标 Master 节点公钥派生种子
  --help, -h                显示帮助信息

示例:
  # 模式 A: 反向穿透模式 (主动连向 Master)
  ApeFree.DonateProcess.WinSlaveCli.exe -c -h 192.168.1.100 -p 9822

  # 模式 B: 正向受控服务模式 (本地监听等待 Master 接入，支持多 Master 并发)
  ApeFree.DonateProcess.WinSlaveCli.exe -l -p 9822

  # 模式 C: P2P 穿透中继模式
  ApeFree.DonateProcess.WinSlaveCli.exe -p2p --server 127.0.0.1:9832
");
        }

        private static ProcessSlaveOptions? ParseOptions(string[] args)
        {
            if (args.Length == 0) return null;

            var options = new ProcessSlaveOptions();
            bool isExplicitListen = false;
            bool isExplicitConnect = false;

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i].ToLowerInvariant();
                if (arg == "-p2p" || arg == "--p2p")
                {
                    options.UseP2P = true;
                }
                else if (arg == "-l" || arg == "--listen")
                {
                    isExplicitListen = true;
                }
                else if (arg == "-c" || arg == "--connect")
                {
                    isExplicitConnect = true;
                }
                else if ((arg == "--server" || arg == "-server") && i + 1 < args.Length)
                {
                    options.P2PServerAddress = args[++i];
                    options.UseP2P = true;
                }
                else if ((arg == "-h" || arg == "--host") && i + 1 < args.Length)
                {
                    options.TargetHost = args[++i];
                    options.ListenHost = options.TargetHost;
                }
                else if ((arg == "-p" || arg == "--port") && i + 1 < args.Length && int.TryParse(args[++i], out var p))
                {
                    options.TargetPort = p;
                    options.ListenPort = p;
                }
                else if (arg == "--seed" && i + 1 < args.Length)
                {
                    options.IdentitySeed = args[++i];
                }
                else if (arg == "--master-seed" && i + 1 < args.Length)
                {
                    options.MasterSeed = args[++i];
                }
            }

            if (options.UseP2P)
            {
                options.TransportMode = NetworkTransportMode.P2P;
            }
            else if (isExplicitListen && !isExplicitConnect)
            {
                options.TransportMode = NetworkTransportMode.Listen;
            }
            else
            {
                options.TransportMode = NetworkTransportMode.Connect;
            }

            return options;
        }

        private static ProcessSlaveOptions PromptInteractiveOptions()
        {
            var options = new ProcessSlaveOptions();

            Console.WriteLine("请选择网络连接模式：");
            Console.WriteLine("  1. 主动拨号连入 Master (反向穿透模式，默认)");
            Console.WriteLine("  2. 服务端本地监听 (正向受控服务，等待 Master 连入)");
            Console.WriteLine("  3. P2P 中继穿透模式 (通过公网 Relay 节点打通)");
            Console.Write("请输入选项 (1/2/3, 默认 1): ");
            var choice = Console.ReadLine()?.Trim();

            if (choice == "2")
            {
                options.TransportMode = NetworkTransportMode.Listen;
                options.UseP2P = false;
                Console.Write("请输入本地监听端口 (默认 9822): ");
                var portStr = Console.ReadLine()?.Trim();
                if (int.TryParse(portStr, out var port)) options.ListenPort = port;
            }
            else if (choice == "3")
            {
                options.TransportMode = NetworkTransportMode.P2P;
                options.UseP2P = true;
                Console.Write("请输入 P2P 中继服务器地址 (默认 127.0.0.1:9832): ");
                var srv = Console.ReadLine()?.Trim();
                options.P2PServerAddress = string.IsNullOrWhiteSpace(srv) ? "127.0.0.1:9832" : srv;
            }
            else
            {
                options.TransportMode = NetworkTransportMode.Connect;
                options.UseP2P = false;
                Console.Write("请输入 Master 端主机 IP (默认 127.0.0.1): ");
                var host = Console.ReadLine()?.Trim();
                if (!string.IsNullOrWhiteSpace(host)) options.TargetHost = host;

                Console.Write("请输入 Master 端监听端口 (默认 9822): ");
                var portStr = Console.ReadLine()?.Trim();
                if (int.TryParse(portStr, out var port)) options.TargetPort = port;
            }

            return options;
        }
    }
}
