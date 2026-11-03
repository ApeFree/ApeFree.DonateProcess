using ApeFree.DonateProcess.Contracts;
using ApeFree.DonateProcess.Master;
using ApeFree.DonateProcess.Slave;
using ApeFree.PyramidMenu.Menus;
using ApeFree.PyramidMenu.Options;
using ApeFree.PyramidMenu.Scenes;
using System;

namespace ApeFree.DonateProcess.WinCli.Common
{
    public enum WizardRunMode
    {
        None,
        Master,
        Slave,
        P2PServer,
        Local
    }

    public class WizardResult
    {
        public WizardRunMode Mode { get; set; }
        public ProcessMasterOptions? MasterOptions { get; set; }
        public ProcessSlaveOptions? SlaveOptions { get; set; }
        public int P2PServerPort { get; set; } = 9832;
    }

    public static class DonateProcessMenuWizard
    {
        private static void SafeClear()
        {
            try
            {
                if (!Console.IsOutputRedirected && !Console.IsInputRedirected)
                {
                    Console.Clear();
                }
            }
            catch { }
        }

        public static WizardResult? Run()
        {
            var rootMenu = new WizardRootMenu();
            var scene = new Scene(rootMenu);

            while (!rootMenu.IsFinished)
            {
                SafeClear();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("===============================================================");
                Console.WriteLine("      ApeFree.DonateProcess 交互式向导 (基于 PyramidMenu 引擎)");
                Console.WriteLine("===============================================================");
                Console.ResetColor();

                var rendered = scene.RenderCurrentMenu();
                Console.WriteLine(rendered);

                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write("\n请根据提示输入序号 (直接按 [回车] 默认选择 [1]，输入 'q' 退出): ");
                Console.ResetColor();

                var rawInput = Console.ReadLine();
                if (rawInput == null)
                {
                    return null;
                }

                var input = rawInput.Trim();
                if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (string.IsNullOrEmpty(input))
                {
                    input = "1";
                }

                var result = scene.Input(new Packet(input));
                if (!result.IsSuccessful && !rootMenu.IsFinished)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[提示] 输入无效: {result.PromptText ?? "请重新选择"}");
                    Console.ResetColor();
                    Console.WriteLine("按任意键继续...");
                    try
                    {
                        if (!Console.IsInputRedirected)
                        {
                            Console.ReadKey(true);
                        }
                    }
                    catch { }
                }
            }

            return rootMenu.FinalResult;
        }

        public class WizardRootMenu : TextMenu
        {
            public bool IsFinished { get; set; }
            public WizardResult? FinalResult { get; set; }

            protected override void OnCreate()
            {
                Title = "选择运行角色";
                TextContent = "请选择当前节点要在本机扮演的进程管理角色：";

                AddMenuOption("Master 远程控制端 (发起远程命令执行与交互终端交互)", typeof(MasterTransportSelectMenu));
                AddMenuOption("Slave 受控分享节点 (提供本机进程管理与执行能力供 Master 调度)", typeof(SlaveTransportSelectMenu));
                AddMenuOption("P2P 引导与中继服务端 (独立启动 Relay Server)", typeof(P2PServerConfigMenu));
                AddCustomOption("本地单机控制台 (无需网络直接管理本机进程与交互终端)", (ctx, opt, packet, ct) =>
                {
                    var root = ctx.RootMenu as WizardRootMenu;
                    if (root != null)
                    {
                        root.FinalResult = new WizardResult
                        {
                            Mode = WizardRunMode.Local
                        };
                        root.IsFinished = true;
                    }
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });
                AddDivider();
                AddCustomOption("退出程序", (ctx, opt, packet, ct) =>
                {
                    IsFinished = true;
                    FinalResult = null;
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });
            }
        }

        #region Master 菜单分支

        internal class MasterTransportSelectMenu : TextMenu
        {
            protected override void OnCreate()
            {
                Title = "Master 控制端 - 选择通信模式";
                TextContent = "请选择 Master 端与 Slave 端的网络连通方式：";

                AddMenuOption("被动监听等待 (Listen，默认反向模式：等待 Slave 拨号连入)", typeof(MasterListenConfigMenu));
                AddMenuOption("主动拨号连接 (Connect，正向直连：主动连接目标 Slave 机器)", typeof(MasterConnectConfigMenu));
                AddMenuOption("P2P 中继穿透模式 (双方均位于 NAT 内部，通过公网 Relay 打通)", typeof(MasterP2PConfigMenu));
                AddDivider();
                AddBackOption("返回上级菜单");
            }
        }

