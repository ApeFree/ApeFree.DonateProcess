using ApeFree.DonateProcess.Contracts;
using ApeFree.DonateProcess.Core;
using System;
using Xunit;

namespace ApeFree.DonateProcess.Tests
{
    public class PowerShellExecutionTests : IDisposable
    {
        private readonly LocalProcessService _service;

        public PowerShellExecutionTests()
        {
            _service = new LocalProcessService();
        }

        [Fact]
        public void ExecutePowerShellScript_BasicScript_ShouldReturnSuccessAndOutput()
        {
            var options = new ScriptExecutionOptionsDto
            {
                ScriptText = "Write-Output 'Hello from ApeFree PowerShell'",
                ExecutionPolicy = "Bypass",
                TimeoutMs = 15000
            };

            var result = _service.ExecutePowerShellScript(options);

            Assert.True(result.Success);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Hello from ApeFree PowerShell", result.StandardOutput);
            Assert.True(result.DurationMs >= 0);
        }

        [Fact]
        public void ExecutePowerShellScript_MultilineScriptWithQuotes_ShouldExecuteWithoutEscapingError()
        {
            var script = @"
$text = ""Special chars: 'quotes' & symbols < > | @ # $""
$count = 3
for ($i = 1; $i -le $count; $i++) {
    Write-Output ""Line $i : $text""
}
";
            var options = new ScriptExecutionOptionsDto
            {
                ScriptText = script,
                ExecutionPolicy = "Bypass",
                TimeoutMs = 20000
            };

            var result = _service.ExecutePowerShellScript(options);

            Assert.True(result.Success);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Line 1 : Special chars: 'quotes'", result.StandardOutput);
            Assert.Contains("Line 3 : Special chars: 'quotes'", result.StandardOutput);
        }

        [Fact]
        public void ExecutePowerShellScript_ErrorScript_ShouldCaptureError()
        {
            var options = new ScriptExecutionOptionsDto
            {
                ScriptText = "Write-Error 'Test deliberate error'; exit 42",
                TimeoutMs = 15000
            };

            var result = _service.ExecutePowerShellScript(options);

            Assert.False(result.Success);
            Assert.Equal(42, result.ExitCode);
            Assert.Contains("Test deliberate error", result.StandardError);
        }

        public void Dispose()
        {
            _service.Dispose();
        }
    }
}
