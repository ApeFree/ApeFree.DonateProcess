using ApeFree.DonateProcess.Contracts;
using ApeFree.DonateProcess.Core;
using System;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace ApeFree.DonateProcess.Tests
{
    public class ProcessQueryTests : IDisposable
    {
        private readonly LocalProcessService _service;

        public ProcessQueryTests()
        {
            _service = new LocalProcessService();
        }

        [Fact]
        public void GetProcesses_ShouldReturnNonEmptyList()
        {
            var processes = _service.GetProcesses();

            Assert.NotNull(processes);
            Assert.NotEmpty(processes);
            Assert.Contains(processes, p => p.Id == Environment.ProcessId);
        }

        [Fact]
        public void GetProcessById_ShouldReturnCurrentProcessDetails()
        {
            var currentPid = Environment.ProcessId;
            var info = _service.GetProcessById(currentPid);

            Assert.NotNull(info);
            Assert.Equal(currentPid, info.Id);
            Assert.False(string.IsNullOrWhiteSpace(info.ProcessName));
            Assert.True(info.WorkingSet64 > 0);
        }

        [Fact]
        public void GetProcesses_WithFilter_ShouldFilterCorrectly()
        {
            var currentPid = Environment.ProcessId;
            using var current = Process.GetCurrentProcess();
            var name = current.ProcessName;

            var filter = new ProcessFilterDto
            {
                NameFilter = name,
                Top = 10,
                SortBy = "Memory",
                Descending = true
            };

            var results = _service.GetProcesses(filter);

            Assert.NotNull(results);
            Assert.NotEmpty(results);
            Assert.All(results, p => Assert.Contains(name, p.ProcessName, StringComparison.OrdinalIgnoreCase));
            Assert.True(results.Count <= 10);
        }

        [Fact]
        public void GetSystemPerformance_ShouldReturnValidMetrics()
        {
            var perf = _service.GetSystemPerformance();

            Assert.NotNull(perf);
            Assert.True(perf.TotalPhysicalMemoryBytes > 0);
            Assert.True(perf.AvailablePhysicalMemoryBytes > 0);
            Assert.True(perf.ProcessCount > 0);
            Assert.False(string.IsNullOrWhiteSpace(perf.MachineName));
        }

        public void Dispose()
        {
            _service.Dispose();
        }
    }
}