        internal class MasterListenConfigMenu : TextMenu
        {
            private string _host = "0.0.0.0";
            private int _port = 9822;

            protected override void OnStart(StartEventArgs e)
            {
                base.OnStart(e);
                UpdateMenu();
            }

            private void UpdateMenu()
            {
                Title = "Master 服务端监听配置 (反向受控模式)";
                TextContent = $"【当前预设配置】\r\n" +
                              $"  - 监听网络: {_host}:{_port}\r\n" +
                              $"  - 行为机制: 开放端口等待受控 Slave 节点主动反弹连入";

                OptionList.Clear();
                AddCustomOption("[一键极速启动] 开放端口监听并等待 Slave 连入", (ctx, opt, packet, ct) =>
                {
                    var root = ctx.RootMenu as WizardRootMenu;
                    if (root != null)
                    {
                        root.FinalResult = new WizardResult
                        {
                            Mode = WizardRunMode.Master,
                            MasterOptions = new ProcessMasterOptions
                            {
                                TransportMode = NetworkTransportMode.Listen,
                                ListenHost = _host,
                                ListenPort = _port
                            }
                        };
                        root.IsFinished = true;
                    }
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });

                AddCustomOption($"修改监听端口 (当前: {_port})", (ctx, opt, packet, ct) =>
                {
                    Console.Write($"\n请输入新的监听端口 [当前: {_port}]: ");
                    var pStr = Console.ReadLine()?.Trim();
                    if (int.TryParse(pStr, out var newPort) && newPort > 0 && newPort <= 65535) _port = newPort;
                    UpdateMenu();
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });

                AddDivider();
                AddBackOption("返回上级");
            }
        }

        internal class MasterConnectConfigMenu : TextMenu
        {
            private string _targetHost = "127.0.0.1";
            private int _targetPort = 9822;

            protected override void OnStart(StartEventArgs e)
            {
                base.OnStart(e);
                UpdateMenu();
            }

            private void UpdateMenu()
            {
                Title = "Master 主动连接配置 (正向控制模式)";
                TextContent = $"【当前预设配置】\r\n" +
                              $"  - 目标 Slave 地址: {_targetHost}:{_targetPort}\r\n" +
                              $"  - 容错机制:        断线后台无限自动重试 (每 3 秒)";

                OptionList.Clear();
                AddCustomOption("[一键极速启动] 主动连接目标 Slave 并开展远程控制", (ctx, opt, packet, ct) =>
                {
                    var root = ctx.RootMenu as WizardRootMenu;
                    if (root != null)
                    {
                        root.FinalResult = new WizardResult
                        {
                            Mode = WizardRunMode.Master,
                            MasterOptions = new ProcessMasterOptions
                            {
                                TransportMode = NetworkTransportMode.Connect,
                                TargetHost = _targetHost,
                                TargetPort = _targetPort
                            }
                        };
                        root.IsFinished = true;
                    }
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });

                AddCustomOption($"修改目标 Slave 地址 (当前: {_targetHost}:{_targetPort})", (ctx, opt, packet, ct) =>
                {
                    Console.Write($"\n请输入目标 Slave IP [当前: {_targetHost}]: ");
                    var h = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrEmpty(h)) _targetHost = h;

                    Console.Write($"请输入目标 Slave 端口 [当前: {_targetPort}]: ");
                    var pStr = Console.ReadLine()?.Trim();
                    if (int.TryParse(pStr, out var newPort) && newPort > 0 && newPort <= 65535) _targetPort = newPort;

                    UpdateMenu();
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });

