from __future__ import annotations

from typing import Any

from fastmcp import FastMCP

from .client import Sts2Client


def create_server(client: Sts2Client | None = None) -> FastMCP:
    sts2 = client or Sts2Client()
    mcp = FastMCP("STS2 AI Agent")

    @mcp.tool
    def health_check() -> dict[str, Any]:
        """检查本地 STS2 AI Agent Mod 是否已加载并可访问。"""
        return sts2.get_health()

    @mcp.tool
    def get_game_state() -> dict[str, Any]:
        """读取当前游戏状态快照。"""
        return sts2.get_state()

    @mcp.tool
    def get_available_actions() -> list[dict[str, Any]]:
        """读取当前状态下允许执行的动作列表。"""
        return sts2.get_available_actions()

    @mcp.tool
    def end_turn() -> dict[str, Any]:
        """在当前战斗中结束玩家回合。仅在 `available_actions` 包含 `end_turn` 时调用。"""
        return sts2.end_turn()

    @mcp.tool
    def play_card(card_index: int, target_index: int | None = None) -> dict[str, Any]:
        """打出当前手牌中的一张牌。若卡牌需要敌方目标，则必须传 `target_index`。"""
        return sts2.play_card(card_index=card_index, target_index=target_index)

    @mcp.tool
    def choose_map_node(option_index: int) -> dict[str, Any]:
        """在地图界面选择一个可前往节点。`option_index` 对应 `map.available_nodes[index]`。"""
        return sts2.choose_map_node(option_index=option_index)

    @mcp.tool
    def collect_rewards_and_proceed() -> dict[str, Any]:
        """在奖励结算界面收取奖励、自动选择卡牌奖励，并点击继续。"""
        return sts2.collect_rewards_and_proceed()

    @mcp.tool
    def claim_reward(option_index: int) -> dict[str, Any]:
        """在奖励结算界面领取一个奖励。`option_index` 对应 `reward.rewards[index]`。"""
        return sts2.claim_reward(option_index=option_index)

    @mcp.tool
    def choose_reward_card(option_index: int) -> dict[str, Any]:
        """在卡牌奖励界面选择一张牌。`option_index` 对应 `reward.card_options[index]`。"""
        return sts2.choose_reward_card(option_index=option_index)

    @mcp.tool
    def skip_reward_cards() -> dict[str, Any]:
        """在卡牌奖励界面跳过拿牌。仅在 `reward.alternatives` 包含跳过按钮时调用。"""
        return sts2.skip_reward_cards()

    @mcp.tool
    def select_deck_card(option_index: int) -> dict[str, Any]:
        """在牌库选牌界面选择一张牌。`option_index` 对应 `selection.cards[index]`。"""
        return sts2.select_deck_card(option_index=option_index)

    @mcp.tool
    def proceed() -> dict[str, Any]:
        """点击当前界面的继续按钮。适用于带 `ProceedButton` 的非战斗界面。"""
        return sts2.proceed()

    return mcp


def main() -> None:
    create_server().run(transport="stdio", show_banner=False)


if __name__ == "__main__":
    main()
