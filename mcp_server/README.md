# STS2 MCP Server

`mcp_server/` 提供一个最小的 `FastMCP` Server，把本地 `STS2AIAgent` Mod 暴露的 HTTP API 包装成 MCP 工具。

## 当前工具

- `health_check`
- `get_game_state`
- `get_available_actions`
- `end_turn`
- `play_card`
- `choose_map_node`
- `collect_rewards_and_proceed`
- `claim_reward`
- `choose_reward_card`
- `skip_reward_cards`
- `select_deck_card`
- `proceed`

## 环境变量

- `STS2_API_BASE_URL`
  - 默认值：`http://127.0.0.1:8080`
- `STS2_API_TIMEOUT_SECONDS`
  - 默认值：`10`

## 本地运行

```powershell
cd "C:/Users/chart/Documents/project/sp/mcp_server"
uv sync
uv run sts2-mcp-server
```

默认通过 `stdio` 运行，适合直接接到 MCP 客户端。

## 快速自测

```powershell
cd "C:/Users/chart/Documents/project/sp/mcp_server"
uv run python -c "from sts2_mcp.client import Sts2Client; import json; print(json.dumps(Sts2Client().get_state(), ensure_ascii=False, indent=2))"
```
