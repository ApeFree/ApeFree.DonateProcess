using System;

namespace ApeFree.DonateProcess.Contracts
{
    /// <summary>
    /// Master 节点协同与控制服务契约。
    /// 由 Master 端实现并暴露，Slave 端在接入中继或直连网络后可主动通知 Master 节点就绪。
    /// </summary>
    public interface IMasterNodeService
    {
        /// <summary>
        /// 通知 Master 端：Slave 节点已完全就绪，可立即发起远程进程控制与交互调度
        /// </summary>
        void NotifySlaveReady();
    }
}
