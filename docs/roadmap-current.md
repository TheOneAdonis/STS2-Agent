# STS2 AI Agent 当前开发路线图

更新时间：`2026-03-10`

---

## 角色分工

> **2026-03-10 角色切换**：Claude 接手全部主线开发（C# Mod + Python MCP + 文档），Codex 转为代码审查与风险提示。

| 角色 | 职责 | 文件范围 |
| --- | --- | --- |
| **Claude（主开发）** | 功能实现、重构、联调、文档同步 | `STS2AIAgent/**/*.cs`、`mcp_server/src/sts2_mcp/*.py`、`docs/*` |
| **Codex（审查）** | 代码 Review、风险提示、回归审查 | 只读所有文件，不直接修改 |

### 审查协议

Claude 每完成一个功能阶段，向 Codex 提交具体 diff 或文件进行 Review。Codex 审查重点：

- 线程安全（游戏线程 vs 线程池）
- Godot 对象生命周期（IsInstanceValid 检查）
- 错误处理完整性
- 命名与结构一致性
- 是否破坏既有动作语义

---

## 总体进度

| 阶段 | 描述 | 状态 |
| --- | --- | --- |
| Phase 0A | 环境搭建 | 已完成 |
| Phase 0B | 逆向侦察 | 已完成 |
| Phase 1A | 协议冻结 | 已完成 |
| Phase 1B | Mod 骨架 + `/health` | 已完成 |
| Phase 1C | 最小纵切 | 已完成 |
| Phase 2 | 战斗状态提取 | 已完成 |
| Phase 3 | 战斗动作执行 | 已完成 |
| Phase 4A | 地图 / 奖励 / 宝箱 | 代码已完成（含宝箱 relic 选择），部分已实机验证 |
| Phase 4B | 事件 / 休息点 | 已完成（代码+MCP+文档），待实机验证 |
| Phase 4C | 商店 | 已完成（代码+MCP+文档），待实机验证 |
| Phase 5 | MCP 完整化 | 基础已完成，随 4B/4C 同步扩展 |
| Phase 6 | 集成与回归 | 未开始 |

---

## 当前能力盘点

### HTTP API

| 端点 | 状态 |
| --- | --- |
| `GET /health` | 已验证 |
| `GET /state` | 已验证 |
| `GET /actions/available` | 已验证 |
| `POST /action` | 已验证 |

### 已实现动作

| 动作 | 实机状态 |
| --- | --- |
| `end_turn` | 已验证 |
| `play_card` | 已验证 |
| `choose_map_node` | 已验证 |
| `proceed` | 已验证 |
| `claim_reward` | 已验证 |
| `choose_reward_card` | 已验证 |
| `collect_rewards_and_proceed` | 已验证 |
| `skip_reward_cards` | 待验证 |
| `select_deck_card` | 待验证 |
| `open_chest` | 代码已完成，待验证 |
| `choose_treasure_relic` | 代码已完成，待验证 |
| `choose_event_option` | 代码已完成，待验证 |
| `choose_rest_option` | 代码已完成，待验证 |
| `open_shop_inventory` | 代码已完成，待验证 |
| `close_shop_inventory` | 代码已完成，待验证 |
| `buy_card` | 代码已完成，待验证 |
| `buy_relic` | 代码已完成，待验证 |
| `buy_potion` | 代码已完成，待验证 |
| `remove_card_at_shop` | 代码已完成，待验证 |

### 已实现状态字段

| 字段 | 状态 |
| --- | --- |
| `combat.player` / `combat.hand` / `combat.enemies` | 已实现并验证 |
| `run.deck` / `run.relics` / `run.potions` / `run.gold` | 已实现，部分已验证 |
| `map.current_node` / `map.available_nodes` | 已验证 |
| `map.rows` / `map.cols` / `map.starting_node` / `map.boss_node` / `map.second_boss_node` / `map.nodes` | 已实现，待实机验证 |
| `reward.*` / `selection.*` | 已实现，部分已验证 |
| `chest.is_opened` / `chest.has_relic_been_claimed` / `chest.relic_options` | 代码已完成，待验证 |
| `event.event_id` / `event.title` / `event.description` / `event.is_finished` / `event.options` | 代码已完成，待验证 |
| `rest.options` | 代码已完成，待验证 |
| `shop` | 代码已完成，待验证 |
| `rest` | 代码已完成，待验证 |
| `game_over` | 仍为 `null` |

### MCP 工具

当前 MCP 已注册并可用的基础工具：

