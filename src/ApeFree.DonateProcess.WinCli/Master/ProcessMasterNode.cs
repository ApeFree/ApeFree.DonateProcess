using ApeFree.ApeRpc;
using ApeFree.ApeRpc.BytesIO;
using ApeFree.DonateProcess.Common;
using ApeFree.DonateProcess.Contracts;
using STTech.BytesIO.Core;
using STTech.BytesIO.P2P;
using STTech.BytesIO.Tcp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApeFree.DonateProcess.Master
{
    /// <summary>
    /// Master 端控制宿主。
    /// 负责监听 TCP 端口、主动连接目标 Slave 或连入 P2P 中继服务器，
    /// 并通过 ApeRpc 动态代理获取远端进程管理服务接口 <see cref="IProcessOperationsService"/>。
    /// </summary>
    public class ProcessMasterNode : IMasterNodeService, IDisposable
    {
        private readonly ProcessMasterOptions _options;
        private TcpServer? _tcpServer;
        private TcpClient? _tcpClient;
        private PeerClient? _p2pClient;
        private RpcTerminal? _terminal;
        private BytesRpcTerminalAddon? _addon;
        private BytesClient? _activeClient;
        private IProcessOperationsService? _remoteService;
        private CancellationTokenSource? _initialConnectCts;
        private bool _isDisposed;

        /// <summary>
        /// 当 Slave 节点成功连入并就绪时触发
        /// </summary>
        public event EventHandler<IProcessOperationsService>? SlaveConnected;

        /// <summary>
        /// 当 Slave 节点断开连接时触发
        /// </summary>
        public event EventHandler? SlaveDisconnected;

        /// <summary>
        /// 远端交互式会话输出实时事件广播
        /// </summary>
        public event EventHandler<SessionOutputDto>? SessionOutputReceived;

        /// <summary>
        /// 远端进程退出事件广播
        /// </summary>
        public event EventHandler<ProcessExitedEventDto>? ProcessExited;

        /// <summary>
        /// 获取配置项
        /// </summary>
        public ProcessMasterOptions Options => _options;

        /// <summary>
        /// 获取当前已绑定的远端进程服务代理（若尚未连接则为 null）
        /// </summary>
        public IProcessOperationsService? RemoteService => _remoteService;

        /// <summary>
        /// RPC 服务名称标识
        /// </summary>
        public string ServiceName => nameof(IMasterNodeService);

        /// <summary>
        /// 当前是否有活动的 Slave 连接
        /// </summary>
        public bool HasActiveSlave => _activeClient != null && _activeClient.IsConnected && _remoteService != null;

        /// <summary>
        /// P2P 模式下本地分配的 PeerId
        /// </summary>
        public string? LocalPeerId => _p2pClient?.LocalPeerId;

        public ProcessMasterNode(ProcessMasterOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// 启动 Master 端宿主并准备接收或连接 Slave
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ProcessMasterNode));

            var terminalInfo = new TerminalInfo
            {
                Name = $"MasterProcessNode-{Environment.MachineName}-{Guid.NewGuid().ToString("N").Substring(0, 6)}"
            };
            _terminal = new RpcTerminal(terminalInfo);
            _addon = new BytesRpcTerminalAddon(_terminal);
            _terminal.RegisterService(this);

            if (_options.TransportMode == NetworkTransportMode.P2P)
            {
                _p2pClient = new PeerClient();
                _p2pClient.Options.EnableRelay = _options.EnableRelay;
                _p2pClient.Options.EnableDht = _options.EnableDht;
                _p2pClient.Options.EnableMdns = _options.EnableMdns;
                _p2pClient.Options.EnableQuic = _options.EnableQuic;

                if (!string.IsNullOrWhiteSpace(_options.MasterSeed))
                {
                    _p2pClient.Options.IdentitySeed = P2PAddressHelper.ParseSeed(_options.MasterSeed);
                }

                if (!string.IsNullOrWhiteSpace(_options.P2PServerAddress))
                {
                    var resolvedServer = P2PAddressHelper.ResolveServerMultiaddr(_options.P2PServerAddress, _options.ServerPeerId, _options.ServerSeed);
                    _p2pClient.Options.BootstrapPeers.Add(resolvedServer);
                    _p2pClient.RemoteAddress = resolvedServer;
                }

                _addon.BindClient(_p2pClient);
                _p2pClient.UseAutoReconnect(3000, 0);

                _p2pClient.OnConnectedSuccessfully += (s, e) =>
                {
                    TryBindRemoteService(_p2pClient);
                };

                _p2pClient.OnDisconnected += (s, e) =>
                {
                    HandleClientDisconnected(_p2pClient);
                };

                var conn = await _p2pClient.ConnectAsync(new ConnectArgument { Timeout = 10000, CancellationToken = cancellationToken });
                if (!conn.IsSuccess && !cancellationToken.IsCancellationRequested)
                {
                    StartInitialConnectLoop(_p2pClient);
                }
            }
            else if (_options.TransportMode == NetworkTransportMode.Connect)
            {
                _tcpClient = new TcpClient
                {
                    Host = _options.TargetHost,
                    Port = _options.TargetPort
                };

                _addon.BindClient(_tcpClient);
                _tcpClient.UseAutoReconnect(3000, 0);

                _tcpClient.OnConnectedSuccessfully += (s, e) =>
                {
                    _initialConnectCts?.Cancel();
                    HandleClientConnected(_tcpClient);
                };

                _tcpClient.OnDisconnected += (s, e) =>
                {
                    HandleClientDisconnected(_tcpClient);
                };

                var conn = await _tcpClient.ConnectAsync(new ConnectArgument { Timeout = 10000, CancellationToken = cancellationToken });
                if (conn.IsSuccess)
                {
                    HandleClientConnected(_tcpClient);
                }
                else if (!cancellationToken.IsCancellationRequested)
                {
                    StartInitialTcpConnectLoop(_tcpClient);
                }
            }
            else
            {
                _tcpServer = new TcpServer
                {
                    Host = _options.ListenHost,
                    Port = _options.ListenPort
                };

                _addon.BindServer(_tcpServer);

                _tcpServer.ClientConnected += (s, e) =>
                {
                    HandleClientConnected(e.Client);
                };

                _tcpServer.ClientDisconnected += (s, e) =>
                {
                    HandleClientDisconnected(e.Client);
                };

                await _tcpServer.StartAsync();
            }
        }

        private void HandleClientConnected(BytesClient client)
        {
            _activeClient = client;
            TryBindRemoteService(client);
        }

        private void HandleClientDisconnected(BytesClient client)
        {
            if (_activeClient == client)
            {
                _activeClient = null;
                _remoteService = null;
                SlaveDisconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <inheritdoc />
        public void NotifySlaveReady()
        {
            if (_activeClient != null && _activeClient.IsConnected)
            {
                TryBindRemoteService(_activeClient);
            }
            else if (_p2pClient != null && _p2pClient.IsConnected)
            {
                TryBindRemoteService(_p2pClient);
            }
        }

        private void TryBindRemoteService(BytesClient client)
        {
            try
            {
                if (_addon == null) return;
                var service = _addon.QueryService<IProcessOperationsService>(nameof(IProcessOperationsService), client);
                if (service != null)
                {
                    _activeClient = client;
                    _remoteService = service;

                    // 挂载远端事件监听
                    try
                    {
                        service.SessionOutputReceived += (s, e) =>
                        {
                            SessionOutputReceived?.Invoke(this, e);
                        };
                        service.ProcessExited += (s, e) =>
                        {
                            ProcessExited?.Invoke(this, e);
                        };
                    }
                    catch
                    {
                        // 忽略事件挂载异常
                    }

                    SlaveConnected?.Invoke(this, service);
                }
            }
            catch
            {
                // 对端可能尚未完成服务注册，等待重试
            }
        }

        private void StartInitialTcpConnectLoop(TcpClient client)
        {
            _initialConnectCts?.Cancel();
            _initialConnectCts = new CancellationTokenSource();
            var token = _initialConnectCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && !_isDisposed && (client != null && !client.IsConnected))
                {
                    try
                    {
                        await Task.Delay(3000, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested || _isDisposed || client == null || client.IsConnected) break;
                        var res = await client.ConnectAsync(new ConnectArgument { Timeout = 10000, CancellationToken = token }).ConfigureAwait(false);
                        if (res.IsSuccess)
                        {
                            HandleClientConnected(client);
                            break;
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, token);
        }

        private void StartInitialConnectLoop(PeerClient client)
        {
            _initialConnectCts?.Cancel();
            _initialConnectCts = new CancellationTokenSource();
            var token = _initialConnectCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && !_isDisposed && (client != null && !client.IsConnected))
                {
                    try
                    {
                        await Task.Delay(3000, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested || _isDisposed || client == null || client.IsConnected) break;
                        await client.ConnectAsync(new ConnectArgument { Timeout = 10000, CancellationToken = token }).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, token);
        }

        public async Task StopAsync()
        {
            _initialConnectCts?.Cancel();

            if (_tcpServer != null)
            {
                await _tcpServer.CloseAsync();
                _tcpServer = null;
            }

            if (_tcpClient != null)
            {
                _tcpClient.DisableAutoReconnect();
                if (_tcpClient.IsConnected)
                {
                    _tcpClient.Disconnect(new DisconnectArgument(DisconnectionReasonCode.Active));
                }
                _tcpClient.Dispose();
                _tcpClient = null;
            }

            if (_p2pClient != null)
            {
                _p2pClient.DisableAutoReconnect();
                if (_p2pClient.IsConnected)
                {
                    _p2pClient.Disconnect(new DisconnectArgument(DisconnectionReasonCode.Active));
                }
                _p2pClient.Dispose();
                _p2pClient = null;
            }

            _activeClient = null;
            _remoteService = null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            StopAsync().GetAwaiter().GetResult();
            _initialConnectCts?.Dispose();
            _addon?.Dispose();
        }
    }
}
