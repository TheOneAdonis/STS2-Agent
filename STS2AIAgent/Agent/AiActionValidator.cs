using STS2AIAgent.Game;

namespace STS2AIAgent.Agent;

internal sealed class AiActionValidator
{
    private static readonly HashSet<string> OptionIndexActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "choose_timeline_epoch",
        "choose_map_node",
        "claim_reward",
        "choose_reward_card",
        "select_deck_card",
        "choose_treasure_relic",
        "choose_event_option",
        "choose_rest_option",
        "buy_card",
        "buy_relic",
        "buy_potion",
        "select_character",
        "use_potion",
        "discard_potion"
    };

    public bool TryBuildValidatedRequest(GameStatePayload state, AiDecisionResult decision, out ActionRequest? request, out string error)
    {
        request = null;
        error = string.Empty;

        if (decision == null || !decision.action.HasAction)
        {
            error = "No executable action was suggested.";
            return false;
        }

        var actionName = decision.action.name.Trim();
        if (!state.available_actions.Contains(actionName, StringComparer.OrdinalIgnoreCase))
        {
            error = $"Suggested action '{actionName}' is no longer available.";
            return false;
        }

        if (string.Equals(actionName, "play_card", StringComparison.OrdinalIgnoreCase))
        {
            if (state.combat == null)
            {
                error = "Combat state is unavailable for play_card.";
                return false;
            }

            if (decision.action.card_index == null)
            {
                error = "play_card requires card_index.";
                return false;
            }

            var cardIndex = decision.action.card_index.Value;
            if (cardIndex < 0 || cardIndex >= state.combat.hand.Length)
            {
                error = $"card_index {cardIndex} is out of range.";
                return false;
            }

            var card = state.combat.hand[cardIndex];
            if (!card.playable)
            {
                error = $"Card at index {cardIndex} is not currently playable.";
                return false;
            }

            if (card.requires_target)
            {
                if (decision.action.target_index == null)
                {
                    error = "play_card requires target_index for the selected card.";
                    return false;
                }

                if (!card.valid_target_indices.Contains(decision.action.target_index.Value))
                {
                    error = $"target_index {decision.action.target_index.Value} is invalid for card_index {cardIndex}.";
                    return false;
                }
            }

            request = BuildRequest(actionName, decision.action);
            return true;
        }

        if (OptionIndexActions.Contains(actionName) && decision.action.option_index == null)
        {
            error = $"{actionName} requires option_index.";
            return false;
        }

        if (decision.action.option_index != null &&
            !ValidateOptionIndexAction(state, actionName, decision.action.option_index.Value, out error))
        {
            return false;
        }

        request = BuildRequest(actionName, decision.action);
        return true;
    }

    public static string FormatAction(AiActionSuggestion action)
    {
        if (action == null || !action.HasAction)
        {
            return "正在观察";
        }

        var parts = new List<string> { $"动作={action.name}" };
        if (action.card_index != null)
        {
            parts.Add($"卡牌索引={action.card_index.Value}");
        }

        if (action.target_index != null)
        {
            parts.Add($"目标索引={action.target_index.Value}");
        }

        if (action.option_index != null)
        {
            parts.Add($"选项索引={action.option_index.Value}");
        }

        return string.Join("，", parts);
    }

    private static ActionRequest BuildRequest(string actionName, AiActionSuggestion action)
    {
        return new ActionRequest
        {
            action = actionName,
            card_index = action.card_index,
            target_index = action.target_index,
            option_index = action.option_index,
            client_context = new
            {
                source = "in_game_agent",
                mode = "single_step"
            }
        };
    }

    private static bool ValidateOptionIndexAction(GameStatePayload state, string actionName, int optionIndex, out string error)
    {
        error = string.Empty;
        if (optionIndex < 0)
        {
            error = $"{actionName} option_index must be >= 0.";
            return false;
        }

        return actionName.ToLowerInvariant() switch
        {
            "choose_timeline_epoch" => ValidateTimeline(state, optionIndex, out error),
            "choose_map_node" => ValidateCount(optionIndex, state.map?.available_nodes.Length ?? 0, actionName, out error),
            "claim_reward" => ValidateCount(optionIndex, state.reward?.rewards.Length ?? 0, actionName, out error),
            "choose_reward_card" => ValidateCount(optionIndex, state.reward?.card_options.Length ?? 0, actionName, out error),
            "select_deck_card" => ValidateCount(optionIndex, state.selection?.cards.Length ?? 0, actionName, out error),
            "choose_treasure_relic" => ValidateCount(optionIndex, state.chest?.relic_options.Length ?? 0, actionName, out error),
            "choose_event_option" => ValidateEventOption(state, optionIndex, out error),
            "choose_rest_option" => ValidateRestOption(state, optionIndex, out error),
            "buy_card" => ValidateShopCard(state, optionIndex, out error),
            "buy_relic" => ValidateShopRelic(state, optionIndex, out error),
            "buy_potion" => ValidateShopPotion(state, optionIndex, out error),
            "select_character" => ValidateCharacter(state, optionIndex, out error),
            "use_potion" => ValidatePotionUse(state, optionIndex, out error),
            "discard_potion" => ValidatePotionDiscard(state, optionIndex, out error),
            _ => true
        };
    }

    private static bool ValidateTimeline(GameStatePayload state, int optionIndex, out string error)
    {
        if (!ValidateCount(optionIndex, state.timeline?.slots.Length ?? 0, "choose_timeline_epoch", out error))
        {
            return false;
        }

        var slot = state.timeline!.slots[optionIndex];
        if (!slot.is_actionable)
        {
            error = $"Timeline slot {optionIndex} is not actionable.";
            return false;
        }

        return true;
    }

    private static bool ValidateEventOption(GameStatePayload state, int optionIndex, out string error)
    {
        if (!ValidateCount(optionIndex, state.@event?.options.Length ?? 0, "choose_event_option", out error))
        {
            return false;
        }

        var option = state.@event!.options[optionIndex];
        if (option.is_locked)
        {
            error = $"Event option {optionIndex} is locked.";
            return false;
        }

        return true;
    }

    private static bool ValidateRestOption(GameStatePayload state, int optionIndex, out string error)
    {
        if (!ValidateCount(optionIndex, state.rest?.options.Length ?? 0, "choose_rest_option", out error))
        {
            return false;
        }

        var option = state.rest!.options[optionIndex];
        if (!option.is_enabled)
        {
            error = $"Rest option {optionIndex} is not enabled.";
            return false;
        }

        return true;
    }

    private static bool ValidateShopCard(GameStatePayload state, int optionIndex, out string error)
    {
        if (!ValidateCount(optionIndex, state.shop?.cards.Length ?? 0, "buy_card", out error))
        {
            return false;
        }

        var item = state.shop!.cards[optionIndex];
        if (!item.is_stocked || !item.enough_gold)
        {
            error = $"Shop card {optionIndex} is unavailable or unaffordable.";
            return false;
        }

        return true;
    }

    private static bool ValidateShopRelic(GameStatePayload state, int optionIndex, out string error)
    {
        if (!ValidateCount(optionIndex, state.shop?.relics.Length ?? 0, "buy_relic", out error))
        {
            return false;
        }

        var item = state.shop!.relics[optionIndex];
        if (!item.is_stocked || !item.enough_gold)
        {
            error = $"Shop relic {optionIndex} is unavailable or unaffordable.";
            return false;
        }

        return true;
    }

    private static bool ValidateShopPotion(GameStatePayload state, int optionIndex, out string error)
    {
        if (!ValidateCount(optionIndex, state.shop?.potions.Length ?? 0, "buy_potion", out error))
        {
            return false;
        }

        var item = state.shop!.potions[optionIndex];
        if (!item.is_stocked || !item.enough_gold)
        {
            error = $"Shop potion {optionIndex} is unavailable or unaffordable.";
            return false;
        }

        return true;
    }

    private static bool ValidateCharacter(GameStatePayload state, int optionIndex, out string error)
    {
        if (!ValidateCount(optionIndex, state.character_select?.characters.Length ?? 0, "select_character", out error))
        {
            return false;
        }

        var option = state.character_select!.characters[optionIndex];
        if (option.is_locked)
        {
            error = $"Character option {optionIndex} is locked.";
            return false;
        }

        return true;
    }

    private static bool ValidatePotionUse(GameStatePayload state, int optionIndex, out string error)
    {
        if (!ValidateCount(optionIndex, state.run?.potions.Length ?? 0, "use_potion", out error))
        {
            return false;
        }

        var potion = state.run!.potions[optionIndex];
        if (!potion.occupied || !potion.can_use)
        {
            error = $"Potion slot {optionIndex} cannot be used right now.";
            return false;
        }

        return true;
    }

    private static bool ValidatePotionDiscard(GameStatePayload state, int optionIndex, out string error)
    {
        if (!ValidateCount(optionIndex, state.run?.potions.Length ?? 0, "discard_potion", out error))
        {
            return false;
        }

        var potion = state.run!.potions[optionIndex];
        if (!potion.occupied || !potion.can_discard)
        {
            error = $"Potion slot {optionIndex} cannot be discarded right now.";
            return false;
        }

        return true;
    }

    private static bool ValidateCount(int optionIndex, int count, string actionName, out string error)
    {
        if (optionIndex >= count)
        {
            error = $"{actionName} option_index {optionIndex} is out of range.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
