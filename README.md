# ApeFree.DonateProcess

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)

## 1. 项目简介 (Introduction)

**ApeFree.DonateProcess** 是 ApeFree 组织旗下专为**主动式远程进程生命周期管理、脚本自动化调度及连续多轮命令行交互会话**打造的跨平台/Windows 开源系统。

在自动化运维、跨设备集群调度及开发测试场景中，我们经常需要在局域网甚至跨多层内网 NAT 环境下远程查看目标机器的进程、终止或重启无响应服务、执行 PowerShell 脚本，或者进入一个**能连续维持上下文状态与环境变量的多轮对话交互式 CMD / PowerShell 终端**。传统方案（如 SSH、WinRM、远程桌面）往往需要配置繁重的服务端凭据、开放防火墙端口或面临公网 IP 限制。

本项目参考 **ApeFree.DonateDisk** 架构，提供基于 `ApeFree.ApeRpc`、`STTech.BytesIO` 与 `ApeFree.PyramidMenu` 的**角色与网络解耦、全双工 RPC 与 Libp2p P2P 中继穿透机制**：
1. **正向直连模式 (正向受控服务)**：受控 Slave 作为服务端开放端口监听（**支持多个 Master 客户端并发连入独立控制**），Master 端主动连接目标 Slave 机器开展调度；
2. **反向受控模式 (传统反向穿透)**：Master 作为服务端监听端口，位于 NAT/内网的受控 Slave 机器主动拨号连入 Master；
3. **P2P 去中心化中继模式 (Libp2p Circuit Relay)**：Master 与 Slave **双方均处于不同内网 NAT / 防火墙**之后，双方均无需公网 IP 和端口映射，只需**连入指定的 P2P 服务端**，即可自动建立端到端 RPC 通道；
4. **PyramidMenu 交互式向导**：支持无参数启动分层菜单向导（智能默认预设，回车一键极速秒启）；
5. **本地单机控制台模式**：无需任何网络配置，直接作为本机功能完备的进程管理器与连续多轮 CMD/PowerShell 调度台使用。

---

## 2. 🌟 核心特性 (Key Features)

* **全能进程生命周期控制**：
  * 支持进程列表检索、模糊匹配、主窗口过滤、PID 精确查询；
  * 支持物理内存（WorkingSet）、峰值内存、虚拟内存、CPU 累计时间等多维度指标并进行降序/升序排序；
  * 提供进程优雅关闭（`CloseMainWindow`）与强制级联终止进程树（`KillEntireProcessTree`）；
  * 智能进程重启：自动从系统捕获原进程完整路径与运行参数并重新启动；
  * 原生 Win32 进程挂起（`NtSuspendProcess`）与恢复（`NtResumeProcess`）。
* **💬 连续多轮对话的交互式 CMD / PowerShell**：
  * 后台持久化交互式 Shell 进程，多轮对话之间**完整保持工作目录、局部环境变量及执行状态**；
  * 支持流式输出实时推送到远端（`SessionOutputReceived` 事件）；
  * 内置同步探测等待模型（`ExecuteSessionCommandAndWait`），精确捕获每轮命令的执行输出、错误与退出码；
  * 支持写入原始用户交互字符（如响应 `[Y/N]` 确认提示）；
  * 支持多会话并发管理与 30 分钟无交互超时自动安全回收。
* **⚡ PowerShell 脚本安全无损执行**：
  * 采用 UTF-16LE Base64 编码命令机制（`-EncodedCommand`），彻底避免多行脚本、单双引号、换行符及管道符号的转义错乱；
  * 完整捕获标准输出、标准错误流、ExitCode 退出码及毫秒级耗时。
* **🌐 网络与角色彻底解耦**：
  * Master 与 Slave 均支持主动连出（Connect）与本地监听（Listen）；
  * 无论哪端监听、哪端连入，连接建立后两端地位对等，Master 负责调度，Slave 负责执行；
  * Slave 监听模式下支持多 Master 并发接入独立交互控制。
* **📦 单文件修剪独立程序**：
  * `ApeFree.DonateProcess.WinCli` 与 `WinSlaveCli` 均支持单文件修剪发布（Self-Contained Single-File Trimmed），轻量免安装，即开即用。

---

## 3. 架构拓扑 (Architecture)

### 3.1 正向直连与反向受控 2x2 灵活拓扑 (网络与角色解耦)
```
【拓扑 A：正向直连模式 (推荐：目标受控端开放端口，支持多 Master 并发控制)】
Master 1 (Client 主动连出) ──┐
                             ├────[TCP 直连]────> Slave 共享节点 (TcpServer 监听 9822)
Master 2 (Client 主动连出) ──┘                    (发布本机进程操作与多轮终端服务)

【拓扑 B：反向受控模式 (目标受控端位于复杂内网，主动向 Master 反弹连接)】
Master 控制端 (TcpServer 监听 9822) <──────[TCP 连入]────── Slave 共享端 (Client 连出)
```

