https://github.com/user-attachments/assets/89353468-a299-4315-9516-e520bcbfbd4b

# 创造性AI

`创造性AI` 是 `STS2 AI Agent` 的实验性共存分支，当前只保留两部分：

- `CreativeAI` Mod：在游戏内提供 AI 托管、状态采集和本地 HTTP 服务
- `CreativeAI.Desktop`：桌面控制台，用于配置、启动、停止和编辑提示词

这个分支已经移除了 `mcp_server`，因此不再面向 MCP 客户端使用。

## 分支定位

当前分支 `codex/in-game-agent-panel-exp` 的目标是：

- 与原版 `STS2 AI Agent` 共存
- 独立安装、独立端口、独立本地配置目录
- 专注于游戏内托管和桌面控制台

当前实验分支默认使用：

- Mod 目录：`mods/CreativeAI/`
- Mod 文件：`CreativeAI.dll`、`CreativeAI.pck`、`CreativeAI.json`
- 桌面程序：`mods/CreativeAI/desktop/CreativeAI.Desktop.exe`
- 本地服务：`http://127.0.0.1:8081/`
- 健康检查：`http://127.0.0.1:8081/health`
- 本地运行目录：`%LOCALAPPDATA%\\creative-ai\\`

如果你想使用原版 `STS2 AI Agent`，请切回 `master` 分支；原版仍使用 `STS2AIAgent` 和 `8080`。

## 安装后目录

构建并安装后，游戏目录通常会出现：

```text
Slay the Spire 2/
  mods/
    STS2AIAgent/
      ...
    CreativeAI/
      CreativeAI.dll
      CreativeAI.pck
      CreativeAI.json
      desktop/
        CreativeAI.Desktop.exe
```

## 快速开始

### 1. 安装 Mod

如果你直接使用已经构建好的文件，只需要把整个 `CreativeAI` 文件夹放进游戏目录的 `mods/` 中。

### 2. 启动游戏

启动游戏后，`CreativeAI` 会在后台启动本地服务，并尝试拉起桌面控制台。

如果你想确认 Mod 是否已经生效，可以打开：

```text
http://127.0.0.1:8081/health
```

### 3. 使用桌面控制台

桌面控制台主要用于：

- 配置 Base URL、模型和 API Key
- 编辑当前角色的战斗提示词 / 爬塔提示词
- 启动或停止 AI 托管
- 观察当前角色、当前界面、当前计划和最近动作

实验分支的提示词和日志默认保存在：

```text
%LOCALAPPDATA%\creative-ai\
```

## 从源码构建

如果你在仓库中直接构建，可使用：

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\build-mod.ps1" -Configuration Debug -GameRoot "D:\SteamLibrary\steamapps\common\Slay the Spire 2" -GodotExe "D:\godot\Godot_v4.5.1-stable_win64.exe\Godot_v4.5.1-stable_win64_console.exe"
```

构建脚本会：

- 编译 `CreativeAI.dll`
- 编译 `CreativeAI.Desktop.exe`
- 生成 `CreativeAI.pck`
- 安装到游戏目录 `mods/CreativeAI/`

## 使用影响

移除 `mcp_server` 后，不影响以下功能：

- 游戏内 AI 托管
- 桌面控制台启动与使用
- 提示词编辑
- 本地知识库读写
- 与原版主线并存安装

会失去的能力只有：

- 通过 MCP 客户端把游戏当成一个 MCP 工具来调用
- 旧的 `start-mcp-stdio.ps1` / `start-mcp-network.ps1` 这类启动方式

## 常见问题

### 看不到 `http://127.0.0.1:8081/health`

优先检查：

1. 游戏是否已经启动
2. `mods/CreativeAI/` 是否完整存在
3. `CreativeAI.dll` 和 `CreativeAI.pck` 是否都在 `mods/CreativeAI/` 下
4. 文件名是否被系统自动改成了带 `(1)` 的副本

### 桌面控制台没有弹出

优先检查：

1. `mods/CreativeAI/desktop/CreativeAI.Desktop.exe` 是否存在
2. 是否被安全软件拦截
3. 是否已经有一个同名桌面进程在运行

## 相关目录

- `STS2AIAgent/`：游戏 Mod 源码
- `STS2AIAgent.Desktop/`：桌面控制台源码
- `scripts/build-mod.ps1`：构建并安装实验分支
- `scripts/package-release.ps1`：打包实验分支发布物
