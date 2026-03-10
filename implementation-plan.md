# STS2 AI Agent Mod - 实施计划

## Context

用户需要开发一个系统，让 LLM 能够完全自主地打通杀戮尖塔 2（Slay the Spire 2）的一整局游戏。原需求文档假设了 Unity + BepInEx 技术栈，但经过实际调研发现 **STS2 使用 Godot 4.5.1 引擎**，需要完全不同的技术方案。

### 关键技术事实

| 项目 | 实际值 |
|------|-------|
| 游戏引擎 | **Godot 4.5.1**（非 Unity） |
| 运行时 | **.NET Core 9.0** |
| 主程序集 | `sts2.dll` (8.87 MB) |
| Harmony | 已内置 v2.4.2.0 |
| Mod 系统 | 原生（.dll + .pck 放入 mods/ 目录） |
| 游戏版本 | v0.98.2 |
| 游戏路径 | `C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/` |
| 程序集目录 | `data_sts2_windows_x86_64/` |

### 架构

```
STS2 (Godot 4.5.1)
  └── Native Mod (C# .dll + .pck)
       ├── StateExtractor (Harmony patches + 反射读取游戏状态)
       ├── ActionExecutor (程序化执行游戏操作)
       └── HTTP Server (System.Net.HttpListener, localhost:8080)
            │
            ▼
MCP Server (Python + FastMCP)
  ├── HTTP Client → 连接 Mod 的 HTTP API
  ├── MCP Tools → get_game_state, play_card, end_turn, navigate_map...
  └── 任何 MCP 兼容客户端 (Claude Code, Cursor 等) 可直接使用
```

---

## 项目目录结构

```
C:/Users/chart/Documents/project/sp/
├── STS2AIAgent/                          ← C# Mod 项目
│   ├── STS2AIAgent.csproj
│   ├── local.props / local.props.example
│   ├── ModEntry.cs                       ← [ModInitializer] 入口
│   ├── Server/
│   │   ├── HttpServer.cs                ← System.Net.HttpListener
│   │   ├── Router.cs                    ← 路由分发
│   │   └── JsonHelper.cs               ← JSON 序列化
│   ├── State/
│   │   ├── GameStateExtractor.cs        ← 总状态提取 + 场景识别
│   │   ├── CombatStateExtractor.cs      ← 战斗状态
│   │   ├── MapStateExtractor.cs         ← 地图状态
│   │   ├── PlayerStateExtractor.cs      ← 玩家/卡组/遗物/药水
│   │   ├── EventStateExtractor.cs
│   │   ├── ShopStateExtractor.cs
│   │   └── RewardStateExtractor.cs
│   ├── Actions/
│   │   ├── ActionExecutor.cs            ← 动作总调度
│   │   ├── CombatActions.cs             ← 出牌/结束回合/药水
│   │   ├── MapActions.cs
│   │   ├── EventActions.cs
│   │   ├── ShopActions.cs
│   │   ├── RewardActions.cs
│   │   └── RestSiteActions.cs
│   ├── Patches/
│   │   ├── StateTrackingPatches.cs      ← Harmony: 状态监控
│   │   └── FlowControlPatches.cs        ← Harmony: 流程控制
│   └── Utils/
│       ├── ReflectionHelper.cs
│       └── GameContext.cs               ← 游戏上下文缓存单例
├── mcp_server/                           ← Python MCP Server
│   ├── pyproject.toml                   ← 依赖: fastmcp, httpx
│   ├── server.py                        ← FastMCP 主文件
│   ├── sts2_client.py                   ← HTTP 客户端
│   ├── tools/
│   │   ├── state_tools.py
│   │   ├── combat_tools.py
│   │   ├── navigation_tools.py
│   │   ├── shop_tools.py
│   │   ├── event_tools.py
│   │   └── reward_tools.py
│   └── models.py                        ← Pydantic 模型
├── docs/
│   ├── api.md
│   ├── setup.md
│   └── reverse-engineering.md
└── sts2-ai-agent-mod-requirements.md
```

