using System.Reflection;
using System.Text.Json;

namespace STS2AIAgent.Agent;

internal sealed class AiAgentConfig
{
    private static readonly StringComparer PromptKeyComparer = StringComparer.OrdinalIgnoreCase;

    public bool enable_agent { get; init; }

    public string provider { get; init; } = "openai_compatible";

    public string base_url { get; init; } = "https://api.openai.com/v1";

    public string model { get; init; } = "gpt-4.1-mini";

    public string api_key { get; init; } = string.Empty;

    public double temperature { get; init; } = 0.2d;

    public bool auto_execute { get; init; }

    public bool auto_combat_loop { get; init; }

    public bool debug_mode { get; init; }

    public Dictionary<string, string> character_combat_prompts { get; init; } = new(PromptKeyComparer);

    public Dictionary<string, string> character_route_prompts { get; init; } = new(PromptKeyComparer);

    public AiAgentConfig Clone()
    {
        return new AiAgentConfig
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

    public bool IsEquivalentTo(AiAgentConfig? other)
    {
        if (other == null)
        {
            return false;
        }

        return enable_agent == other.enable_agent &&
            string.Equals(provider, other.provider, StringComparison.Ordinal) &&
            string.Equals(base_url, other.base_url, StringComparison.Ordinal) &&
            string.Equals(model, other.model, StringComparison.Ordinal) &&
            string.Equals(api_key, other.api_key, StringComparison.Ordinal) &&
            temperature.Equals(other.temperature) &&
            auto_execute == other.auto_execute &&
            auto_combat_loop == other.auto_combat_loop &&
            debug_mode == other.debug_mode &&
            DictionaryEquals(character_combat_prompts, other.character_combat_prompts) &&
            DictionaryEquals(character_route_prompts, other.character_route_prompts);
    }

    public AiAgentConfig Sanitize()
    {
        var normalizedProvider = string.IsNullOrWhiteSpace(provider)
            ? "openai_compatible"
            : provider.Trim();
        var normalizedBaseUrl = string.IsNullOrWhiteSpace(base_url)
            ? "https://api.openai.com/v1"
            : base_url.Trim().TrimEnd('/');
        var normalizedModel = string.IsNullOrWhiteSpace(model)
            ? "gpt-4.1-mini"
            : model.Trim();
        var normalizedTemperature = Math.Clamp(temperature, 0d, 2d);

        return new AiAgentConfig
        {
            enable_agent = enable_agent,
            provider = normalizedProvider,
            base_url = normalizedBaseUrl,
            model = normalizedModel,
            api_key = api_key?.Trim() ?? string.Empty,
            temperature = normalizedTemperature,
            auto_execute = auto_execute,
            auto_combat_loop = auto_combat_loop,
            debug_mode = debug_mode,
            character_combat_prompts = AiDefaultPrompts.MergeCombatPrompts(character_combat_prompts),
            character_route_prompts = AiDefaultPrompts.MergeRoutePrompts(character_route_prompts)
        };
    }

    public string GetCharacterPrompt(AiRuntimeKind runtimeKind, string? characterId, string? characterName)
    {
        var promptMap = runtimeKind == AiRuntimeKind.Combat
            ? character_combat_prompts
            : character_route_prompts;

        if (TryReadPrompt(promptMap, characterId, out var prompt) ||
            TryReadPrompt(promptMap, characterName, out prompt))
        {
            return prompt;
        }

        return AiDefaultPrompts.GetDefaultPrompt(
            runtimeKind == AiRuntimeKind.Combat,
            characterId,
            characterName);
    }

    private static bool TryReadPrompt(IReadOnlyDictionary<string, string> prompts, string? key, out string prompt)
    {
        prompt = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalizedKey = AiDefaultPrompts.ResolvePromptKey(key);
        if (!string.IsNullOrWhiteSpace(normalizedKey) &&
            prompts.TryGetValue(normalizedKey, out var normalizedPrompt) &&
            !string.IsNullOrWhiteSpace(normalizedPrompt))
        {
            prompt = normalizedPrompt;
            return true;
        }

        if (!prompts.TryGetValue(key.Trim(), out var rawPrompt) || string.IsNullOrWhiteSpace(rawPrompt))
        {
            return false;
        }

        prompt = rawPrompt;
        return true;
    }

    private static bool DictionaryEquals(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string>? right)
    {
        left ??= new Dictionary<string, string>(PromptKeyComparer);
        right ??= new Dictionary<string, string>(PromptKeyComparer);
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) ||
                !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

internal static class AiSecretMasker
{
    public static string Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty)";
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= 8)
        {
            return new string('*', trimmed.Length);
        }

