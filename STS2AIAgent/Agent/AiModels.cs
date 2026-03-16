using STS2AIAgent.Game;

namespace STS2AIAgent.Agent;

internal enum AiRuntimeKind
{
    Combat,
    Route
}

internal sealed record AiLogEntry(DateTime TimestampUtc, string Level, string Message);

internal sealed record AiActionSuggestion
{
    public string name { get; init; } = "none";

    public int? card_index { get; init; }

    public int? target_index { get; init; }

    public int? option_index { get; init; }

    public bool HasAction =>
        !string.IsNullOrWhiteSpace(name) &&
        !string.Equals(name, "none", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(name, "wait", StringComparison.OrdinalIgnoreCase);
}

internal sealed record AiDecisionResult
{
    public string plan_summary { get; init; } = string.Empty;

    public string reasoning { get; init; } = string.Empty;

    public AiActionSuggestion action { get; init; } = new();

    public bool requires_confirmation { get; init; } = true;

    public string stop_reason { get; init; } = string.Empty;

    public string[] safety_checks { get; init; } = Array.Empty<string>();

    public string raw_response { get; init; } = string.Empty;
}

internal sealed record AiRuntimeSnapshot
{
    public bool is_busy { get; init; }

    public bool agent_enabled { get; init; }

    public bool automation_paused { get; init; }

    public bool auto_combat_loop { get; init; }

    public string active_runtime { get; init; } = "route";

    public string status { get; init; } = "Idle";

    public string state_summary { get; init; } = "No state captured yet.";

    public string plan_summary { get; init; } = string.Empty;

    public string reasoning { get; init; } = string.Empty;

    public string pending_action { get; init; } = string.Empty;

    public bool has_pending_action { get; init; }

    public string last_action_result { get; init; } = string.Empty;

    public string error { get; init; } = string.Empty;

    public string current_character_id { get; init; } = string.Empty;

    public string current_character_name { get; init; } = string.Empty;

    public string current_screen { get; init; } = "UNKNOWN";

    public string session_phase { get; init; } = "menu";

    public bool can_start_automation { get; init; }

    public string start_block_reason { get; init; } = string.Empty;

    public AiAgentConfig config { get; init; } = new();

    public string config_path { get; init; } = AiRuntimePaths.ConfigPath;

    public string knowledge_root { get; init; } = AiRuntimePaths.KnowledgeRoot;

    public IReadOnlyList<AiRuntimeContextSnapshot> runtime_contexts { get; init; } = Array.Empty<AiRuntimeContextSnapshot>();

    public IReadOnlyList<AiLogEntry> logs { get; init; } = Array.Empty<AiLogEntry>();
}

internal sealed record AiRuntimeContextSnapshot
{
    public string runtime { get; init; } = "route";

    public string status { get; init; } = "Idle";

    public string plan_summary { get; init; } = string.Empty;

    public string reasoning { get; init; } = string.Empty;

    public string pending_action { get; init; } = string.Empty;

    public bool has_pending_action { get; init; }

    public string last_action_result { get; init; } = string.Empty;

    public string error { get; init; } = string.Empty;

    public IReadOnlyList<string> recent_notes { get; init; } = Array.Empty<string>();
}

internal sealed record AiRuntimePromptContext
{
    public required AiRuntimeKind runtime_kind { get; init; }

    public IReadOnlyList<string> recent_notes { get; init; } = Array.Empty<string>();
}

internal sealed record AiConnectivityProbeResult
{
    public bool ok { get; init; }

    public DateTime tested_at_utc { get; init; }

    public string provider { get; init; } = string.Empty;

    public string base_url { get; init; } = string.Empty;

    public string model { get; init; } = string.Empty;

    public string reply { get; init; } = string.Empty;
}

internal sealed record GameStateFingerprint(string RunId, string Screen, int? Turn)
{
    public static GameStateFingerprint FromState(GameStatePayload state)
    {
        return new GameStateFingerprint(state.run_id, state.screen, state.turn);
    }

    public bool Matches(GameStatePayload state)
    {
        if (!string.Equals(RunId, state.run_id, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(Screen, state.screen, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(Screen, "COMBAT", StringComparison.Ordinal) && Turn != state.turn)
        {
            return false;
        }

        return true;
    }
}
