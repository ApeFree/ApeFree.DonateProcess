using ApeFree.DonateProcess.Contracts;
using ApeFree.DonateProcess.Core;
using System;
using System.Diagnostics;
using System.Threading;
using Xunit;

namespace ApeFree.DonateProcess.Tests
{
    public class ProcessLifecycleTests : IDisposable
    {
        private readonly LocalProcessService _service;

        public ProcessLifecycleTests()
        {
            _service = new LocalProcessService();
        }

        [Fact]
        public void StartProcess_WithWaitForExit_ShouldCaptureOutputAndExitCode()
        {
            var options = new ProcessStartOptionsDto
            {
                FileName = "cmd.exe",
                Arguments = "/c echo Hello_ApeFree_Process",
                WaitForExit = true,
                WaitTimeoutMs = 10000,
                CreateNoWindow = true
            };

            var result = _service.StartProcess(options);

            Assert.True(result.Success);
            Assert.Equal(0, result.ExitCode);
            Assert.NotNull(result.StandardOutput);
            Assert.Contains("Hello_ApeFree_Process", result.StandardOutput);
        }

        [Fact]
        public void StartAndKillProcess_ShouldTerminateSuccessfully()
        {
            // 启动一个持久运行的子进程
            var startOptions = new ProcessStartOptionsDto
            {
                FileName = "cmd.exe",
                Arguments = "/k",
                CreateNoWindow = true
            };

            var startResult = _service.StartProcess(startOptions);
            Assert.True(startResult.Success);
            Assert.True(startResult.ProcessId > 0);

            try
            {
                // 验证进程正在运行
                using var p = Process.GetProcessById(startResult.ProcessId);
                Assert.False(p.HasExited);

                // 执行终止
                var killResult = _service.KillProcess(new ProcessKillOptionsDto
                {
                    ProcessId = startResult.ProcessId,
                    Force = true,
                    KillEntireProcessTree = true
                });

                Assert.True(killResult.Success);
                Assert.Equal(1, killResult.AffectedCount);

                // 等待退出确认
                Thread.Sleep(300);
                Assert.Throws<ArgumentException>(() => Process.GetProcessById(startResult.ProcessId));
            }
            catch
            {
                try { Process.GetProcessById(startResult.ProcessId).Kill(true); } catch { }
                throw;
            }
        }

        [Fact]
        public void SuspendAndResumeProcess_ShouldSucceed()
        {
            var startResult = _service.StartProcess(new ProcessStartOptionsDto
            {
                FileName = "cmd.exe",
                Arguments = "/k",
                CreateNoWindow = true
            });

            Assert.True(startResult.Success);

            try
            {
                // 挂起
                var suspendRes = _service.SuspendProcess(startResult.ProcessId);
                Assert.True(suspendRes.Success);

                // 恢复
                var resumeRes = _service.ResumeProcess(startResult.ProcessId);
                Assert.True(resumeRes.Success);
            }
            finally
            {
                try { Process.GetProcessById(startResult.ProcessId).Kill(true); } catch { }
            }
        }

        public void Dispose()
        {
            _service.Dispose();
        }
    }
}
