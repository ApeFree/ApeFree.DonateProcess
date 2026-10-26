using ApeFree.ApeRpc;
using ApeFree.ApeRpc.BytesIO;
using ApeFree.DonateProcess.Contracts;
using ApeFree.DonateProcess.Slave;
using STTech.BytesIO.Tcp;
using System;
using System.Threading.Tasks;
using Xunit;

namespace ApeFree.DonateProcess.Tests
{
    public class EndToEndRpcProcessTests
    {
        [Fact]
        public async Task EndToEndRpc_MasterToSlave_ShouldExecuteProcessesAndSessionsRemotely()
        {
            int testPort = 19826;

            // 1. 建立 Master 服务端
            using var server = new TcpServer { Host = "127.0.0.1", Port = testPort };
            var masterTerminal = new RpcTerminal(new TerminalInfo { Name = "MasterRpcTerminal" });
            using var masterAddon = new BytesRpcTerminalAddon(masterTerminal);
            masterAddon.BindServer(server);

            var clientConnectedTcs = new TaskCompletionSource<TcpClient>();
            server.ClientConnected += (s, e) =>
            {
                clientConnectedTcs.TrySetResult(e.Client);
            };

            await server.StartAsync();

            // 2. 启动 Slave 节点端
            var slaveOptions = new ProcessSlaveOptions
            {
                UseP2P = false,
                TargetHost = "127.0.0.1",
                TargetPort = testPort
            };

            using var slaveNode = new ProcessSlaveNode(slaveOptions);
            var slaveStarted = await slaveNode.StartAsync();
            Assert.True(slaveStarted, "Slave 连接 Master 失败。");

            // 等待 Master 端捕获到该接入的 Client 通道
            var connectedClient = await clientConnectedTcs.Task;
            Assert.NotNull(connectedClient);

            // 等待 RPC 初始化握手完成
            await Task.Delay(500);

            // 3. Master 端通过连接上的 Client 信道获取 Slave 暴露的进程服务代理
            var remoteProcessService = masterAddon.QueryService<IProcessOperationsService>(nameof(IProcessOperationsService), connectedClient);
            Assert.NotNull(remoteProcessService);

            // 4. 远程调用：获取远端进程列表
            var processes = remoteProcessService.GetProcesses(new ProcessFilterDto { Top = 5 });
            Assert.NotNull(processes);
            Assert.NotEmpty(processes);

            // 5. 远程调用：执行远端 PowerShell 脚本
            var psResult = remoteProcessService.ExecutePowerShellScript(new ScriptExecutionOptionsDto
            {
                ScriptText = "Write-Output 'Remote RPC PowerShell Execution Success!'",
                TimeoutMs = 15000
            });
            Assert.True(psResult.Success);
            Assert.Contains("Remote RPC PowerShell Execution Success!", psResult.StandardOutput);

            // 6. 远程调用：连续多轮对话的交互式 CMD 会话！
            var remoteSession = remoteProcessService.StartInteractiveSession(ShellType.Cmd);
            Assert.NotNull(remoteSession);

            try
            {
                // 第一轮：在远端会话中设置上下文变量
                var r1 = remoteProcessService.ExecuteSessionCommandAndWait(remoteSession.SessionId, "set REMOTE_TEST=ApeFreeRemoteRpcToken99", 10000);
                Assert.True(r1.Success);

                // 第二轮：在远端会话中验证上下文跨网络轮次保留
                var r2 = remoteProcessService.ExecuteSessionCommandAndWait(remoteSession.SessionId, "echo Value=%REMOTE_TEST%", 10000);
                Assert.True(r2.Success);
                Assert.Contains("Value=ApeFreeRemoteRpcToken99", r2.Output);
            }
            finally
            {
                remoteProcessService.CloseInteractiveSession(remoteSession.SessionId);
            }
        }
    }
}
