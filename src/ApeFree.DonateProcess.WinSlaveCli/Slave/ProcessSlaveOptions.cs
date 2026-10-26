using System;
using System.Collections.Generic;

namespace ApeFree.DonateProcess.Slave
{
    /// <summary>
    /// Slave 节点运行配置项
    /// </summary>
    public class ProcessSlaveOptions
    {
        /// <summary>
        /// 是否使用 P2P 模式（Libp2p 穿透与去中心化传输）
        /// </summary>
        public bool UseP2P { get; set; } = false;

        /// <summary>
        /// Master 端主机地址（TCP 模式下有效，例如 "127.0.0.1"）
        /// </summary>
        public string TargetHost { get; set; } = "127.0.0.1";

        /// <summary>
        /// Master 端监听端口（TCP 模式下有效，默认 9822）
        /// </summary>
        public int TargetPort { get; set; } = 9822;

        /// <summary>
        /// 公网 P2P 中继/引导服务器地址（例如 "127.0.0.1:9832"）
        /// </summary>
        public string? P2PServerAddress { get; set; }

        /// <summary>
        /// 远端目标节点的 Multiaddr 字符串
        /// </summary>
        public string? P2PRemoteAddress { get; set; }

        /// <summary>
        /// 目标对端 PeerId
        /// </summary>
        public string? TargetPeerId { get; set; }

        /// <summary>
        /// 本地节点私钥派生种子
        /// </summary>
        public string? IdentitySeed { get; set; }

        /// <summary>
        /// 目标 Master 节点公钥派生种子
        /// </summary>
        public string? MasterSeed { get; set; }

        /// <summary>
        /// P2P 服务端种子
        /// </summary>
        public string? ServerSeed { get; set; }

        /// <summary>
        /// P2P 服务端固定 PeerId
        /// </summary>
        public string? ServerPeerId { get; set; }

        /// <summary>
        /// 附加引导节点列表
        /// </summary>
        public List<string> BootstrapPeers { get; set; } = new List<string>();

        /// <summary>
        /// 是否开启中继能力
        /// </summary>
        public bool EnableRelay { get; set; } = true;

        /// <summary>
        /// 是否开启分布式哈希表 (Kademlia DHT)
        /// </summary>
        public bool EnableDht { get; set; } = true;

        /// <summary>
        /// 是否开启局域网组播发现 (mDNS)
        /// </summary>
        public bool EnableMdns { get; set; } = true;

        /// <summary>
        /// 是否启用 QUIC 传输
        /// </summary>
        public bool EnableQuic { get; set; } = false;

        /// <summary>
        /// 节点显示别名
        /// </summary>
        public string? NodeName { get; set; }
    }
}
