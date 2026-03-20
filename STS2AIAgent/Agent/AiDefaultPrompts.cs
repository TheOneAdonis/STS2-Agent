using System.Text.Json;

namespace STS2AIAgent.Agent;

internal static class AiDefaultPrompts
{
    private sealed record PromptProfile(string Id, string Name, params string[] Aliases);

    private sealed class PromptTemplate
    {
        public Dictionary<string, string>? character_combat_prompts { get; init; }

        public Dictionary<string, string>? character_route_prompts { get; init; }
    }

    private sealed record PromptMaps(
        Dictionary<string, string> Combat,
        Dictionary<string, string> Route);

    private static readonly StringComparer PromptKeyComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly PromptProfile[] Profiles =
    [
        new("IRONCLAD", "Ironclad", "铁甲战士"),
        new("SILENT", "Silent", "静默猎手"),
        new("REGENT", "Regent"),
        new("NECROBINDER", "Necrobinder"),
        new("DEFECT", "Defect", "故障机器人")
    ];

    private static readonly IReadOnlyDictionary<string, string> CanonicalKeys = BuildCanonicalKeys();
    private static readonly Lazy<PromptMaps> CachedPromptMaps = new(LoadPromptMaps, isThreadSafe: true);

    public static Dictionary<string, string> MergeCombatPrompts(IReadOnlyDictionary<string, string>? prompts)
    {
        return MergePrompts(prompts, CachedPromptMaps.Value.Combat);
    }

    public static Dictionary<string, string> MergeRoutePrompts(IReadOnlyDictionary<string, string>? prompts)
    {
        return MergePrompts(prompts, CachedPromptMaps.Value.Route);
    }

    public static string ResolvePromptKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var trimmed = key.Trim();
        return CanonicalKeys.TryGetValue(trimmed, out var canonicalKey)
            ? canonicalKey
            : trimmed;
    }

    public static string GetDefaultPrompt(bool isCombat, string? characterId, string? characterName)
    {
        var promptMap = isCombat ? CachedPromptMaps.Value.Combat : CachedPromptMaps.Value.Route;
        if (TryReadPrompt(promptMap, characterId, out var prompt) ||
            TryReadPrompt(promptMap, characterName, out prompt))
        {
            return prompt;
        }

        return string.Empty;
    }

    public static bool TryReadTemplateJson(out string json)
    {
        var path = AiRuntimePaths.BundledConfigPath;
        if (File.Exists(path))
        {
            json = File.ReadAllText(path);
            return true;
        }

        json = string.Empty;
        return false;
    }

    private static PromptMaps LoadPromptMaps()
    {
        if (!TryReadTemplateJson(out var json))
        {
            return new PromptMaps(
                new Dictionary<string, string>(PromptKeyComparer),
                new Dictionary<string, string>(PromptKeyComparer));
        }

        try
        {
            var template = JsonSerializer.Deserialize<PromptTemplate>(json, JsonOptions) ?? new PromptTemplate();
            return new PromptMaps(
                SanitizePromptMap(template.character_combat_prompts),
                SanitizePromptMap(template.character_route_prompts));
        }
        catch
        {
            return new PromptMaps(
                new Dictionary<string, string>(PromptKeyComparer),
                new Dictionary<string, string>(PromptKeyComparer));
        }
    }

    private static Dictionary<string, string> MergePrompts(IReadOnlyDictionary<string, string>? prompts, IReadOnlyDictionary<string, string> defaults)
    {
        var merged = SanitizePromptMap(prompts);
        foreach (var pair in defaults)
        {
            if (!merged.ContainsKey(pair.Key))
            {
                merged[pair.Key] = pair.Value;
            }
        }

        return merged;
    }

    private static Dictionary<string, string> SanitizePromptMap(IReadOnlyDictionary<string, string>? prompts)
    {
        var sanitized = new Dictionary<string, string>(PromptKeyComparer);
        if (prompts == null)
        {
            return sanitized;
        }

        foreach (var pair in prompts)
        {
            var key = ResolvePromptKey(pair.Key);
            var value = pair.Value?.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            sanitized[key] = value;
        }

        return sanitized;
    }

    private static bool TryReadPrompt(IReadOnlyDictionary<string, string> prompts, string? key, out string prompt)
    {
        prompt = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalizedKey = ResolvePromptKey(key);
        if (!string.IsNullOrWhiteSpace(normalizedKey) &&
            prompts.TryGetValue(normalizedKey, out var normalizedPrompt) &&
            !string.IsNullOrWhiteSpace(normalizedPrompt))
        {
            prompt = normalizedPrompt;
            return true;
        }

        return false;
    }

    private static Dictionary<string, string> BuildCanonicalKeys()
    {
        var aliases = new Dictionary<string, string>(PromptKeyComparer);
        foreach (var profile in Profiles)
        {
            aliases[profile.Id] = profile.Id;
            aliases[profile.Name] = profile.Id;
            foreach (var alias in profile.Aliases)
            {
                aliases[alias] = profile.Id;
            }
        }

        return aliases;
    }
}
