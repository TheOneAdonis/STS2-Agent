namespace STS2AIAgent.Desktop;

public sealed class AgentConfig
{
    private static readonly StringComparer PromptKeyComparer = StringComparer.OrdinalIgnoreCase;

    public bool enable_agent { get; set; }

    public string provider { get; set; } = "openai_compatible";

    public string base_url { get; set; } = "https://api.openai.com/v1";

    public string model { get; set; } = "gpt-4.1-mini";

    public string api_key { get; set; } = string.Empty;

    public double temperature { get; set; } = 0.2d;

    public bool auto_execute { get; set; }

    public bool auto_combat_loop { get; set; }

    public bool debug_mode { get; set; }

    public Dictionary<string, string> character_combat_prompts { get; set; } = new(PromptKeyComparer);

    public Dictionary<string, string> character_route_prompts { get; set; } = new(PromptKeyComparer);

    public AgentConfig Clone()
    {
        return new AgentConfig
        {
            enable_agent = enable_agent,
            provider = provider,
            base_url = base_url,
            model = model,
            api_key = api_key,
            temperature = temperature,
            auto_execute = auto_execute,
            auto_combat_loop = auto_combat_loop,
            debug_mode = debug_mode,
            character_combat_prompts = new Dictionary<string, string>(character_combat_prompts, PromptKeyComparer),
            character_route_prompts = new Dictionary<string, string>(character_route_prompts, PromptKeyComparer)
        };
    }
}

public sealed class ApiEnvelope<T>
{
    public bool ok { get; set; }

    public string request_id { get; set; } = string.Empty;

    public T? data { get; set; }

    public ApiError? error { get; set; }
}

public sealed class ApiError
{
    public string code { get; set; } = string.Empty;

    public string message { get; set; } = string.Empty;
}

public sealed class ServiceHealthPayload
{
    public string service { get; set; } = string.Empty;

    public string mod_version { get; set; } = string.Empty;

    public string protocol_version { get; set; } = string.Empty;

    public string game_version { get; set; } = string.Empty;

    public string status { get; set; } = string.Empty;
}

public sealed class AgentSnapshot
{
    public bool is_busy { get; set; }

    public bool agent_enabled { get; set; }

    public bool automation_paused { get; set; }

    public bool auto_combat_loop { get; set; }

    public string active_runtime { get; set; } = "route";

    public string status { get; set; } = "Idle";

    public string state_summary { get; set; } = string.Empty;

    public string plan_summary { get; set; } = string.Empty;

    public string reasoning { get; set; } = string.Empty;

    public string pending_action { get; set; } = string.Empty;

    public bool has_pending_action { get; set; }

    public string last_action_result { get; set; } = string.Empty;

    public string error { get; set; } = string.Empty;

    public string current_character_id { get; set; } = string.Empty;

    public string current_character_name { get; set; } = string.Empty;

    public string current_screen { get; set; } = "UNKNOWN";

    public string session_phase { get; set; } = "menu";

    public bool can_start_automation { get; set; }

    public string start_block_reason { get; set; } = string.Empty;

    public AgentConfig config { get; set; } = new();

    public string config_path { get; set; } = string.Empty;

    public string knowledge_root { get; set; } = string.Empty;

    public List<RuntimeContextSnapshot> runtime_contexts { get; set; } = new();

    public List<AgentLogEntry> logs { get; set; } = new();
}

public sealed class RuntimeContextSnapshot
{
    public string runtime { get; set; } = "route";

    public string status { get; set; } = "Idle";

    public string plan_summary { get; set; } = string.Empty;

    public string reasoning { get; set; } = string.Empty;

    public string pending_action { get; set; } = string.Empty;

    public bool has_pending_action { get; set; }

    public string last_action_result { get; set; } = string.Empty;

    public string error { get; set; } = string.Empty;

    public List<string> recent_notes { get; set; } = new();
}

public sealed class AgentLogEntry
{
    public DateTime TimestampUtc { get; set; }

    public string Level { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public sealed class ConnectivityProbeResult
{
    public bool ok { get; set; }

    public DateTime tested_at_utc { get; set; }

    public string provider { get; set; } = string.Empty;

    public string base_url { get; set; } = string.Empty;

    public string model { get; set; } = string.Empty;

    public string reply { get; set; } = string.Empty;
}