- `health_check`
- `get_game_state`
- `get_available_actions`
- `end_turn`
- `play_card`
- `choose_map_node`
- `claim_reward`
- `choose_reward_card`
- `skip_reward_cards`
- `select_deck_card`
- `collect_rewards_and_proceed`
- `proceed`
- `open_chest`
- `choose_treasure_relic`
- `choose_event_option`
- `choose_rest_option`
- `open_shop_inventory`
- `close_shop_inventory`
- `buy_card`
- `buy_relic`
- `buy_potion`
- `remove_card_at_shop`

---

## 任务清单

### T-0: 接手收尾（Claude）

- [x] 更新本文档角色分工
- [x] **[A]** 修复 `WaitForNextFrameAsync` 线程安全：移除 `.ConfigureAwait(false)` 以避免脱离游戏线程
- [x] **[B]** 修复 `Router.cs` request_id 碰撞：时间戳 + 原子计数器
- [x] **[C]** 清理 `.gitignore`：添加 `.claude/`、`.serena/`、`node_modules/`
- [x] 核对 `docs/api.md` 与 C# 实现一致性

涉及文件：`GameActionService.cs`、`Router.cs`、`.gitignore`、`docs/*`

### T-1: Phase 4A 实机回归

- 实机验证 `map.nodes` 全图结构
- 实机验证 `skip_reward_cards`
- 实机验证 `select_deck_card`

> 需要启动游戏，由用户配合验证。

### T-2: Phase 4B 事件系统

- [x] 逆向 `NEventRoom` 子节点结构，找到事件选项按钮
- [x] 实现 `EventPayload`（选项列表、描述文本等）
- [x] 实现 `choose_event_option` 动作
- [x] 接入 `BuildStatePayload()` / `BuildAvailableActionNames()` / `ExecuteAsync()`
- [x] MCP：`client.py` + `server.py` 新增 `choose_event_option`
- [x] 文档：更新 `docs/api.md`
- [ ] 实机验证

涉及文件：`GameStateService.cs`、`GameActionService.cs`、`client.py`、`server.py`、`docs/api.md`

### T-3: Phase 4B 休息点系统

- [x] 逆向 `NRestSiteRoom` 子节点结构
- [x] 实现 `RestPayload`（可选动作：休息 / 升级牌 / 其他）
- [x] 实现 `choose_rest_option` 动作
- [x] 扩展 `select_deck_card` 覆盖升级牌场景（NCardGridSelectionScreen 基类统一）
- [x] MCP + 文档同步
- [ ] 实机验证

涉及文件：同 T-2

### T-4: Phase 4C 商店系统

- [x] 逆向 `NMerchantRoom` / `NMerchantInventory` / `NMerchantCardRemoval` 结构
- [x] 实现 `ShopPayload`（库存开关、商品列表、价格、删牌服务等）
- [x] 实现 `open_shop_inventory` / `close_shop_inventory`
- [x] 实现 `buy_card`、`buy_relic`、`buy_potion`、`remove_card_at_shop`
- [x] MCP + 文档同步
- [ ] 实机验证

涉及文件：同 T-2

### T-5: Game Over 状态

- 实现 `GameOverPayload`（胜负、楼层、统计）
- 接入 `BuildStatePayload()`

涉及文件：`GameStateService.cs`

### T-6: 低优先级重构

- 拆分 `GameStateService.cs` 内部 Payload 类型到 `Payloads/*.cs`
- 仅做文件组织，不改行为

---

## 执行顺序

1. **T-0**：接手收尾（当前）
2. **T-2**：事件系统（C# + MCP + 文档一站式完成）
3. **T-3**：休息点系统
4. **T-4**：商店系统（已完成，待实机验证）
5. **T-5**：Game Over
6. **T-1**：实机回归（穿插在有游戏环境时进行）
7. **T-6**：重构（所有功能稳定后）

每完成一个 T-* 任务，向 Codex 提交 diff 做 Review。

---

## 当前阻塞与风险

1. `GameStateService.cs` 是高冲突文件，T-2 / T-3 / T-4 必须串行推进。
2. Windows 下不能热替换已加载的 Mod DLL，所有实机验证都依赖"关游戏 → 安装 → 重开"。
3. STS2 更新后可能导致逆向入口失效，事件、商店、休息点都要优先找稳定入口。
4. `map.nodes` 目前只提供图结构和运行时状态，不提供内建评分；路线规划逻辑必须在上层策略做。
5. 事件 / 休息点 / 商店的 UI 结构已完成一轮逆向；游戏更新后仍需优先复核入口稳定性。
