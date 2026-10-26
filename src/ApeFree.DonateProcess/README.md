# ApeFree.DonateProcess

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![NuGet](https://img.shields.io/nuget/v/ApeFree.DonateProcess.svg)](https://www.nuget.org/packages/ApeFree.DonateProcess)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)

## 1. 项目简介 (Introduction)

**ApeFree.DonateProcess** 是 ApeFree 组织旗下专为进程生命周期管控、自动化脚本调度与交互式命令行会话打造的纯契约与核心开发库。

通过标准化的 RPC 契约定义与高性能本地实现，本库不仅支持完整的单机进程管理（进程查询、启动、优雅关闭、强杀进程树、自动重启、挂起/恢复、PowerShell 脚本无转义执行），更与 `ApeFree.ApeRpc`、`STTech.BytesIO` 无缝深度集成，使客户端可以跨越网络边界，像调用本地 API 一样对远端 Slave 节点的进程执行全生命周期管理与多轮连续对话交互。

---

## 2. 🌟 核心特性 (Key Features)

* **全能进程生命周期控制**：
  * 支持按名称、PID、窗口句柄、内存、CPU 等维度过滤与排序查询进程列表；
  * 提供进程优雅关闭（`CloseMainWindow`）与强制级联终止进程树（`KillEntireProcessTree`）；
  * 智能进程重启：自动捕获原进程文件路径与启动参数并完成无缝重启；
  * 支持 Win32 进程挂起（`NtSuspendProcess`）与恢复（`NtResumeProcess`）。
* **💬 连续多轮对话的交互式 CMD / PowerShell**：
  * 后台持久化交互式 Shell 进程，支持实时输出流推送到前端（`SessionOutputReceived` 事件）；
  * 提供 `ExecuteSessionCommandAndWait` 同步多轮对话执行模型，自动探测指令执行完成标记与退出码；
  * 支持发送原始用户输入（如确认选项 `[Y/N]`），支持多会话并发管理与超时自动回收。
* **⚡ PowerShell 脚本安全无损执行**：
  * 采用 UTF-16LE Base64 编码命令机制（`-EncodedCommand`），彻底杜绝多行脚本、单双引号、换行符及管道符号的转义错乱；
  * 完整捕获标准输出、标准错误流、ExitCode 退出码及毫秒级耗时。
* **🌐 双向 RPC 远程就绪**：
  * 核心接口 `IProcessOperationsService` 原生兼容 `ApeFree.ApeRpc`，支持 TCP 直连与 Libp2p P2P 中继网络反向调用，NAT 穿透无阻碍。

---

## 3. 📦 安装方式 (Installation)

通过 .NET CLI 安装核心包：

```bash
dotnet add package ApeFree.DonateProcess
```

或在项目文件 `.csproj` 中添加引用：

```xml
<PackageReference Include="ApeFree.DonateProcess" Version="0.1.0" />
```

---

## 4. 🚀 快速上手 (Quick Start)

### 4.1 本地单机直接使用

```csharp
using ApeFree.DonateProcess.Contracts;
using ApeFree.DonateProcess.Core;

// 1. 初始化本地进程服务
using var processService = new LocalProcessService();

// 2. 查询占用内存前 5 的进程
var topProcesses = processService.GetProcesses(new ProcessFilterDto
{
    Top = 5,
    SortBy = "Memory",
    Descending = true
});

foreach (var p in topProcesses)
{
    Console.WriteLine($"[PID: {p.Id}] {p.ProcessName} - 内存: {p.WorkingSet64 / 1024 / 1024} MB");
}

// 3. 执行 PowerShell 脚本
var psResult = processService.ExecutePowerShellScript(new ScriptExecutionOptionsDto
{
    ScriptText = "Get-Date -Format 'yyyy-MM-dd HH:mm:ss'; Write-Output 'Hello from ApeFree!'"
});
Console.WriteLine($"PowerShell 输出: {psResult.StandardOutput.Trim()}");

// 4. 连续多轮交互式 CMD 会话
var session = processService.StartInteractiveSession(ShellType.Cmd);

// 第一轮：切换或查看目录
var round1 = processService.ExecuteSessionCommandAndWait(session.SessionId, "cd");
Console.WriteLine($"当前工作路径: {round1.Output.Trim()}");

// 第二轮：连续定义变量并回显
processService.ExecuteSessionCommandAndWait(session.SessionId, "set APE_VAR=ApeFree123");
var round2 = processService.ExecuteSessionCommandAndWait(session.SessionId, "echo %APE_VAR%");
Console.WriteLine($"会话状态保留变量: {round2.Output.Trim()}");

// 销毁会话
processService.CloseInteractiveSession(session.SessionId);
```

### 4.2 远程 RPC 场景使用

在 Slave 端启动服务并注册到 `RpcTerminal`，Master 端即可直接通过代理透明调度：

```csharp
// Master 端获取远端代理实例
IProcessOperationsService remoteProcess = rpcTerminalAddon.QueryService<IProcessOperationsService>();

// 远程获取 Slave 机器进程列表
var remoteList = remoteProcess.GetProcesses(new ProcessFilterDto { NameFilter = "chrome" });

// 远程执行连续交互式 CMD
var remoteSession = remoteProcess.StartInteractiveSession(ShellType.Cmd);
var result = remoteProcess.ExecuteSessionCommandAndWait(remoteSession.SessionId, "dir C:\\");
Console.WriteLine(result.Output);
```

---

## 5. 🧩 扩展生态 / 关联子包

| 项目 | 说明 |
| :--- | :--- |
| **`ApeFree.DonateProcess`** | 核心契约、DTO 模型与本地进程/CMD/PowerShell 执行引擎 |
| **`ApeFree.DonateProcess.WinSlaveCli`** | 独立轻量级 Slave 客户端工具（将本机进程执行能力分享给远端） |
| **`ApeFree.DonateProcess.WinCli`** | 综合控制台终端（集成 Master 管理控制、Slave 节点及 P2P 中继服务端） |

---

## 6. 📄 开源许可证 (License)

本项目遵循 [Apache-2.0](LICENSE) 开源许可协议。
