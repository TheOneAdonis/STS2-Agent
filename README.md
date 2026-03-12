

https://github.com/user-attachments/assets/89353468-a299-4315-9516-e520bcbfbd4b

# STS2 AI Agent

`STS2 AI Agent` 由两部分组成：

- `STS2AIAgent` Mod：把游戏状态和操作暴露为本地 HTTP API。
- `mcp_server`：把本地 HTTP API 包装成 MCP Server，供支持 MCP 的客户端直接调用。


## 你会下载到什么

发布包内通常包含这些目录：

```text
mod/
  STS2AIAgent.dll
  STS2AIAgent.pck
mcp_server/
  pyproject.toml
  uv.lock
  src/sts2_mcp/...
scripts/
  start-mcp-stdio.ps1
  start-mcp-network.ps1
README.md
```

如果你只想安装 Mod，只需要 `mod/` 目录里的两个文件。

## 快速开始

### 1. 安装 Mod

1. 下载并解压 release 压缩包。
2. 打开你的游戏目录。
   Steam 默认路径通常是：

   ```text
   C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2
   ```

3. 如果游戏目录下没有 `mods` 文件夹，就新建一个。
4. 把 `mod/STS2AIAgent.dll` 和 `mod/STS2AIAgent.pck` 复制到游戏目录的 `mods/` 中。

最终结构应当类似：

```text
Slay the Spire 2/
  mods/
    STS2AIAgent.dll
    STS2AIAgent.pck
```

### 2. 启动游戏

先正常启动一次游戏，让 Mod 随游戏一起加载。

如果你想确认 Mod 是否已经生效，可以在浏览器里打开：

```text
http://127.0.0.1:8080/health
```

能看到返回结果，就说明 Mod 已成功启动。

### 3. 启动 MCP

#### 推荐方式：stdio MCP

这是最适合接入桌面 AI 客户端的方式。

先准备环境：

1. 安装 Python 3.11 或更高版本。
2. 安装 `uv`。

安装 `uv` 的常见方式：

```powershell
powershell -ExecutionPolicy Bypass -c "irm https://astral.sh/uv/install.ps1 | iex"
```

然后在 release 解压目录中运行：

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\start-mcp-stdio.ps1"
```

脚本会自动：

- 进入 `mcp_server/`
- 执行 `uv sync`
- 启动 `sts2-mcp-server`

如果你更喜欢手动启动，也可以执行：

```powershell
cd ".\mcp_server"
uv sync
uv run sts2-mcp-server
```

#### 可选方式：HTTP MCP

如果你的 MCP 客户端更适合通过网络地址连接，可以启动 HTTP 版本：

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\start-mcp-network.ps1"
```

默认监听地址：

```text
http://127.0.0.1:8765/mcp
```

## MCP 客户端如何接

如果你的客户端支持 `stdio` MCP，一般只需要把启动命令指向：

```text
uv run sts2-mcp-server
```

工作目录设置为 release 包中的 `mcp_server/` 即可。

如果你的客户端支持 HTTP MCP，地址填：

```text
http://127.0.0.1:8765/mcp
```

## 常见问题

### 看不到 `http://127.0.0.1:8080/health`

优先检查：

1. 游戏是否已经启动。
2. `STS2AIAgent.dll` 和 `STS2AIAgent.pck` 是否都放进了 `mods/`。
3. 文件名是否被系统自动改成了带 `(1)` 的副本。
4. 游戏目录是否放错了，例如放进了仓库目录而不是 Steam 游戏目录。

### MCP 能启动，但读不到游戏状态

这通常表示 MCP 正常，但游戏里的 Mod 没有连上。请先确认：

1. 游戏正在运行。
2. `http://127.0.0.1:8080/health` 可访问。
3. MCP 使用的接口地址仍然是默认值 `http://127.0.0.1:8080`。

### 要不要开启 debug 动作

正式使用不需要。

`run_console_command` 这类调试工具默认关闭，发布建议保持关闭。


## 相关目录

- `STS2AIAgent/`：游戏 Mod 源码
- `mcp_server/`：MCP Server 源码
- `scripts/`：构建、验证和启动脚本