                AddDivider();
                AddBackOption("返回上级");
            }
        }

        internal class MasterP2PConfigMenu : TextMenu
        {
            private string _serverAddr = "127.0.0.1:9832";

            protected override void OnStart(StartEventArgs e)
            {
                base.OnStart(e);
                UpdateMenu();
            }

            private void UpdateMenu()
            {
                Title = "Master P2P 穿透配置";
                TextContent = $"【当前预设配置】\r\n" +
                              $"  - P2P 中继服务端: {_serverAddr}\r\n" +
                              $"  - 凭据机制:       内置预设种子 (自动打通)";

                OptionList.Clear();
                AddCustomOption("[一键极速启动] 连入公网 Relay 并就绪控制", (ctx, opt, packet, ct) =>
                {
                    var root = ctx.RootMenu as WizardRootMenu;
                    if (root != null)
                    {
                        root.FinalResult = new WizardResult
                        {
                            Mode = WizardRunMode.Master,
                            MasterOptions = new ProcessMasterOptions
                            {
                                TransportMode = NetworkTransportMode.P2P,
                                UseP2P = true,
                                P2PServerAddress = _serverAddr
                            }
                        };
                        root.IsFinished = true;
                    }
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });

                AddCustomOption($"修改 P2P 服务端地址 [当前: {_serverAddr}]", (ctx, opt, packet, ct) =>
                {
                    Console.Write($"\n请输入 P2P 服务端地址: ");
                    var s = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrEmpty(s)) _serverAddr = s;
                    UpdateMenu();
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });

                AddDivider();
                AddBackOption("返回上级");
            }
        }

        #endregion

        #region Slave 菜单分支

        internal class SlaveTransportSelectMenu : TextMenu
        {
            protected override void OnCreate()
            {
                Title = "Slave 受控端 - 选择通信模式";
                TextContent = "请选择 Slave 端与 Master 端的网络连通方式：";

                AddMenuOption("主动拨号连接 (Connect，默认反向穿透：主动连接 Master)", typeof(SlaveConnectConfigMenu));
                AddMenuOption("被动监听等待 (Listen，正向受控服务：开放端口等待 Master 接入)", typeof(SlaveListenConfigMenu));
                AddMenuOption("P2P 中继穿透模式 (双方均位于 NAT 内部，通过公网 Relay 打通)", typeof(SlaveP2PConfigMenu));
                AddDivider();
                AddBackOption("返回上级菜单");
            }
        }

        internal class SlaveConnectConfigMenu : TextMenu
        {
            private string _targetHost = "127.0.0.1";
            private int _targetPort = 9822;

            protected override void OnStart(StartEventArgs e)
            {
                base.OnStart(e);
                UpdateMenu();
            }

            private void UpdateMenu()
            {
                Title = "Slave 主动连出配置 (反向穿透模式)";
                TextContent = $"【当前预设配置】\r\n" +
                              $"  - 目标 Master: {_targetHost}:{_targetPort}\r\n" +
                              $"  - 自动重连:   已启用 (每 3 秒)";

                OptionList.Clear();
                AddCustomOption("[一键极速启动] 主动拨号连接 Master 并共享进程管理能力", (ctx, opt, packet, ct) =>
                {
                    var root = ctx.RootMenu as WizardRootMenu;
                    if (root != null)
                    {
                        root.FinalResult = new WizardResult
                        {
                            Mode = WizardRunMode.Slave,
                            SlaveOptions = new ProcessSlaveOptions
                            {
                                TransportMode = NetworkTransportMode.Connect,
                                TargetHost = _targetHost,
                                TargetPort = _targetPort
                            }
                        };
                        root.IsFinished = true;
                    }
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });

                AddCustomOption($"修改目标 Master 地址 (当前: {_targetHost}:{_targetPort})", (ctx, opt, packet, ct) =>
                {
                    Console.Write($"\n请输入目标 Master IP [当前: {_targetHost}]: ");
                    var h = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrEmpty(h)) _targetHost = h;

                    Console.Write($"请输入目标 Master 端口 [当前: {_targetPort}]: ");
                    var pStr = Console.ReadLine()?.Trim();
                    if (int.TryParse(pStr, out var newPort) && newPort > 0 && newPort <= 65535) _targetPort = newPort;

                    UpdateMenu();
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });

                AddDivider();
                AddBackOption("返回上级");
            }
        }

        internal class SlaveListenConfigMenu : TextMenu
        {
            private string _host = "0.0.0.0";
            private int _port = 9822;

            protected override void OnStart(StartEventArgs e)
            {
                base.OnStart(e);
                UpdateMenu();
            }

            private void UpdateMenu()
            {
                Title = "Slave 服务端监听配置 (正向受控服务模式)";
                TextContent = $"【当前预设配置】\r\n" +
                              $"  - 监听网络: {_host}:{_port}\r\n" +
                              $"  - 多端并发: 完全允许 (支持多个 Master 并发连入执行)";

                OptionList.Clear();
                AddCustomOption("[一键极速启动] 开放端口监听并等待 Master 连入", (ctx, opt, packet, ct) =>
                {
                    var root = ctx.RootMenu as WizardRootMenu;
                    if (root != null)
                    {
                        root.FinalResult = new WizardResult
                        {
                            Mode = WizardRunMode.Slave,
                            SlaveOptions = new ProcessSlaveOptions
                            {
                                TransportMode = NetworkTransportMode.Listen,
                                ListenHost = _host,
                                ListenPort = _port
                            }
                        };
                        root.IsFinished = true;
                    }
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });

                AddCustomOption($"修改本地监听端口 (当前: {_port})", (ctx, opt, packet, ct) =>
                {
                    Console.Write($"\n请输入本地监听端口 [当前: {_port}]: ");
                    var pStr = Console.ReadLine()?.Trim();
                    if (int.TryParse(pStr, out var newPort) && newPort > 0 && newPort <= 65535) _port = newPort;
                    UpdateMenu();
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });

                AddDivider();
                AddBackOption("返回上级");
            }
        }

        internal class SlaveP2PConfigMenu : TextMenu
        {
            private string _serverAddr = "127.0.0.1:9832";

            protected override void OnStart(StartEventArgs e)
            {
                base.OnStart(e);
                UpdateMenu();
            }

            private void UpdateMenu()
            {
                Title = "Slave P2P 穿透配置";
                TextContent = $"【当前预设配置】\r\n" +
                              $"  - P2P 中继服务端: {_serverAddr}";

                OptionList.Clear();
                AddCustomOption("[一键极速启动] 连入公网 Relay 并就绪受控", (ctx, opt, packet, ct) =>
                {
                    var root = ctx.RootMenu as WizardRootMenu;
                    if (root != null)
                    {
                        root.FinalResult = new WizardResult
                        {
                            Mode = WizardRunMode.Slave,
                            SlaveOptions = new ProcessSlaveOptions
                            {
                                TransportMode = NetworkTransportMode.P2P,
                                UseP2P = true,
                                P2PServerAddress = _serverAddr
                            }
                        };
                        root.IsFinished = true;
                    }
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });

                AddCustomOption($"修改 P2P 中继服务端地址 (当前: {_serverAddr})", (ctx, opt, packet, ct) =>
                {
                    Console.Write($"\n请输入 P2P 中继服务端地址: ");
                    var s = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrEmpty(s)) _serverAddr = s;
                    UpdateMenu();
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });

                AddDivider();
                AddBackOption("返回上级");
            }
        }

        #endregion

        #region P2P Server 菜单分支

        internal class P2PServerConfigMenu : TextMenu
        {
            private int _port = 9832;

            protected override void OnStart(StartEventArgs e)
            {
                base.OnStart(e);
                Title = "P2P 引导与中继服务端配置";
                TextContent = $"【当前预设配置】\r\n" +
                              $"  - 监听端口: {_port}\r\n" +
                              $"  - 身份凭据: 内置默认固定种子 (零配置直连)";

                OptionList.Clear();
                AddCustomOption("[一键极速启动] 立即启动 P2P 中继服务端", (ctx, opt, packet, ct) =>
                {
                    var root = ctx.RootMenu as WizardRootMenu;
                    if (root != null)
                    {
                        root.FinalResult = new WizardResult
                        {
                            Mode = WizardRunMode.P2PServer,
                            P2PServerPort = _port
                        };
                        root.IsFinished = true;
                    }
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });

                AddCustomOption($"修改监听端口 (当前: {_port})", (ctx, opt, packet, ct) =>
                {
                    Console.Write($"\n请输入监听端口 [当前: {_port}]: ");
                    var pStr = Console.ReadLine()?.Trim();
                    if (int.TryParse(pStr, out var newPort) && newPort > 0 && newPort <= 65535) _port = newPort;
                    return System.Threading.Tasks.Task.FromResult(Result.Success(ctx, opt, packet));
                });

                AddDivider();
                AddBackOption("返回上级菜单");
            }
        }

        #endregion
    }
}
