

# 创造性AI
本 mod fork 自 https://github.com/CharTyr/STS2-Agent ，集成了其将杀戮尖塔2游戏状态和操作暴露为本地 HTTP API 的功能，并将 MCP 配置修改为了单独的控制台以简化配置流程。

## 它能做什么

`创造性AI` 的目标不是只做一次性问答，而是持续接管一局爬塔流程。

它可以：

- 在游戏运行时持续同步当前界面、角色、阶段与运行状态
- 在战斗阶段调用战斗提示词，处理出牌、用药、选目标、结束回合
- 在非战斗阶段调用爬塔提示词，处理路线、抓牌、事件、商店、休息点等决策
- 为不同角色分别保存独立提示词
- 在桌面控制台中实时查看连接状态、当前计划、最近动作与错误信息
- 在本地保存模型配置与提示词，不依赖额外中间服务

## 项目组成

安装完成后，核心结构如下：

```text
Slay the Spire 2/
  mods/
    CreativeAI/
      CreativeAI.dll
      CreativeAI.pck
      CreativeAI.json
      desktop/
        CreativeAI.Desktop.exe
```

默认运行参数：

- Mod 目录：`mods/CreativeAI/`
- 本地服务：`http://127.0.0.1:8081/`
- 健康检查：`http://127.0.0.1:8081/health`
- 本地配置目录：`%LOCALAPPDATA%\creative-ai\`

## 核心功能

### 1. 游戏内托管

当你在控制台点击“开始托管”后，系统会在当前对局中自动接管决策。

- 战斗中使用战斗提示词
- 地图、奖励、事件、商店、休息点等阶段使用爬塔提示词
- 运行时会自动根据当前上下文切换 Agent

### 2. 角色专属提示词

每个角色都有独立的两套提示词：

- 战斗提示词：偏向回合内操作与即时战术
- 爬塔提示词：偏向路线规划、资源管理与长期构筑

只要进入对应角色的对局，控制台就会自动切换到该角色的提示词上下文。

### 3. 桌面控制台

桌面控制台提供这些能力：

- 配置模型服务地址、模型名和 API Key
- 检测模型连接
- 保存配置到本地并同步给游戏内服务
- 启动或停止 AI 托管
- 查看当前角色、当前界面、当前阶段、当前计划、最近动作和错误信息
- 在托管停止时编辑当前角色的提示词

## 使用方式

### 1. 安装

将整个 `CreativeAI` 文件夹放入游戏目录的 `mods/` 下即可。

### 2. 启动游戏

启动游戏后，Mod 会尝试启动本地服务，并拉起桌面控制台。

如果你想检查服务是否正常工作，可访问：

```text
http://127.0.0.1:8081/health
```

### 3. 配置模型

第一次使用时，需要在控制台填写：

- `Base URL`
- `模型`
- `API Key`

保存后，配置会写入本地目录：

```text
%LOCALAPPDATA%\creative-ai\config\
```

### 4. 编辑提示词

进入任意角色的实际对局后，控制台会识别当前角色，并加载该角色的：

- 战斗提示词
- 爬塔提示词

当 AI 未运行时，你可以直接编辑并保存。

### 5. 启动托管

完成配置后，点击“开始托管”即可。

运行过程中：

- 配置输入框会锁定
- 提示词输入框会锁定
- 控制台会持续显示当前托管状态与 AI 输出

点击“停止托管”后即可恢复编辑。

## 适用场景

`创造性AI` 适合这些用途：

- 想让 AI 完整跑一局爬塔
- 想为不同角色分别调教提示词
- 想观察 AI 在不同阶段的计划、理由和执行结果
- 想把模型调用、提示词编辑和游戏托管整合在一个本地工作流里

## 从源码构建

可使用脚本直接编译并安装到游戏目录：

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\build-mod.ps1" -Configuration Debug -GameRoot "D:\SteamLibrary\steamapps\common\Slay the Spire 2" -GodotExe "D:\godot\Godot_v4.5.1-stable_win64.exe\Godot_v4.5.1-stable_win64_console.exe"
```

构建结果包括：

- `CreativeAI.dll`
- `CreativeAI.pck`
- `CreativeAI.Desktop.exe`

并会自动安装到游戏目录的 `mods/CreativeAI/` 中。

## 常见问题

### 控制台打不开

优先检查：

1. `mods/CreativeAI/desktop/CreativeAI.Desktop.exe` 是否存在
2. 是否被系统或安全软件拦截
3. 是否已经有同名进程在运行

### 健康检查地址无法访问

优先检查：

1. 游戏是否已经启动
2. `mods/CreativeAI/` 是否完整
3. `CreativeAI.dll` 与 `CreativeAI.pck` 是否都存在
4. 本地 `8081` 端口是否被其他程序占用

### 为什么提示词不能随时编辑

这是刻意的运行保护：

- AI 运行时会锁定提示词与基础配置
- 目的是避免在同一局运行中途切换上下文，导致决策漂移或配置不一致

停止托管后即可继续修改。

## 目录说明

- `C:\Users\白泽\Desktop\工作\STS2-Agent\STS2AIAgent\`：游戏内 Mod 源码
- `C:\Users\白泽\Desktop\工作\STS2-Agent\STS2AIAgent.Desktop\`：桌面控制台源码
- `C:\Users\白泽\Desktop\工作\STS2-Agent\scripts\build-mod.ps1`：构建并安装
- `C:\Users\白泽\Desktop\工作\STS2-Agent\scripts\package-release.ps1`：打包发布物
