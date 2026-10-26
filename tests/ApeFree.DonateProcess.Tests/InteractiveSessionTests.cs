using ApeFree.DonateProcess.Contracts;
using ApeFree.DonateProcess.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace ApeFree.DonateProcess.Tests
{
    public class InteractiveSessionTests : IDisposable
    {
        private readonly LocalProcessService _service;

        public InteractiveSessionTests()
        {
            _service = new LocalProcessService();
        }

        [Fact]
        public void CmdInteractiveSession_ContinuousMultiRoundDialogue_ShouldPreserveStateAcrossRounds()
        {
            // 1. 创建连续多轮交互式 CMD 会话
            var session = _service.StartInteractiveSession(ShellType.Cmd);
            Assert.NotNull(session);
            Assert.False(string.IsNullOrWhiteSpace(session.SessionId));
            Assert.True(session.ProcessId > 0);
            Assert.True(session.IsActive);

            try
            {
                // 第一轮对话：定义会话级环境变量
                var round1 = _service.ExecuteSessionCommandAndWait(session.SessionId, "set APE_SESSION_KEY=ApeFreeDynamicSession123", 10000);
                Assert.True(round1.Success, $"Round 1 failed: {round1.ErrorMessage}");

                // 第二轮对话：回显该环境变量，验证会话上下文跨轮次完整保留！
                var round2 = _service.ExecuteSessionCommandAndWait(session.SessionId, "echo Value=%APE_SESSION_KEY%", 10000);
                Assert.True(round2.Success, $"Round 2 failed: {round2.ErrorMessage}");
                Assert.Contains("Value=ApeFreeDynamicSession123", round2.Output);

                // 第三轮对话：执行目录查询
                var round3 = _service.ExecuteSessionCommandAndWait(session.SessionId, "echo CurrentDirectory=%CD%", 10000);
                Assert.True(round3.Success, $"Round 3 failed: {round3.ErrorMessage}");
                Assert.Contains("CurrentDirectory=", round3.Output);
            }
            finally
            {
                // 关闭并销毁会话
                var closed = _service.CloseInteractiveSession(session.SessionId);
                Assert.True(closed);
            }
        }

        [Fact]
        public void PowerShellInteractiveSession_ContinuousMultiRoundDialogue_ShouldPreserveVariables()
        {
            // 1. 创建连续多轮交互式 PowerShell 会话
            var session = _service.StartInteractiveSession(ShellType.PowerShell);
            Assert.NotNull(session);

            try
            {
                // 第一轮：计算并保存变量
                var round1 = _service.ExecuteSessionCommandAndWait(session.SessionId, "$numA = 100; $numB = 250", 15000);
                Assert.True(round1.Success, $"Round 1 failed: {round1.ErrorMessage}");

                // 第二轮：使用前一轮保存的变量进行计算输出
                var round2 = _service.ExecuteSessionCommandAndWait(session.SessionId, "$sum = $numA + $numB; Write-Output \"TotalSum=$sum\"", 15000);
                Assert.True(round2.Success, $"Round 2 failed: {round2.ErrorMessage}");
                Assert.Contains("TotalSum=350", round2.Output);
            }
            finally
            {
                _service.CloseInteractiveSession(session.SessionId);
            }
        }

        [Fact]
        public void CmdInteractiveSession_OutputStreamingAndBuffer_ShouldCaptureLines()
        {
            var session = _service.StartInteractiveSession(ShellType.Cmd);
            var capturedEvents = new List<SessionOutputDto>();

            _service.SessionOutputReceived += (s, e) =>
            {
                if (e.SessionId == session.SessionId)
                {
                    capturedEvents.Add(e);
                }
            };

            try
            {
                _service.ExecuteSessionCommandAndWait(session.SessionId, "echo StreamingTest123", 10000);

                // 验证流式事件
                Assert.NotEmpty(capturedEvents);
                Assert.Contains(capturedEvents, ev => ev.Text.Contains("StreamingTest123"));

                // 验证缓冲区拉取
                var buffer = _service.ReadSessionOutput(session.SessionId, 50);
                Assert.NotEmpty(buffer);
                Assert.Contains(buffer, line => line.Text.Contains("StreamingTest123"));
            }
            finally
            {
                _service.CloseInteractiveSession(session.SessionId);
            }
        }

        public void Dispose()
        {
            _service.Dispose();
        }
    }
}
