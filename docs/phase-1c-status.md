# Phase 1C Status

更新时间：2026-03-10

## 已实现接口

- `GET /health`
- `GET /state`
- `GET /actions/available`
- `POST /action`

## 已实现动作

- `end_turn`
- `play_card`

## `GET /state` 当前最小返回

- 顶层字段：`state_version`、`run_id`、`screen`、`in_combat`、`turn`、`available_actions`
- 战斗字段：`combat.player`、`combat.hand`、`combat.enemies`

## 已覆盖的 `screen`

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
- `UNKNOWN`

## `play_card` 当前能力边界

- 需要 `card_index`
- 仅支持当前手牌中的卡牌
- `TargetType.AnyEnemy` 需要额外传 `target_index`
- 其他需要特殊目标的类型暂未实现

## 当前限制

- Windows 下无法对已加载的 Mod DLL 热替换，安装新版后必须重启游戏
- `play_card` 的稳定态判断已做最小实现，但仍需继续收紧，避免过早返回 `completed`
- 地图、事件、奖励、商店等非战斗动作尚未接入

## 下一步

1. 重启游戏并安装最新 Mod
2. 在实战中验证 `combat.hand` / `combat.enemies`
3. 实测 `play_card`
4. 根据实测结果继续修正稳定态与目标类型处理