---

## 分阶段实施

### Phase 0: 逆向工程侦察（2-3 天）

用 ILSpy 反编译 `sts2.dll`，定位关键运行时类。

**已知的类结构（来自 spire-codex 和 BaseLib）：**

| 类名 | 命名空间 | 用途 |
|------|---------|------|
| `RunManager` | `MegaCrit.Sts2.Core.Runs` | 运行管理，ActionQueue |
| `CombatManager` | `MegaCrit.Sts2.Core` | 战斗管理，SetUpCombat |
| `Player` | `MegaCrit.Sts2.Core.Entities.Players` | 玩家实体 |
| `Creature` | `MegaCrit.Sts2.Core.Entities.Creatures` | 生物基类 |
| `CardModel` | `MegaCrit.Sts2.Core.Models` | 卡牌，EnqueueManualPlay |
| `ModelDb` | `MegaCrit.Sts2.Core.Models` | 全局模型数据库 |

**需要额外定位的类：**
- 场景/屏幕类型枚举（COMBAT/MAP/EVENT/SHOP 等）
- 地图节点类
- 商店类
- 奖励屏幕类
- 怪物 Intent 类

**操作步骤：**
1. 安装 ILSpy: `dotnet tool install ilspycmd -g`
2. 反编译: `ilspycmd -p -o ./extraction/decompiled "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/sts2.dll"`
3. 搜索关键类和单例模式
4. 记录到 `docs/reverse-engineering.md`

---

### Phase 1: Mod 骨架 + HTTP 服务器（2-3 天）

**目标：** 可加载的 Mod + 响应 `/health` 的 HTTP 服务。

**关键文件：**
- `STS2AIAgent.csproj` — 参考 sts2_example_mod 模板，引用 sts2.dll、0Harmony.dll、GodotSharp.dll
- `ModEntry.cs` — `[ModInitializer("Initialize")]` 入口，初始化 Harmony + 启动 HTTP 服务
- `Server/HttpServer.cs` — `System.Net.HttpListener` 在后台线程运行

**HTTP 线程安全策略：**
1. HTTP 监听在专用后台线程
2. 收到请求 → `CallDeferred` 调度到 Godot 主线程
3. 主线程执行 → `TaskCompletionSource` 返回结果
4. HTTP 线程序列化 JSON 返回

**API 端点：**

| 端点 | 方法 | 功能 |
|------|------|------|
| `/health` | GET | 健康检查 + Mod 版本 |
| `/state` | GET | 完整游戏状态快照 |
| `/state/combat` | GET | 战斗状态 |
| `/state/map` | GET | 地图状态 |
| `/actions/available` | GET | 当前可执行动作列表 |
| `/action` | POST | 执行游戏动作 |

---

### Phase 2: 战斗状态提取（3-4 天）

**目标：** 完整读取战斗中所有状态。

提取的状态结构：
```json
{
  "screen": "COMBAT",
  "combat": {
    "turn": 1,
    "phase": "PLAYER_TURN",
    "player": { "hp": 80, "max_hp": 80, "energy": 3, "block": 0, "powers": [...] },
    "hand": [{ "index": 0, "id": "Strike", "name": "打击", "cost": 1, "type": "attack", "is_playable": true, "damage": 6 }],
    "enemies": [{ "index": 0, "id": "Cultist", "hp": 50, "intent": "attack", "intent_damage": 6, "powers": [...] }],
    "draw_pile_count": 10,
    "discard_pile_count": 0,
    "exhaust_pile_count": 0
  },
  "available_actions": ["play_card", "use_potion", "end_turn"]
}
```

**关键技术：**
- `DamageVar.PreviewValue` 获取最终计算后的伤害（含增益修正）
- Harmony Postfix patch `CombatManager.SetUpCombat` 缓存战斗引用
- GameContext 单例缓存关键运行时对象引用

---