        return $"{trimmed[..4]}***{trimmed[^4..]}";
    }
}

internal static class AiRuntimePaths
{
    private const string AppDirName = "creative-ai";
    private const string KnowledgeDirName = "knowledge";
    private const string ConfigDirName = "config";

    public static string AppRoot
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(baseDir))
            {
                baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
            }

            return Path.Combine(baseDir, AppDirName);
        }
    }

    public static string ConfigRoot => Path.Combine(AppRoot, ConfigDirName);

    public static string ConfigPath => Path.Combine(ConfigRoot, "in-game-agent.json");

    public static string ModRoot
    {
        get
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var modDir = Path.GetDirectoryName(assemblyPath);
            return string.IsNullOrWhiteSpace(modDir) ? AppContext.BaseDirectory : modDir;
        }
    }

    public static string BundledConfigPath => Path.Combine(ModRoot, "in-game-agent.json");

    public static string LogRoot => Path.Combine(AppRoot, "logs");

    public static string UiLogPath => Path.Combine(LogRoot, "ui-runtime.log");

    public static string AiLogPath => Path.Combine(LogRoot, "agent-runtime.log");

    public static string LlmDebugJsonlPath => Path.Combine(LogRoot, "llm-debug.jsonl");

    public static string KnowledgeRoot
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("STS2_AGENT_KNOWLEDGE_DIR")?.Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }

            var runtimeDir = Environment.GetEnvironmentVariable("STS2_AGENT_RUNTIME_DIR")?.Trim();
            if (!string.IsNullOrWhiteSpace(runtimeDir))
            {
                return Path.Combine(Path.GetFullPath(runtimeDir), KnowledgeDirName);
            }

            return Path.Combine(AppRoot, KnowledgeDirName);
        }
    }
}

internal sealed class AiConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public AiAgentConfig Load()
    {
        EnsureConfigFileExists();

        var path = AiRuntimePaths.ConfigPath;
        if (!File.Exists(path))
        {
            return new AiAgentConfig().Sanitize();
        }

        try
        {
            var json = File.ReadAllText(path);
            return (JsonSerializer.Deserialize<AiAgentConfig>(json, JsonOptions) ?? new AiAgentConfig()).Sanitize();
        }
        catch
        {
            return new AiAgentConfig().Sanitize();
        }
    }

    public bool TryLoad(out AiAgentConfig config, out string error)
    {
        EnsureConfigFileExists();

        var path = AiRuntimePaths.ConfigPath;
        if (!File.Exists(path))
        {
            config = new AiAgentConfig().Sanitize();
            error = string.Empty;
            return true;
        }

        try
        {
            var json = File.ReadAllText(path);
            config = (JsonSerializer.Deserialize<AiAgentConfig>(json, JsonOptions) ?? new AiAgentConfig()).Sanitize();
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            config = new AiAgentConfig().Sanitize();
            error = ex.Message;
            return false;
        }
    }

    public DateTime? GetLastWriteTimeUtc()
    {
        var path = AiRuntimePaths.ConfigPath;
        if (!File.Exists(path))
        {
            return null;
        }

        return File.GetLastWriteTimeUtc(path);
    }

    public void Save(AiAgentConfig config)
    {
        var path = AiRuntimePaths.ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config.Sanitize(), JsonOptions));
    }

    private static void EnsureConfigFileExists()
    {
        var path = AiRuntimePaths.ConfigPath;
        if (File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (AiDefaultPrompts.TryReadTemplateJson(out var json))
        {
            File.WriteAllText(path, json);
            return;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(new AiAgentConfig().Sanitize(), JsonOptions));
    }
}
