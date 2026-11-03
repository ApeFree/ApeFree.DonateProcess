using ApeFree.ApeRpc;
using ApeFree.ApeRpc.BytesIO;
using ApeFree.DonateProcess.Common;
using ApeFree.DonateProcess.Contracts;
using ApeFree.DonateProcess.Core;
using STTech.BytesIO.Core;
using STTech.BytesIO.P2P;
using STTech.BytesIO.Tcp;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ApeFree.DonateProcess.Slave
{
    /// <summary>
    /// Slave 节点控制宿主。
    /// 负责将本机进程管理与控制能力包装为 ApeRpc 服务，主动连接目标 Master 端或作为服务端监听端口等待 Master 接入。
    /// 支持标准 TCP 直连 (主动连出 / 被动监听) 以及 P2P (Libp2p) 点对点穿透传输。
    /// </summary>
    public class ProcessSlaveNode : IDisposable
    {
        private readonly ProcessSlaveOptions _options;
        private readonly ConcurrentDictionary<BytesClient, bool> _connectedClients = new ConcurrentDictionary<BytesClient, bool>();
        private BytesClient? _client;
        private TcpServer? _tcpServer;
        private RpcTerminal? _terminal;
        private BytesRpcTerminalAddon? _addon;
        private LocalProcessService? _processService;
        private CancellationTokenSource? _initialConnectCts;
        private bool _isDisposed;

        /// <summary>
        /// 自动重连间隔时间（毫秒），默认 3000ms
        /// </summary>
        public int ReconnectInterval { get; set; } = 3000;

        /// <summary>
        /// 当与 Master 端建立物理连接成功时触发（Connect 模式）
        /// </summary>
        public event EventHandler? Connected;

        /// <summary>
        /// 当与 Master 端的连接中断时触发（Connect 模式）
        /// </summary>
        public event EventHandler? Disconnected;

        /// <summary>
        /// 当有 Master 客户端连入本地服务端时触发（Listen 模式）
        /// </summary>
        public event EventHandler<BytesClient>? ClientConnected;

        /// <summary>
        /// 当连入的 Master 客户端断开连接时触发（Listen 模式）
        /// </summary>
        public event EventHandler<BytesClient>? ClientDisconnected;

        /// <summary>
        /// 获取配置项
        /// </summary>
        public ProcessSlaveOptions Options => _options;

        /// <summary>
        /// 获取当前进程服务实例
        /// </summary>
        public LocalProcessService? ProcessService => _processService;

        /// <summary>
        /// 当前是否已连接（在 Connect 模式下检测客户端连接，在 Listen 模式下检测是否有至少一个 Master 连入）
        /// </summary>
        public bool IsConnected => (_client != null && _client.IsConnected) || !_connectedClients.IsEmpty;

        /// <summary>
        /// 服务端当前是否正在运行监听（Listen 模式）
        /// </summary>
        public bool IsListening => _tcpServer != null && _tcpServer.IsListening;

        /// <summary>
        /// 当前已连入的 Master 客户端数量
        /// </summary>
        public int ConnectedClientCount => _connectedClients.Count;

        /// <summary>
        /// 最近一次连接尝试的结果详情
        /// </summary>
        public ConnectResult? LastConnectResult { get; private set; }

        /// <summary>
        /// 若在 P2P 模式下，获取当前本地节点的 PeerId
        /// </summary>
        public string? LocalPeerId => (_client as PeerClient)?.LocalPeerId;

        public ProcessSlaveNode(ProcessSlaveOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// 启动 Slave 节点并就绪提供服务（按配置选择服务端监听或主动连接 Master）
        /// </summary>
        public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ProcessSlaveNode));

            // 1. 初始化本地进程服务
            _processService = new LocalProcessService();

            // 2. 初始化 RPC 终端
            var terminalInfo = new TerminalInfo
            {
                Name = $"SlaveProcessNode-{Environment.MachineName}-{Guid.NewGuid().ToString("N").Substring(0, 6)}"
            };
            _terminal = new RpcTerminal(terminalInfo);

            if (_options.TransportMode == NetworkTransportMode.Listen)
            {
                // 3a. 本地服务端监听模式
                _tcpServer = new TcpServer
                {
                    Host = _options.ListenHost,
                    Port = _options.ListenPort
                };

                _addon = new BytesRpcTerminalAddon(_terminal);
                _addon.BindServer(_tcpServer);

                _terminal.RegisterService(_processService);

                _tcpServer.ClientConnected += (s, e) =>
                {
                    _connectedClients.TryAdd(e.Client, true);
                    ClientConnected?.Invoke(this, e.Client);
                };

                _tcpServer.ClientDisconnected += (s, e) =>
                {
                    _connectedClients.TryRemove(e.Client, out _);
                    ClientDisconnected?.Invoke(this, e.Client);
                };

                await _tcpServer.StartAsync().ConfigureAwait(false);
                return true;
            }
            else if (_options.TransportMode == NetworkTransportMode.P2P)
            {
                // 3b. P2P (Libp2p) 点对点传输模式
                var peerClient = new PeerClient();
                peerClient.Options.EnableRelay = _options.EnableRelay;
                peerClient.Options.EnableDht = _options.EnableDht;
                peerClient.Options.EnableMdns = _options.EnableMdns;
                peerClient.Options.EnableQuic = _options.EnableQuic;

                if (!string.IsNullOrWhiteSpace(_options.IdentitySeed))
                {
                    peerClient.Options.IdentitySeed = P2PAddressHelper.ParseSeed(_options.IdentitySeed);
                }

                if (string.IsNullOrWhiteSpace(_options.TargetPeerId) && !string.IsNullOrWhiteSpace(_options.MasterSeed))
                {
                    _options.TargetPeerId = P2PAddressHelper.GetPeerIdFromSeed(_options.MasterSeed);
                }

                if (!string.IsNullOrWhiteSpace(_options.P2PServerAddress))
                {
                    var resolvedServer = P2PAddressHelper.ResolveServerMultiaddr(_options.P2PServerAddress, _options.ServerPeerId, _options.ServerSeed);
                    peerClient.Options.BootstrapPeers.Add(resolvedServer);
                }

                if (_options.BootstrapPeers?.Count > 0)
                {
                    foreach (var bp in _options.BootstrapPeers)
                    {
                        if (!peerClient.Options.BootstrapPeers.Contains(bp))
                        {
                            peerClient.Options.BootstrapPeers.Add(bp);
                        }
                    }
                }

                string remoteAddr;
                if (!string.IsNullOrWhiteSpace(_options.P2PRemoteAddress))
                {
                    remoteAddr = _options.P2PRemoteAddress;
                }
                else if (!string.IsNullOrWhiteSpace(_options.P2PServerAddress))
                {
                    remoteAddr = P2PAddressHelper.ResolveServerMultiaddr(_options.P2PServerAddress, _options.ServerPeerId, _options.ServerSeed);
                }
                else if (!string.IsNullOrWhiteSpace(_options.TargetPeerId))
                {
                    remoteAddr = $"/p2p/{_options.TargetPeerId}";
                }
                else
                {
                    var host = string.IsNullOrWhiteSpace(_options.TargetHost) ? "127.0.0.1" : _options.TargetHost;
                    var port = _options.TargetPort > 0 ? _options.TargetPort : 9832;
                    remoteAddr = P2PAddressHelper.ToMultiaddr($"{host}:{port}");
                }

                peerClient.RemoteAddress = remoteAddr;
                _client = peerClient;
            }
            else
            {
                // 3c. 标准 TCP 客户端主动拨号模式
                _client = new TcpClient
                {
                    Host = _options.TargetHost,
                    Port = _options.TargetPort
                };
            }

            // 启用自动无限重连
            _client.UseAutoReconnect(ReconnectInterval, 0);

            _client.OnDisconnected += (s, e) =>
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
            };

            _client.OnConnectedSuccessfully += (s, e) =>
            {
                _initialConnectCts?.Cancel();
                Connected?.Invoke(this, EventArgs.Empty);

                if (_options.TransportMode == NetworkTransportMode.P2P && !string.IsNullOrWhiteSpace(_options.P2PServerAddress))
                {
                    NotifyMasterReadyWithRetry();
                }
            };

            // 挂载 BytesRpc 插件并绑定信道
            _addon = new BytesRpcTerminalAddon(_terminal, _client);

            // 注册服务契约实现
            _terminal.RegisterService(_processService);

            // 连接 Master 端
            var connectResult = await _client.ConnectAsync(new ConnectArgument { Timeout = 10000, CancellationToken = cancellationToken });
            LastConnectResult = connectResult;

            if (connectResult.IsSuccess)
            {
                if (_options.TransportMode == NetworkTransportMode.P2P && !string.IsNullOrWhiteSpace(_options.P2PServerAddress))
                {
                    NotifyMasterReadyWithRetry();
                }
            }
            else if (!cancellationToken.IsCancellationRequested)
            {
                StartInitialReconnectLoop();
            }

            return connectResult.IsSuccess;
        }

        private void NotifyMasterReadyWithRetry()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(400).ConfigureAwait(false);
                for (int i = 0; i < 5; i++)
                {
                    if (_isDisposed || _client == null || !_client.IsConnected) break;
                    try
                    {
                        var masterService = _addon?.QueryService<IMasterNodeService>(nameof(IMasterNodeService), _client);
                        if (masterService != null)
                        {
                            masterService.NotifySlaveReady();
                            break;
                        }
                    }
                    catch
                    {
                        // 稍微重试
                    }
                    await Task.Delay(1000).ConfigureAwait(false);
                }
            });
        }

        private void StartInitialReconnectLoop()
        {
            _initialConnectCts?.Cancel();
            _initialConnectCts = new CancellationTokenSource();
            var token = _initialConnectCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && !_isDisposed)
                {
                    try
                    {
                        await Task.Delay(ReconnectInterval, token).ConfigureAwait(false);
                        if (_client == null || _client.IsConnected) break;

                        var res = await _client.ConnectAsync(new ConnectArgument { Timeout = 10000, CancellationToken = token }).ConfigureAwait(false);
                        LastConnectResult = res;
                        if (res.IsSuccess)
                        {
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                    }
                }
            }, token);
        }

        public async Task StopAsync()
        {
            _initialConnectCts?.Cancel();

            if (_tcpServer != null)
            {
                await _tcpServer.CloseAsync().ConfigureAwait(false);
                _tcpServer = null;
            }

            if (_client != null)
            {
                _client.DisableAutoReconnect();
                if (_client.IsConnected)
                {
                    _client.Disconnect(new DisconnectArgument(DisconnectionReasonCode.Active));
                }
            }

            _connectedClients.Clear();
            _processService?.Dispose();
        }

        public void Stop()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();

            _initialConnectCts?.Dispose();
            _client?.Dispose();
            _tcpServer?.Dispose();
            _addon?.Dispose();
        }
    }
}