### 3.2 P2P 去中心化中继模式 (双方均位于 NAT 内部)
```
                    ┌────────────────────────────┐
                    │   公网 P2P Server / Relay  │
                    │   (STTech.BytesIO.P2P)     │
                    └───────▲────────────▲───────┘
          主动连接 (Multiaddr)│            │主动连接 (Multiaddr + Circuit)
                            │            │
  ┌─────────────────────────┴──┐       ┌─┴──────────────────────────┐
  │   Master 端 (NAT 内部)     │       │    Slave 端 (NAT 内部)     │
  │   远程进程控制与交互控制台 │ <─────>│   本地进程执行与多轮会话服务│
  └────────────────────────────┘ ApeRpc └────────────────────────────┘
```

---

## 4. 工程组成 (Projects)

| 工程 | 说明 | 运行形态 |
| :--- | :--- | :--- |
| **`ApeFree.DonateProcess`** | 核心类库，定义 RPC 接口契约、传输 DTO、多轮会话管理与本地执行引擎 | 跨平台类库 (`net10.0`) |
| **`ApeFree.DonateProcess.WinSlaveCli`** | 独立轻量级 Slave 客户端工具（将本机进程执行能力分享给远端） | 单文件修剪控制台 EXE (`net10.0-windows`) |
| **`ApeFree.DonateProcess.WinCli`** | 综合命令行终端，集成 Master 管理端、Slave 节点、P2P 服务端及本地控制台 | 单文件修剪控制台 EXE (`net10.0-windows`) |
| **`ApeFree.DonateProcess.Tests`** | 完整单元与端到端网络集成测试套件 | xUnit 测试工程 (`net10.0-windows`) |

---

## 5. 快速上手 (Quick Start)

### 5.1 零参数交互式向导 (PyramidMenu)
直接双击或在命令行运行程序（不带参数）：
```bash
ApeFree.DonateProcess.WinCli.exe
```
程序将拉起基于 `ApeFree.PyramidMenu` 的分层向导，按提示输入序号或直接敲回车即可秒级秒启所需服务！

---

### 5.2 方案 A：正向直连模式 (受控端监听，支持多客户端)
#### 第一步：受控端开放端口监听 (正向服务)
```bash
ApeFree.DonateProcess.WinSlaveCli.exe -l -p 9822
```

#### 第二步：控制端主动连入
```bash
ApeFree.DonateProcess.WinCli.exe -h 192.168.1.100 -p 9822
```

---

### 5.3 方案 B：反向受控模式 (受控端位于内网反弹连接)
#### 第一步：控制端开放端口监听等待反弹
```bash
ApeFree.DonateProcess.WinCli.exe -m -l -p 9822
```

#### 第二步：受控端主动连向控制端
```bash
ApeFree.DonateProcess.WinSlaveCli.exe -h 192.168.1.50 -p 9822
```

---

### 5.4 方案 C：P2P 中继穿透模式 (跨内网 NAT 穿透)
#### 第一步：启动公网 P2P 引导与中继服务端 (Relay Server)
```bash
ApeFree.DonateProcess.WinCli.exe --p2p-server -p 9832
```

#### 第二步：启动 Master 端 (远程管理控制台)
```bash
ApeFree.DonateProcess.WinCli.exe -m -p2p --server "127.0.0.1:9832"
```

#### 第三步：启动 Slave 端 (本地进程共享节点)
```bash
ApeFree.DonateProcess.WinSlaveCli.exe -p2p --server "127.0.0.1:9832"
```

---

### 5.5 方案 D：本地单机控制台模式 (无需网络)
```bash
ApeFree.DonateProcess.WinCli.exe --local
```

---

## 6. 交互式控制台命令速查

在 Master 或本地控制台中，可随时输入以下指令：

| 指令 | 说明 | 示例 |
| :--- | :--- | :--- |
| `list [关键字] [条数]` | 列出进程列表（按内存占用降序排列） | `list chrome 10` |
| `start <程序> [参数]` | 启动新进程 | `start notepad.exe D:\test.txt` |
| `kill <PID>` | 终止进程及整个子进程树 | `kill 8432` |
| `kill-name <进程名>` | 按进程名称批量终止 | `kill-name notepad` |
| `restart <PID>` | 自动提取路径参数并重启进程 | `restart 8432` |
| `suspend <PID>` | 挂起（暂停）进程 | `suspend 8432` |
| `resume <PID>` | 恢复执行挂起的进程 | `resume 8432` |
| `ps <脚本正文>` | 执行一次性 PowerShell 脚本 | `ps Get-Process \| Select-Object -First 3` |
| `cmd` | **进入连续多轮对话的交互式 CMD 终端** | `cmd` |
| `powershell` | **进入连续多轮对话的交互式 PowerShell 终端** | `powershell` |
| `perf` | 查看主机性能指标（CPU、内存使用量、进程数） | `perf` |
| `clear` | 清理屏幕 | `clear` |
| `exit` | 退出控制台 | `exit` |

### 💬 连续多轮 CMD 对话演示
进入 `cmd` 模式后，直接键入操作系统命令，变量与路径跨轮次完整保持：
```cmd
[CMD]> cd /d C:\Users
[CMD]> echo %CD%
C:\Users
[CMD]> set MY_TEMP_VAR=ApeFreeSession666
[CMD]> echo %MY_TEMP_VAR%
ApeFreeSession666
[CMD]> exit
```

---

## 7. 📄 开源许可证 (License)

本项目基于 [Apache-2.0](LICENSE) 协议开源。
