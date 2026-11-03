using ApeFree.DonateProcess.Contracts;
using ApeFree.DonateProcess.Master;
using ApeFree.DonateProcess.Slave;
using ApeFree.DonateProcess.WinCli.Common;
using ApeFree.PyramidMenu.Menus;
using ApeFree.PyramidMenu.Scenes;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ApeFree.DonateProcess.Tests
{
    public class ProcessNetworkRoleDecouplingTests
    {
        /// <summary>
        /// 测试场景 1：正向直连（Slave 作为服务端监听端口，Master 作为客户端主动拨号连入并执行进程指令与多轮交互会话）
        /// </summary>
        [Fact]
        public async Task TestForwardConnect_SlaveListen_MasterConnect_ShouldSucceed()
        {
            int testPort = 19830;

            // 1. 启动 Slave 服务端监听
            var slaveOptions = new ProcessSlaveOptions
            {
                TransportMode = NetworkTransportMode.Listen,
                ListenHost = "127.0.0.1",
                ListenPort = testPort
            };

            using var slaveNode = new ProcessSlaveNode(slaveOptions);
            var slaveStarted = await slaveNode.StartAsync();
            Assert.True(slaveStarted, "Slave 监听端启动失败。");
            Assert.True(slaveNode.IsListening, "Slave 应该处于监听状态。");

            // 2. 启动 Master 客户端主动连入
            var masterOptions = new ProcessMasterOptions
            {
                TransportMode = NetworkTransportMode.Connect,
                TargetHost = "127.0.0.1",
                TargetPort = testPort
            };

            using var masterNode = new ProcessMasterNode(masterOptions);
            await masterNode.StartAsync();

            // 等待连接与 RPC 代理就绪
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!masterNode.HasActiveSlave && !cts.IsCancellationRequested)
            {
                await Task.Delay(200, cts.Token);
            }

            Assert.True(masterNode.HasActiveSlave, "Master 应当已成功连入 Slave 服务端并获取到代理服务。");
            Assert.NotNull(masterNode.RemoteService);

            // 3. 执行远程进程查询
            var processes = masterNode.RemoteService!.GetProcesses(new ProcessFilterDto { Top = 5 });
            Assert.NotNull(processes);
            Assert.NotEmpty(processes);

            // 4. 执行远程 PowerShell 脚本
            var psResult = masterNode.RemoteService.ExecutePowerShellScript(new ScriptExecutionOptionsDto
            {
                ScriptText = "Write-Output 'ProcessForwardConnect_Success'",
                TimeoutMs = 15000
            });
            Assert.True(psResult.Success);
            Assert.Contains("ProcessForwardConnect_Success", psResult.StandardOutput);

            // 5. 交互式多轮会话测试
            var session = masterNode.RemoteService.StartInteractiveSession(ShellType.Cmd);
            Assert.NotNull(session);
            try
            {
                var r1 = masterNode.RemoteService.ExecuteSessionCommandAndWait(session.SessionId, "set FORWARD_TEST_KEY=Decoupled42", 10000);
                Assert.True(r1.Success);

                var r2 = masterNode.RemoteService.ExecuteSessionCommandAndWait(session.SessionId, "echo [%FORWARD_TEST_KEY%]", 10000);
                Assert.True(r2.Success);
                Assert.Contains("[Decoupled42]", r2.Output);
            }
            finally
            {
                masterNode.RemoteService.CloseInteractiveSession(session.SessionId);
            }
        }

        /// <summary>
        /// 测试场景 2：多 Master 并发连接同一个 Slave 服务端，并验证会话独立与断开计数
        /// </summary>
        [Fact]
        public async Task TestMultiMasterConcurrent_SlaveListen_ShouldServeMultipleClients()
        {
            int testPort = 19831;

            var slaveOptions = new ProcessSlaveOptions
            {
                TransportMode = NetworkTransportMode.Listen,
                ListenHost = "127.0.0.1",
                ListenPort = testPort
            };

            using var slaveNode = new ProcessSlaveNode(slaveOptions);
            await slaveNode.StartAsync();

            // 启动 Master 1
            var master1Options = new ProcessMasterOptions
            {
                TransportMode = NetworkTransportMode.Connect,
                TargetHost = "127.0.0.1",
                TargetPort = testPort
            };
            using var master1 = new ProcessMasterNode(master1Options);
            await master1.StartAsync();

            // 启动 Master 2
            var master2Options = new ProcessMasterOptions
            {
                TransportMode = NetworkTransportMode.Connect,
                TargetHost = "127.0.0.1",
                TargetPort = testPort
            };
            using var master2 = new ProcessMasterNode(master2Options);
            await master2.StartAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while ((!master1.HasActiveSlave || !master2.HasActiveSlave) && !cts.IsCancellationRequested)
            {
                await Task.Delay(200, cts.Token);
            }

            Assert.True(master1.HasActiveSlave);
            Assert.True(master2.HasActiveSlave);
            Assert.Equal(2, slaveNode.ConnectedClientCount);

            // Master 1 和 Master 2 分别独立启动一个交互终端
            var session1 = master1.RemoteService!.StartInteractiveSession(ShellType.Cmd);
            var session2 = master2.RemoteService!.StartInteractiveSession(ShellType.Cmd);

            Assert.NotEqual(session1.SessionId, session2.SessionId);

            var res1 = master1.RemoteService!.ExecuteSessionCommandAndWait(session1.SessionId, "echo M1_OUTPUT", 10000);
            var res2 = master2.RemoteService!.ExecuteSessionCommandAndWait(session2.SessionId, "echo M2_OUTPUT", 10000);

            Assert.Contains("M1_OUTPUT", res1.Output);
            Assert.Contains("M2_OUTPUT", res2.Output);

            master1.RemoteService!.CloseInteractiveSession(session1.SessionId);
            master2.RemoteService!.CloseInteractiveSession(session2.SessionId);

            // 断开 Master 1
            await master1.StopAsync();
            await Task.Delay(300);

            // Slave 端的已连接 Master 计数应降为 1
            Assert.Equal(1, slaveNode.ConnectedClientCount);

            // Master 2 依然能正常工作
            var res2After = master2.RemoteService.GetProcesses(new ProcessFilterDto { Top = 1 });
            Assert.NotNull(res2After);
            Assert.NotEmpty(res2After);
        }

        /// <summary>
        /// 测试场景 3：基于 PyramidMenu 的分层向导状态机导航与选项生成
        /// </summary>
        [Fact]
        public void TestPyramidMenuWizard_MasterConnectOption_ShouldProduceValidOptions()
        {
            var rootMenu = new DonateProcessMenuWizard.WizardRootMenu();
            var scene = new Scene(rootMenu);

            // 1. 根菜单选择 [1] Master 控制端
            var r1 = scene.Input(new Packet("1"));
            Assert.True(r1.IsSuccessful);

            // 2. 选择 [2] 主动拨号连接 (Connect)
            var r2 = scene.Input(new Packet("2"));
            Assert.True(r2.IsSuccessful);

            // 3. 一键极速启动 [1]
            var r3 = scene.Input(new Packet("1"));
            Assert.True(r3.IsSuccessful);

            Assert.True(rootMenu.IsFinished);
            Assert.NotNull(rootMenu.FinalResult);
            Assert.Equal(WizardRunMode.Master, rootMenu.FinalResult!.Mode);
            Assert.NotNull(rootMenu.FinalResult.MasterOptions);
            Assert.Equal(NetworkTransportMode.Connect, rootMenu.FinalResult.MasterOptions!.TransportMode);
        }

        [Fact]
        public void TestPyramidMenuWizard_SlaveListenOption_ShouldProduceValidOptions()
        {
            var rootMenu = new DonateProcessMenuWizard.WizardRootMenu();
            var scene = new Scene(rootMenu);

            // 1. 根菜单选择 [2] Slave 受控分享节点
            var r1 = scene.Input(new Packet("2"));
            Assert.True(r1.IsSuccessful);

            // 2. 选择 [2] 被动监听等待 (Listen)
            var r2 = scene.Input(new Packet("2"));
            Assert.True(r2.IsSuccessful);

            // 3. 一键极速启动 [1]
            var r3 = scene.Input(new Packet("1"));
            Assert.True(r3.IsSuccessful);

            Assert.True(rootMenu.IsFinished);
            Assert.NotNull(rootMenu.FinalResult);
            Assert.Equal(WizardRunMode.Slave, rootMenu.FinalResult!.Mode);
            Assert.NotNull(rootMenu.FinalResult.SlaveOptions);
            Assert.Equal(NetworkTransportMode.Listen, rootMenu.FinalResult.SlaveOptions!.TransportMode);
        }

        [Fact]
        public void TestPyramidMenuWizard_LocalOption_ShouldProduceValidOptions()
        {
            var rootMenu = new DonateProcessMenuWizard.WizardRootMenu();
            var scene = new Scene(rootMenu);

            // 根菜单选择 [4] 本地单机控制台
            var r = scene.Input(new Packet("4"));
            Assert.True(r.IsSuccessful);

            Assert.True(rootMenu.IsFinished);
            Assert.NotNull(rootMenu.FinalResult);
            Assert.Equal(WizardRunMode.Local, rootMenu.FinalResult!.Mode);
        }
    }
}
