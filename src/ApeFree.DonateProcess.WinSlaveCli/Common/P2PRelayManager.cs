using STTech.BytesIO.Core;
using STTech.BytesIO.P2P;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ApeFree.DonateProcess.Common
{
    /// <summary>
    /// P2P 引导与中继服务端对等节点中继管理器
    /// </summary>
    public class P2PRelayManager : IDisposable
    {
        private readonly object _lock = new object();
        private readonly Dictionary<PeerClient, EventHandler<DataReceivedEventArgs>> _clients = new Dictionary<PeerClient, EventHandler<DataReceivedEventArgs>>();
        private bool _isDisposed;

        /// <summary>
        /// 当中继通道发生变更（节点加入或离开）时触发
        /// </summary>
        public event EventHandler<int>? ActivePeerCountChanged;

        /// <summary>
        /// 获取当前活动的中继对等节点数量
        /// </summary>
        public int ActivePeerCount
        {
            get
            {
                lock (_lock)
                {
                    return _clients.Count;
                }
            }
        }

        /// <summary>
        /// 注册一个接入的对等节点并挂载中继转发监听
        /// </summary>
        public void RegisterClient(PeerClient client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            lock (_lock)
            {
                if (_isDisposed) return;

                if (_clients.TryGetValue(client, out var existingHandler))
                {
                    client.OnDataReceived -= existingHandler;
                    _clients.Remove(client);
                }

                EventHandler<DataReceivedEventArgs> handler = (sender, args) =>
                {
                    if (args?.Data == null || args.Data.Memory.Length == 0) return;
                    var payload = args.Data.Memory.ToArray();

                    List<PeerClient> targets;
                    lock (_lock)
                    {
                        if (_isDisposed) return;
                        targets = _clients.Keys.Where(c => c != client && c.IsConnected).ToList();
                    }

                    foreach (var target in targets)
                    {
                        try
                        {
                            target.Send(payload);
                        }
                        catch
                        {
                        }
                    }
                };

                client.OnDataReceived += handler;
                _clients[client] = handler;
            }

            ActivePeerCountChanged?.Invoke(this, ActivePeerCount);
        }

        /// <summary>
        /// 注销断开的对等节点并安全解除中继转发监听
        /// </summary>
        public void UnregisterClient(PeerClient client)
        {
            if (client == null) return;

            lock (_lock)
            {
                if (_clients.TryGetValue(client, out var handler))
                {
                    client.OnDataReceived -= handler;
                    _clients.Remove(client);
                }
            }

            ActivePeerCountChanged?.Invoke(this, ActivePeerCount);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                foreach (var kvp in _clients)
                {
                    kvp.Key.OnDataReceived -= kvp.Value;
                }
                _clients.Clear();
            }
        }
    }
}