### Phase 3: 战斗动作执行（3-4 天）

**目标：** 程序化出牌、结束回合、使用药水。

**出牌调用链：**
```
CardModel.EnqueueManualPlay(target)
  → new PlayCardAction → RunManager.ActionQueueSynchronizer.RequestEnqueue
  → 合法性检查 → CardModel.OnPlayWrapper
```

**动作格式：**
```json
{"action": "play_card", "card_index": 0, "target_index": 0}
{"action": "end_turn"}
{"action": "use_potion", "potion_index": 0, "target_index": 0}
```

**动作校验：** 能量检查、目标有效性、回合状态

**"等待状态稳定"机制：** Harmony patch 动画/队列完成回调 + TaskCompletionSource

---

### Phase 4: 地图导航 + 非战斗场景（4-5 天）

覆盖所有非战斗场景：
- **地图导航** — 选择下一个节点
- **事件** — 选择选项
- **商店** — 买卡/买遗物/买药水/删牌
- **休息点** — 休息/升级/特殊动作
- **宝箱** — 打开/跳过
- **卡牌奖励** — 选卡/跳过
- **游戏结束** — 检测通关/死亡

---

### Phase 5: MCP Server（2-3 天，可与 Phase 4 并行）

**Python FastMCP 服务器，封装 HTTP API 为 MCP 工具。**

核心工具列表：

| MCP Tool | 对应 API | 用途 |
|----------|---------|------|
| `get_game_state` | GET /state | 获取完整游戏状态 |
| `get_available_actions` | GET /actions/available | 当前可执行动作 |
| `play_card` | POST /action | 出牌 |
| `end_turn` | POST /action | 结束回合 |
| `use_potion` | POST /action | 使用药水 |
| `choose_map_node` | POST /action | 选择地图节点 |
| `choose_event_option` | POST /action | 选择事件选项 |
| `buy_card` / `buy_relic` | POST /action | 商店购买 |
| `rest` / `smith` | POST /action | 休息点操作 |
| `take_reward_card` / `skip_reward` | POST /action | 奖励选择 |

工具 docstring 对 LLM 友好，返回值包含执行结果 + 更新后状态。

---

### Phase 6: 集成测试（3-5 天）

1. 构建 Mod → 安装 → 启动游戏 → 验证 HTTP API
2. 启动 MCP Server → 配置 Claude Desktop/Claude Code
3. 让 LLM 执行完整游戏流程
4. 边界情况：动画中读取状态、快速连续动作、手动/API 冲突

---

## 前置环境准备

1. **.NET 9.0 SDK** — 编译 Mod
2. **Godot 4.5.1 Mono** — 打包 .pck 资源文件
3. **Python 3.11+** + `uv` — MCP Server
4. **ILSpy** (`dotnet tool install ilspycmd -g`) — 反编译 sts2.dll

---

## 技术风险

| 风险 | 缓解策略 |
|------|---------|
| HttpListener 权限问题 | 备选: TcpListener 手动实现 HTTP |
| 关键类 internal/private | AccessTools (Harmony) + System.Reflection |
| 动作后状态不稳定 | Harmony patch 完成回调 + TaskCompletionSource |
| 游戏更新破坏 Mod | null 检查 + 版本检测 |
| Godot 主线程限制 | 所有游戏访问通过 CallDeferred 调度 |

---

## 验证方案

1. **Mod 加载验证：** 游戏启动后检查日志输出 "STS2 AI Agent ready"
2. **HTTP API 验证：** `curl http://localhost:8080/health` 返回 200
3. **状态提取验证：** 进入战斗后 `curl http://localhost:8080/state` 返回完整 JSON
4. **动作执行验证：** `curl -X POST http://localhost:8080/action -d '{"action":"end_turn"}'` 游戏响应
5. **MCP 验证：** 在 Claude Code 中配置 MCP Server，执行 `get_game_state` 工具
6. **端到端验证：** LLM 通过 MCP 工具自主完成一场完整战斗
