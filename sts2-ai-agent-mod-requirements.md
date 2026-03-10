# STS2 AI Agent Mod 需求文档

## 项目概述

开发一个 BepInEx Mod，让外部 LLM 能通过 HTTP API 完整控制杀戮尖塔 2（Slay the Spire 2）的整场游戏流程。

---

## 目标

实现一个完整的 AI 代理系统，能够：
- 自动完成从开局到通关/死亡的完整游戏流程
- 在战斗、地图导航、商店、事件等所有场景做出决策
- 管理卡组构筑、药水使用、路线规划等长期策略

---

## 系统架构

```
┌─────────────────────────────────────────────┐
│  Slay the Spire 2 (Unity)                   │
│  ├─ BepInEx Mod Loader                      │
│  ├─ STS2AIPlugin.dll (C# Mod)               │
│  │   ├─ StateExtractor                      │
│  │   ├─ ActionExecutor                      │
│  │   └─ HTTPServer (localhost:8080)         │
│  └─ 游戏原逻辑                               │
└──────────────────┬──────────────────────────┘
                   │ HTTP/WebSocket
                   ▼
┌─────────────────────────────────────────────┐
│  Python AI Agent                             │
│  ├─ GameClient (HTTP 客户端)                │
│  ├─ StateParser (状态解析)                  │
│  ├─ LLMEngine (决策引擎)                    │
│  ├─ StrategyPlanner (长期规划)              │
│  └─ ActionSender (动作执行)                 │
└─────────────────────────────────────────────┘
```

---

## 功能需求

### Phase 1: 核心系统 (P0)

#### 1.1 Mod 基础架构
- **BepInEx 6** 插件框架
- **Harmony** 运行时注入
- **EmbedIO** 轻量 HTTP 服务器
- 端口 8080，本地绑定

#### 1.2 HTTP API 设计

**基础端点**

| 端点 | 方法 | 功能 |
|------|------|------|
| `/health` | GET | 服务健康检查 |
| `/state` | GET | 获取当前完整游戏状态 |
| `/action` | POST | 执行游戏动作 |
| `/events` | WebSocket | 实时事件流 |

**动作类型**

```json
{
  "type": "play_card",
  "card_index": 0,
  "target_index": 0
}
```

```json
{
  "type": "end_turn"
}
```

```json
{
  "type": "select_option",
  "option_index": 1
}
```

### Phase 2: 战斗系统 (P0)

#### 2.1 状态提取

**玩家状态**
```json
{
  "player": {
    "hp": 80,
    "max_hp": 80,
    "energy": 3,
    "max_energy": 3,
    "block": 0,
    "buffs": [],
    "debuffs": []
  }
}
```

**手牌信息**
```json
{
  "hand": [
    {
      "index": 0,
      "id": "Strike",
      "name": "打击",
      "cost": 1,
      "type": "attack",
      "rarity": "basic",
      "description": "造成 6 点伤害",
      "upgraded": false
    }
  ]
}
```

**敌人状态**
```json
{
  "enemies": [
    {
      "index": 0,
      "id": "Cultist",
      "name": "邪教徒",
      "hp": 50,
      "max_hp": 50,
      "intent": "ritual",
      "intent_description": "正在施放仪式",
      "buffs": [],
      "debuffs": []
    }
  ]
}
```

**战斗状态**
```json
{
  "combat": {
    "turn": 1,
    "floor": 1,
    "act": 1,
    "draw_pile_count": 10,
    "discard_pile_count": 0,
    "exhaust_pile_count": 0,
    "potions": [
      {"index": 0, "id": "FirePotion", "name": "火焰药水"}
    ]
  }
}
```

#### 2.2 战斗动作

- `play_card` — 打出指定手牌
- `use_potion` — 使用药水
- `end_turn` — 结束回合
- `target_enemy` — 选择目标敌人
- `target_self` — 选择自己为目标

### Phase 3: 地图导航 (P1)

#### 3.1 地图状态

