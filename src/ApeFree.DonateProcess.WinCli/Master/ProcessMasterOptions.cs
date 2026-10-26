using System;
using System.Collections.Generic;

namespace ApeFree.DonateProcess.Master
{
    /// <summary>
    /// Master 节点运行配置项
    /// </summary>
    public class ProcessMasterOptions
    {
        /// <summary>
        /// 是否使用 P2P 模式（Libp2p 去中心化中继穿透）
        /// </summary>
        public bool UseP2P { get; set; } = false;

        /// <summary>
        /// TCP 监听端口（TCP 模式下有效，默认 9822）
        /// </summary>
        public int ListenPort { get; set; } = 9822;

        /// <summary>
        /// TCP 监听地址（TCP 模式下有效，默认 "0.0.0.0"）
        /// </summary>
        public string ListenHost { get; set; } = "0.0.0.0";

        /// <summary>
        /// 公网 P2P 中继/引导服务器地址（例如 "127.0.0.1:9832"）
        /// </summary>
        public string? P2PServerAddress { get; set; }

        /// <summary>
        /// Master 节点自身的身份种子
        /// </summary>
        public string? MasterSeed { get; set; } = "ApeFree.DonateProcess.Master.DefaultSeed";

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
    }
}
