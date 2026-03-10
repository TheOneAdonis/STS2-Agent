---
name: sts2-mcp-player
description: Use this skill when operating the Slay the Spire 2 MCP mod. It reduces action-selection mistakes by enforcing a room-by-room workflow, state-first decision loop, and the recommended high-level tools for rewards, shops, rest sites, events, and combat.
---

# STS2 MCP Player

Use this skill when driving the STS2 MCP mod as a gameplay agent.

## Core Loop

1. Call `health_check` once at the start of the session.
2. Before every decision, call `get_game_state`.
3. Only call tools that are present in `available_actions`.
4. After every action, call `get_game_state` again before deciding the next step.

Do not assume an action succeeded just because the tool returned `completed`; always inspect the returned `state` or fetch a fresh one.

## Preferred Actions

- Reward rooms: prefer `collect_rewards_and_proceed` unless you are making a deliberate card choice.
- Card rewards: `skip_reward_cards` dismisses the card-choice overlay, but the underlying reward may still remain claimable. Re-read state after skipping.
- Rest sites: use `choose_rest_option`; if smithing opens `CARD_SELECTION`, finish it with `select_deck_card`, then `proceed`.
- Shops: first `open_shop_inventory`; leave the inner inventory with `close_shop_inventory`; leave the room with `proceed`.
- Chests: `open_chest`, then `choose_treasure_relic`, then `proceed`.
- Main menu: prefer `continue_run` if resuming, otherwise `open_character_select`.
- Timeline gate: if `open_timeline` is available, finish timeline interactions before trying to start a run.

## Screen-Specific Rules

- `COMBAT`: do not call room tools. Use `play_card`, `end_turn`, `use_potion`, `discard_potion`.
- `REWARD`: if `available_actions` includes `proceed`, it is safe to leave remaining rewards behind. If it does not, keep resolving rewards or use `collect_rewards_and_proceed`.
- `CARD_SELECTION`: finish the selection first. Common follow-up is `select_deck_card`.
- `MODAL`: resolve `confirm_modal` or `dismiss_modal` before anything else.
- `EVENT`: use `choose_event_option`; if the event starts combat, expect the flow `EVENT -> COMBAT -> EVENT/MAP`.
- `UNKNOWN`: immediately re-read state once. If it persists, stop making assumptions and inspect the available payloads.

## Common Mistakes To Avoid

- Do not use `proceed` on reward-card screens where `pending_card_choice = true`.
- Do not assume `skip_reward_cards` consumes the card reward. Check whether the reward item is still present after the overlay closes.
- Do not keep using old indexes after state changes; recompute from the latest payload.
- Do not assume `selection.kind == "deck_card_select"` is the only card-selection variant. Handle upgrade/transform/enchant variants too.
- Do not assume `shop.is_open=true` means you are done; you may still need `close_shop_inventory` before leaving.

## Debug Policy

`run_console_command` is development-only.

- Only use it if the environment explicitly enables `STS2_ENABLE_DEBUG_ACTIONS=1`.
- Do not depend on it for normal gameplay or release validation.
- If it is unavailable, continue with the regular MCP flow.

## Minimal Decision Heuristics

- In combat, prefer playable cards that spend available energy efficiently and avoid ending turn with obvious free value unused.
- In rewards, only take cards that clearly improve the current deck; otherwise skip.
- In shops, avoid spending all gold before checking relics and card removal.
- In events, prefer non-locked options and re-read state after every branch because events can mutate in place.