```json
{
  "map": {
    "current_node": {"x": 5, "y": 3},
    "available_nodes": [
      {"x": 6, "y": 2, "type": "enemy", "icon": "👹"},
      {"x": 6, "y": 3, "type": "elite", "icon": "👑"},
      {"x": 6, "y": 4, "type": "rest", "icon": "🔥"}
    ],
    "boss_node": {"x": 15, "y": 3, "type": "boss"},
    "current_floor": 5,
    "total_floors": 15
  }
}
```

#### 3.2 导航动作

- `move_to` — 移动到指定节点
- `view_map` — 查看完整地图

### Phase 4: 非战斗场景 (P1)

#### 4.1 商店

```json
{
  "shop": {
    "cards": [...],
    "relics": [...],
    "potions": [...],
    "remove_cost": 75,
    "gold": 150
  }
}
```

**动作**
- `buy_card` — 购买卡牌
- `buy_relic` — 购买遗物
- `buy_potion` — 购买药水
- `remove_card` — 删除卡组中的牌
- `skip` — 离开商店

#### 4.2 事件

```json
{
  "event": {
    "id": "GoldenIdol",
    "title": "黄金神像",
    "description": "你发现了一个黄金神像...",
    "options": [
      {"index": 0, "text": "拿走神像", "risk": "受伤"},
      {"index": 1, "text": "离开", "risk": "无"}
    ]
  }
}
```

**动作**
- `select_event_option` — 选择事件选项

#### 4.3 休息点

```json
{
  "rest": {
    "options": ["rest", "smith", "lift", "dig"]
  }
}
```

**动作**
- `rest` — 回血
- `smith` — 升级卡牌
- `lift` — 获得力量（特定角色）
- `dig` — 挖掘（特定遗物）

#### 4.4 宝箱

**动作**
- `open_chest` — 打开宝箱
- `skip_chest` — 跳过

### Phase 5: 卡组管理 (P1)

#### 5.1 卡组信息

```json
{
  "deck": {
    "cards": [...],
    "count": 15,
    "strikes": 4,
    "defends": 4,
    "attacks": 8,
    "skills": 4,
    "powers": 2
  }
}
```

#### 5.2 战斗后奖励

```json
{
  "rewards": {
    "gold": 15,
    "cards": [
      {"index": 0, "id": "Bash", "name": "重击"},
      {"index": 1, "id": "Cleave", "name": "顺劈斩"},
      {"index": 2, "id": "Skip", "name": "跳过"}
    ],
    "potions": [...],
    "relic": null
  }
}
```

**动作**
- `take_reward_card` — 选择卡牌奖励
- `take_reward_potion` — 选择药水
- `take_reward_relic` — 选择遗物
- `skip_reward` — 跳过

### Phase 6: 长期策略 (P2)

#### 6.1 整场 Run 状态

```json
{
  "run": {
    "character": "ironclad",
    "ascension": 0,
    "seed": "1234567890",
    "start_time": "2026-03-10T10:00:00Z",
    "current_floor": 5,
    "max_floors": 15,
    "boss": "slime_boss",
    "gold": 150,
    "hp": 80,
    "max_hp": 80,
    "relics": [...],
    "deck_size": 15
  }
}
```

#### 6.2 游戏结束检测

```json
{
  "game_over": {
    "finished": true,
    "victory": false,
    "floor_reached": 12,
    "score": 1250,
    "killed_by": "gremlin_nob"
  }
}
```

### Phase 7: 错误处理 (P2)

#### 7.1 非法操作防护
- 能量不足时阻止出牌
- 目标不存在时返回错误
- 非玩家回合阻止动作

#### 7.2 超时处理
- 动作执行超时：5 秒
- 状态查询超时：2 秒
- 自动心跳检测

#### 7.3 崩溃恢复
- 游戏崩溃检测
- 自动重连机制
- 状态同步恢复

---

## 完整游戏流程覆盖

