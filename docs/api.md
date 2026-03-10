# STS2 AI Agent Mod - 最小 HTTP API 协议 v0

状态：草案，可实现  
版本：`2026-03-10-v0`

## 约束

- 协议基于 `HTTP + JSON`
- 默认监听 `http://127.0.0.1:8080`
- 响应类型固定为 `application/json; charset=utf-8`
- 新增字段必须向后兼容，不删除已有字段

## 通用响应

成功响应：

```json
{
  "ok": true,
  "request_id": "req_20260310_0001",
  "data": {}
}
```

失败响应：

```json
{
  "ok": false,
  "request_id": "req_20260310_0001",
  "error": {
    "code": "invalid_action",
    "message": "Action is not available in the current state.",
    "details": {
      "action": "end_turn",
      "screen": "MAP"
    },
    "retryable": false
  }
}
```

## 错误码

| 错误码 | 含义 |
| --- | --- |
| `unknown_error` | 未分类错误 |
| `invalid_request` | 请求体不合法 |
| `invalid_action` | 当前状态下不能执行该动作 |
| `invalid_target` | 目标或索引非法 |
| `state_unavailable` | 当前状态暂时不可安全读取 |
| `internal_error` | 服务内部异常 |

## Screen 枚举

- `UNKNOWN`
- `MAIN_MENU`
- `CHARACTER_SELECT`
- `MAP`
- `COMBAT`
- `EVENT`
- `SHOP`
- `REST`
- `REWARD`
- `CHEST`
- `GAME_OVER`

## Action Status

- `completed`
- `pending`
- `rejected`
- `failed`

## `GET /health`

作用：返回 Mod 基础状态。

示例：

```json
{
  "ok": true,
  "request_id": "req_20260310_0001",
  "data": {
    "service": "sts2-ai-agent",
    "mod_version": "0.1.0",
    "protocol_version": "2026-03-10-v0",
    "game_version": "v0.98.2",
    "status": "ready"
  }
}
```

## `GET /state`

作用：返回当前最小状态快照。

示例：

```json
{
  "ok": true,
  "request_id": "req_20260310_0002",
  "data": {
    "state_version": 1,
    "run_id": "WXJVZBQFK2",
    "screen": "COMBAT",
    "in_combat": true,
    "turn": 1,
    "available_actions": [
      "end_turn",
      "play_card"
    ],
    "combat": {
      "player": {
        "current_hp": 72,
        "max_hp": 80,
        "block": 0,
        "energy": 3,
        "stars": 0
      },
      "hand": [
        {
          "index": 0,
          "card_id": "Strike",
          "name": "Strike",
          "upgraded": false,
          "target_type": "AnyEnemy",
          "requires_target": true,
          "costs_x": false,
          "energy_cost": 1,
          "star_cost": 0,
          "playable": true,
          "unplayable_reason": null
        }
      ],
      "enemies": [
        {
          "index": 0,
          "enemy_id": "Nibbit",
          "name": "Nibbit",
          "current_hp": 12,
          "max_hp": 12,
          "block": 0,
          "is_alive": true,
          "is_hittable": true,
          "intent": "ButtMove"
        }
      ]
    },
    "map": null,
    "event": null,
    "shop": null,
    "rest": null,
    "reward": null,
    "game_over": null
  }
}
```

关键字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `state_version` | number | 状态模型版本 |
| `run_id` | string | 本局运行标识 |
| `screen` | string | 当前逻辑界面 |
| `in_combat` | boolean | 是否处于战斗流程 |
| `turn` | number or null | 当前回合数 |
| `available_actions` | string[] | 当前可执行动作名称 |
| `combat.player` | object or null | 玩家最小战斗状态 |
| `combat.hand` | object[] or null | 手牌快照 |
| `combat.enemies` | object[] or null | 敌人快照 |

## `GET /actions/available`

作用：返回当前状态下允许执行的动作描述。

示例：

```json
{
  "ok": true,
  "request_id": "req_20260310_0003",
  "data": {
    "screen": "COMBAT",
    "actions": [
      {
        "name": "end_turn",
        "requires_target": false,
        "requires_index": false
      },
      {
        "name": "play_card",
        "requires_target": false,
        "requires_index": true
      }
    ]
  }
}
```

说明：

- `play_card.requires_target` 固定为 `false`，因为是否需要目标取决于具体卡牌
- 调用方必须结合 `GET /state` 中 `combat.hand[index].requires_target` 决定是否传 `target_index`

## `POST /action`

作用：执行单个动作。

请求体：

```json
{
  "action": "play_card",
  "card_index": 0,
  "target_index": 0,
  "option_index": null,
  "client_context": {
    "source": "mcp",
    "tool_name": "play_card"
  }
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `action` | string | 动作名称 |
| `card_index` | number or null | 手牌索引，`play_card` 必填 |
| `target_index` | number or null | 目标索引，部分 `play_card` 必填 |
| `option_index` | number or null | 预留给非战斗场景 |
| `client_context` | object or null | 调试上下文，不参与游戏逻辑 |

当前已实现动作：

- `end_turn`
- `play_card`

`play_card` 规则：

- 卡牌必须来自当前手牌
- `TargetType.AnyEnemy` 必须传 `target_index`
- 其他特殊目标类型暂未实现

响应示例：

```json
{
  "ok": true,
  "request_id": "req_20260310_0004",
  "data": {
    "action": "play_card",
    "status": "completed",
    "stable": true,
    "message": "Action completed.",
    "state": {
      "state_version": 1,
      "run_id": "WXJVZBQFK2",
      "screen": "COMBAT",
      "in_combat": true,
      "turn": 1,
      "available_actions": [
        "end_turn"
      ]
    }
  }
}
```

## MCP 映射

- `get_game_state` -> `GET /state`
- `get_available_actions` -> `GET /actions/available`
- 所有动作类工具 -> `POST /action`

## 当前已知限制

- 动作完成时的 `stable` 语义还会继续收紧
- 非战斗状态的结构化数据尚未实现
- `play_card` 目前只覆盖最小可用链路，不追求全目标类型
