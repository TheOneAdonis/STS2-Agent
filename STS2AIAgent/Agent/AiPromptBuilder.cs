using System.Text;
using STS2AIAgent.Game;
using STS2AIAgent.Server;

namespace STS2AIAgent.Agent;

internal sealed record AiPrompt(string SystemPrompt, string UserPrompt);

internal sealed class AiPromptBuilder
{
    public AiPrompt Build(GameStatePayload state, AiAgentConfig config, string knowledgeSnippet, AiRuntimePromptContext runtimeContext)
    {
        var runtimeLabel = runtimeContext.runtime_kind == AiRuntimeKind.Combat ? "combat" : "route";
        var autoExecute = config.auto_execute;
        var playableHandCount = state.combat?.hand.Count(card => card.playable) ?? 0;
        var usablePotionCount = state.run?.potions.Count(potion => potion.can_use) ?? 0;
        var canEndTurn = state.available_actions.Contains("end_turn", StringComparer.OrdinalIgnoreCase);
        var characterId = state.run?.character_id ?? string.Empty;
        var characterName = state.run?.character_name ?? string.Empty;
        var characterLabel = string.IsNullOrWhiteSpace(characterName)
            ? (string.IsNullOrWhiteSpace(characterId) ? "unknown" : characterId)
            : string.IsNullOrWhiteSpace(characterId)
                ? characterName
                : $"{characterName} ({characterId})";
        var characterPrompt = config.GetCharacterPrompt(runtimeContext.runtime_kind, characterId, characterName);
        var systemPrompt = string.Join(
            "\n",
            [
                $"You are the {runtimeLabel} in-game Slay the Spire 2 agent embedded inside the mod runtime.",
                "You are playing Slay the Spire 2.",
                $"Current character: {characterLabel}.",
                "Respond with exactly one JSON object and no markdown.",
                "Use only actions that are present in available_actions.",
                "If indexes are needed, use the live indexes from the provided state.",
                "Treat the knowledge snippet as optional prior experience, not a hard rule.",
                "Plan can mention multiple future steps, but action must describe only the immediate next step.",
                "If the state is unsafe or no valid action is available, set action.name to \"none\".",
                "Keep reasoning concise and operational, not hidden chain-of-thought.",
                "All non-action fields in the JSON must be written in Chinese, including plan_summary, reasoning, stop_reason, and safety_checks.",
                "Health carries across floors and combats; it does not automatically refill each floor.",
                "Normal block is usually removed at the start of your next turn unless the live state clearly indicates it will be retained.",
                "Do not spend HP casually for short-term value unless the trade is clearly worth the long-run risk.",
                "A fixed healing opportunity normally appears only after defeating the floor boss, so survival and HP preservation matter.",
                "When the state marks a card as playable=false, treat that as authoritative even if the card text looks strong.",
                "Energy, stars, target legality, and other live combat restrictions override your intuition about what should be playable.",
                runtimeContext.runtime_kind == AiRuntimeKind.Combat
                    ? "Optimize for tactical combat sequencing, potion timing, card ordering, targeting, and turn safety."
                    : "Optimize for route planning, rewards, events, upgrades, shops, rests, and long-run value.",
                runtimeContext.runtime_kind == AiRuntimeKind.Combat
                    ? "Combat checklist: at the start of every turn, explicitly evaluate usable potions before committing to cards."
                    : "Route checklist: prefer actions that keep the run moving and avoid dead-end planning.",
                runtimeContext.runtime_kind == AiRuntimeKind.Combat
                    ? "The enemy_intents section is authoritative. Use it as the resolved enemy intent instead of guessing from raw move ids."
                    : null,
                runtimeContext.runtime_kind == AiRuntimeKind.Combat
                    ? "If no playable cards remain, decide whether a usable potion is worth using this turn; if not, choose end_turn immediately when available."
                    : "If the route state already exposes a concrete progress action, prefer that over returning none.",
                runtimeContext.runtime_kind == AiRuntimeKind.Combat
                    ? "Do not return action.name = \"none\" when end_turn is available and there are no playable cards left."
                    : "Do not return action.name = \"none\" when a safe progression action is available.",
                runtimeContext.runtime_kind == AiRuntimeKind.Combat
                    ? "For play_card, choose only from hand entries where playable=true; if playable=false and why=not_enough_energy, do not plan around that card this step."
                    : "For route decisions, prefer currently available progress actions over speculative future branches.",
                runtimeContext.runtime_kind == AiRuntimeKind.Route
                    ? "When evaluating card rewards, take a card only if it strengthens the current deck plan, patches a clear weakness, or is a genuinely strong standalone pickup; skipping is better than blindly taking the first option."
                    : null,
                runtimeContext.runtime_kind == AiRuntimeKind.Route
                    ? "Card reward decisions must account for current deck synergy, curve, defense, frontload, scaling, draw consistency, and relic interactions."
                    : null,
                string.IsNullOrWhiteSpace(characterPrompt)
                    ? "No extra character-specific guidance was provided."
                    : $"Character-specific guidance:\n{characterPrompt}",
                "JSON schema:",
                "{",
                "  \"plan_summary\": \"short next-step plan\",",
                "  \"reasoning\": \"brief reason grounded in the live state\",",
                "  \"action\": {",
                "    \"name\": \"available action name or none\",",
                "    \"card_index\": 0,",
                "    \"target_index\": 0,",
                "    \"option_index\": 0",
                "  },",
                "  \"requires_confirmation\": true,",
                "  \"stop_reason\": \"optional reason to stop\",",
                "  \"safety_checks\": [\"short safety check strings\"]",
                "}"
            ]);

        var agentViewJson = JsonHelper.Serialize(state.agent_view ?? new { });
        var recentNotes = runtimeContext.recent_notes.Count == 0
            ? "(none)"
            : string.Join("\n", runtimeContext.recent_notes.Select(note => $"- {note}"));
        var userPrompt = new StringBuilder()
            .AppendLine($"runtime: {runtimeLabel}")
            .AppendLine($"mode: {(autoExecute ? $"auto_execute_{runtimeLabel}_single_step" : $"advisory_{runtimeLabel}_single_step")}")
            .AppendLine($"character_id: {(string.IsNullOrWhiteSpace(characterId) ? "null" : characterId)}")
            .AppendLine($"character_name: {(string.IsNullOrWhiteSpace(characterName) ? "null" : characterName)}")
            .AppendLine($"screen: {state.screen}")
            .AppendLine($"run_id: {state.run_id}")
            .AppendLine($"turn: {(state.turn?.ToString() ?? "null")}")
            .AppendLine($"available_actions: [{string.Join(", ", state.available_actions)}]")
            .AppendLine($"combat_turn_state: {state.combat_turn_state}")
            .AppendLine($"combat_actions_ready: {state.combat_actions_ready.ToString().ToLowerInvariant()}")
            .AppendLine($"combat_transitioning: {state.combat_transitioning.ToString().ToLowerInvariant()}")
            .AppendLine($"combat_playable_hand_count: {playableHandCount}")
            .AppendLine($"combat_usable_potion_count: {usablePotionCount}")
            .AppendLine($"combat_end_turn_available: {canEndTurn.ToString().ToLowerInvariant()}")
            .AppendLine("enemy_intents:")
            .AppendLine(runtimeContext.runtime_kind == AiRuntimeKind.Combat
                ? BuildEnemyIntentLines(state.combat)
                : "(none)")
            .AppendLine("runtime_context:")
            .AppendLine(recentNotes)
            .AppendLine("knowledge_snippet:")
            .AppendLine(string.IsNullOrWhiteSpace(knowledgeSnippet) ? "(none)" : knowledgeSnippet)
            .AppendLine("agent_view_json:")
            .AppendLine(agentViewJson)
            .ToString();

        return new AiPrompt(systemPrompt, userPrompt);
    }

    private static string BuildEnemyIntentLines(CombatPayload? combat)
    {
        if (combat?.enemies == null || combat.enemies.Length == 0)
        {
            return "(none)";
        }

        return string.Join(
            "\n",
            combat.enemies.Select(enemy =>
            {
                var parts = new List<string>
                {
                    $"- enemy[{enemy.index}] {enemy.name}",
                    $"hp={enemy.current_hp}/{enemy.max_hp}"
                };

                if (enemy.block > 0)
                {
                    parts.Add($"block={enemy.block}");
                }

                if (!string.IsNullOrWhiteSpace(enemy.intent))
                {
                    parts.Add($"intent={enemy.intent}");
                }

                if (!string.IsNullOrWhiteSpace(enemy.intent_details) &&
                    !string.Equals(enemy.intent_details, enemy.intent, StringComparison.Ordinal))
                {
                    parts.Add($"detail={enemy.intent_details}");
                }

                if (!string.IsNullOrWhiteSpace(enemy.intent_id))
                {
                    parts.Add($"move_id={enemy.intent_id}");
                }

                return string.Join(" | ", parts);
            }));
    }
}