```
新游戏
  ↓
选择角色（铁甲战士/静默猎手/故障机器人/观者）
  ↓
初始卡组生成
  ↓
地图生成（Act 1）
  ↓
循环直到通关或死亡：
  ├─ 地图导航（选择房间）
  │    ├─ 普通敌人 → 战斗 → 选奖励
  │    ├─ 精英敌人 → 战斗（更难）→ 更好奖励
  │    ├─ Boss → 战斗（最难）→ 遗物 + 进入下一 Act
  │    ├─ 商店 → 买卖卡牌/药水/遗物
  │    ├─ 事件 → 选择选项
  │    ├─ 休息点 → 回血/升级/特殊动作
  │    └─ 宝箱 → 获得遗物
  │
  ├─ 卡组管理（添加/删除/升级卡牌）
  ├─ 药水管理（购买/使用）
  └─ 遗物管理（获得新遗物效果）
  ↓
通关（击败 Act 3 Boss）或 死亡
  ↓
统计信息（分数、成就）
```

---

## 技术约束

- **引擎**: Unity 2022.3 LTS
- **运行时**: .NET 6
- **Mod 框架**: BepInEx 6 + Harmony 2
- **HTTP 服务器**: EmbedIO 或 LiteNetLib
- **端口**: 8080（本地）
- **数据格式**: JSON

---

## 逆向工程目标

需要找到以下关键类和方法：

| 类名 | 用途 |
|------|------|
| `Player` | 玩家状态（血量、能量、卡组） |
| `AbstractCard` | 卡牌信息 |
| `AbstractMonster` | 敌人信息 |
| `AbstractCreature` | 生物基类（意图、buff） |
| `CombatManager` / `BattleManager` | 战斗管理 |
| `MapManager` | 地图管理 |
| `ShopScreen` | 商店界面 |
| `EventManager` | 事件管理 |
| `RewardScreen` | 奖励选择 |

---

## 交付物

### Mod 端 (C#)
```
STS2AIPlugin/
├── STS2AIPlugin.csproj
├── Plugin.cs (入口)
├── StateExtractor.cs
├── ActionExecutor.cs
├── HTTPServer.cs
├── Patches/
│   ├── CombatPatch.cs
│   ├── MapPatch.cs
│   └── ShopPatch.cs
└── Utils/
    └── ReflectionHelper.cs
```

### AI 端 (Python)
```
sts2_agent/
├── client/
│   ├── http_client.py
│   └── websocket_client.py
├── parser/
│   └── state_parser.py
├── engine/
│   ├── llm_engine.py
│   └── strategy_planner.py
├── actions/
│   └── action_sender.py
├── main.py
└── config.yaml
```

### 文档
- `docs/api.md` — API 详细文档
- `docs/reverse.md` — 逆向工程笔记
- `docs/setup.md` — 安装和运行指南

---

## 优先级

| 阶段 | 功能 | 优先级 | 预估时间 |
|------|------|--------|----------|
| 1 | 基础架构 + HTTP API | P0 | 2-3 天 |
| 2 | 战斗状态提取 | P0 | 2-3 天 |
| 3 | 战斗动作执行 | P0 | 2-3 天 |
| 4 | 地图导航 | P1 | 2-3 天 |
| 5 | 商店/事件/休息 | P1 | 3-4 天 |
| 6 | 卡组管理 + 奖励 | P1 | 2-3 天 |
| 7 | AI 端决策引擎 | P2 | 3-5 天 |
| 8 | 长期策略规划 | P2 | 持续优化 |
| 9 | 错误处理 + 测试 | P2 | 持续优化 |

---

## 验收标准

- [ ] Mod 成功加载，HTTP 服务正常
- [ ] 能提取完整战斗状态
- [ ] 能执行基础战斗动作（出牌、结束回合）
- [ ] 能完成一场完整战斗
- [ ] 能导航地图并选择房间
- [ ] 能处理商店、事件、休息点
- [ ] 能管理卡组（选奖励、删牌）
- [ ] AI 端能自主完成从开局到通关/死亡的完整流程
- [ ] 错误处理和崩溃恢复正常

---

## 备注

- 参考项目：Slay the Spire 1 的 [Communication Mod](https://github.com/ForgottenArbiter/CommunicationMod)
- 开发时注意游戏版本更新可能破坏 Mod
- 考虑向后兼容官方 Mod 工具（如果未来发布）
