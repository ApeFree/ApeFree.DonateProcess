using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ApeFree.DonateProcess.Common
{
    /// <summary>
    /// P2P 网络地址与身份凭据转换工具类
    /// </summary>
    public static class P2PAddressHelper
    {
        /// <summary>
        /// 默认预设的 P2P 引导与中继服务端凭据种子
        /// </summary>
        public const string DefaultServerSeed = "ApeFree.DonateProcess.P2PServer.DefaultSeed";

        /// <summary>
        /// 根据指定的种子字符串派生计算出唯一的 Libp2p PeerId
        /// </summary>
        public static string? GetPeerIdFromSeed(string? seed)
        {
            var seedBytes = ParseSeed(seed);
            if (seedBytes == null) return null;
            return new Nethermind.Libp2p.Core.Identity(seedBytes).PeerId.ToString();
        }

        /// <summary>
        /// 解析 P2P 服务端 Multiaddr 地址，确保包含合法的 PeerId
        /// </summary>
        public static string ResolveServerMultiaddr(string serverAddress, string? serverPeerId = null, string? serverSeed = null)
        {
            if (string.IsNullOrWhiteSpace(serverAddress))
            {
                throw new ArgumentException("P2P 服务端地址不能为空", nameof(serverAddress));
            }

            if (serverAddress.Contains("/p2p/"))
            {
                return ToMultiaddr(serverAddress);
            }

            if (!string.IsNullOrWhiteSpace(serverPeerId))
            {
                return ToMultiaddr(serverAddress, peerId: serverPeerId);
            }

            if (!string.IsNullOrWhiteSpace(serverSeed))
            {
                var derivedPeerId = GetPeerIdFromSeed(serverSeed);
                return ToMultiaddr(serverAddress, peerId: derivedPeerId);
            }

            var defaultPeerId = GetPeerIdFromSeed(DefaultServerSeed);
            return ToMultiaddr(serverAddress, peerId: defaultPeerId);
        }

        /// <summary>
        /// 将常见的 IP/域名/端口格式转换为 Libp2p 标准 Multiaddr 格式
        /// </summary>
        public static string ToMultiaddr(string address, string? peerId = null, int defaultPort = 9832)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("地址不能为空", nameof(address));
            }

            var trimmed = address.Trim();

            string? embeddedPeerId = null;
            var p2pIndex = trimmed.IndexOf("/p2p/", StringComparison.OrdinalIgnoreCase);
            if (p2pIndex >= 0)
            {
                embeddedPeerId = trimmed.Substring(p2pIndex + 5).Trim();
                trimmed = trimmed.Substring(0, p2pIndex).TrimEnd('/');
            }

            var effectivePeerId = !string.IsNullOrWhiteSpace(peerId) ? peerId.Trim() : embeddedPeerId;
            if (effectivePeerId != null && effectivePeerId.StartsWith("/p2p/"))
            {
                effectivePeerId = effectivePeerId.Substring(5).Trim();
            }

            string baseAddr;
            if (trimmed.StartsWith("/"))
            {
                baseAddr = trimmed;
            }
            else
            {
                string host;
                int port = defaultPort;

                if (trimmed.StartsWith("[") && trimmed.Contains("]"))
                {
                    var closeIndex = trimmed.IndexOf(']');
                    host = trimmed.Substring(1, closeIndex - 1);
                    var afterClose = trimmed.Substring(closeIndex + 1);
                    if (afterClose.StartsWith(":") && int.TryParse(afterClose.Substring(1), out var parsedPort))
                    {
                        port = parsedPort;
                    }
                }
                else if (trimmed.Contains(":"))
                {
                    var parts = trimmed.Split(':');
                    host = parts[0];
                    if (int.TryParse(parts[1], out var parsedPort))
                    {
                        port = parsedPort;
                    }
                }
                else
                {
                    host = trimmed;
                }

                if (IPAddress.TryParse(host, out var ip))
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        baseAddr = $"/ip4/{ip}/tcp/{port}";
                    }
                    else
                    {
                        baseAddr = $"/ip6/{ip}/tcp/{port}";
                    }
                }
                else
                {
                    baseAddr = $"/dns/{host}/tcp/{port}";
                }
            }

            if (!string.IsNullOrWhiteSpace(effectivePeerId))
            {
                return $"{baseAddr}/p2p/{effectivePeerId}";
            }

            return baseAddr;
        }

        /// <summary>
        /// 将字符串口令或十六进制私钥转换为 32 字节私钥种子
        /// </summary>
        public static byte[]? ParseSeed(string? seed)
        {
            if (string.IsNullOrWhiteSpace(seed)) return null;

            var trimmed = seed.Trim();
            if (trimmed.Length == 64 && IsHexString(trimmed))
            {
                var bytes = new byte[32];
                for (int i = 0; i < 32; i++)
                {
                    bytes[i] = Convert.ToByte(trimmed.Substring(i * 2, 2), 16);
                }
                return bytes;
            }

            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(trimmed));
        }

        private static bool IsHexString(string str)
        {
            foreach (var c in str)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
