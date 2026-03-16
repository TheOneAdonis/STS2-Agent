using System.Text;
using STS2AIAgent.Game;

namespace STS2AIAgent.Agent;

internal sealed class AiKnowledgeStore
{
    private readonly object _gate = new();
    private const int PromptSnippetLimit = 2400;

    public void AppendDecision(GameStatePayload state, AiDecisionResult decision)
    {
        if (decision == null)
        {
            return;
        }

        var actionSummary = AiActionValidator.FormatAction(decision.action);
        var note = $"plan={Sanitize(decision.plan_summary)} | action={Sanitize(actionSummary)} | why={Sanitize(decision.reasoning)}";

        lock (_gate)
        {
            if (state.screen == "COMBAT" && state.combat?.enemies.Length > 0)
            {
                AppendCombatObservation(state, note);
                return;
            }

            if (state.screen == "EVENT" && !string.IsNullOrWhiteSpace(state.@event?.event_id))
            {
                AppendEventObservation(state, note);
                return;
            }

            AppendGeneralObservation(state, note);
        }
    }

    public void AppendExecutionResult(GameStatePayload state, AiDecisionResult decision, string resultMessage)
    {
        var actionSummary = AiActionValidator.FormatAction(decision.action);
        var note = $"executed={Sanitize(actionSummary)} | result={Sanitize(resultMessage)}";

        lock (_gate)
        {
            if (state.screen == "COMBAT" && state.combat?.enemies.Length > 0)
            {
                AppendCombatObservation(state, note);
                return;
            }

            if (state.screen == "EVENT" && !string.IsNullOrWhiteSpace(state.@event?.event_id))
            {
                AppendEventObservation(state, note);
                return;
            }

            AppendGeneralObservation(state, note);
        }
    }

    public string BuildPromptSupplement(GameStatePayload state)
    {
        lock (_gate)
        {
            if (state.screen == "COMBAT" && state.combat?.enemies.Length > 0)
            {
                var combatKey = BuildCombatKey(state.combat.enemies);
                var groupKind = state.combat.enemies.Length <= 1 ? "solo" : "groups";
                var path = Path.Combine(AiRuntimePaths.KnowledgeRoot, "combat", "global", groupKind, $"{combatKey}.md");
                return BuildSnippet(path, "combat knowledge");
            }

            if (state.screen == "EVENT" && !string.IsNullOrWhiteSpace(state.@event?.event_id))
            {
                var eventId = NormalizeSegment(state.@event.event_id, "unknown_event");
                var path = Path.Combine(AiRuntimePaths.KnowledgeRoot, "events", "global", $"{eventId}.md");
                return BuildSnippet(path, "event knowledge");
            }

            return string.Empty;
        }
    }

    private static void AppendCombatObservation(GameStatePayload state, string note)
    {
        var combatKey = BuildCombatKey(state.combat!.enemies);
        var groupKind = state.combat.enemies.Length <= 1 ? "solo" : "groups";
        var path = Path.Combine(AiRuntimePaths.KnowledgeRoot, "combat", "global", groupKind, $"{combatKey}.md");
        var enemyIds = state.combat.enemies
            .Select(enemy => NormalizeSegment(enemy.enemy_id, "unknown_enemy"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        EnsureFile(path, BuildCombatTemplate(combatKey, enemyIds));
        AppendSectionLine(path, "Observations", FormatNoteLine(state, note));
    }

    private static void AppendEventObservation(GameStatePayload state, string note)
    {
        var eventId = NormalizeSegment(state.@event!.event_id, "unknown_event");
        var path = Path.Combine(AiRuntimePaths.KnowledgeRoot, "events", "global", $"{eventId}.md");
        EnsureFile(path, BuildEventTemplate(eventId, state.@event.title));
        AppendSectionLine(path, "Observations", FormatNoteLine(state, note));
    }

    private static void AppendGeneralObservation(GameStatePayload state, string note)
    {
        var path = Path.Combine(AiRuntimePaths.KnowledgeRoot, "sessions", "advisory-log.md");
        EnsureFile(path, "# In-Game Agent Advisory Log\n");
        File.AppendAllText(path, $"- {FormatNoteLine(state, note)}{Environment.NewLine}", Encoding.UTF8);
    }

    private static string BuildCombatKey(IEnumerable<CombatEnemyPayload> enemies)
    {
        var counts = enemies
            .Select(enemy => NormalizeSegment(enemy.enemy_id, "unknown_enemy"))
            .GroupBy(id => id, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}_x{group.Count()}");
        return string.Join("+", counts);
    }

    private static string BuildCombatTemplate(string combatKey, IEnumerable<string> enemyIds)
    {
        var createdAt = UtcTimestamp();
        var enemyList = string.Join(", ", enemyIds.Select(id => $"\"{id}\""));
        return
            "---\n" +
            "type: combat\n" +
            $"combat_key: {combatKey}\n" +
            $"enemy_ids: [{enemyList}]\n" +
            $"created_at: {createdAt}\n" +
            "---\n\n" +
            "## Known Patterns\n\n" +
            "## Traits\n\n" +
            "## Tactical Notes\n\n" +
            "## Observations\n";
    }

    private static string BuildEventTemplate(string eventId, string? title)
    {
        var createdAt = UtcTimestamp();
        return
            "---\n" +
            "type: event\n" +
            $"event_id: {eventId}\n" +
            $"title: {Sanitize(title ?? string.Empty)}\n" +
            $"created_at: {createdAt}\n" +
            "---\n\n" +
            "## Option Outcomes\n\n" +
            "## Planning Notes\n\n" +
            "## Observations\n";
    }

    private static void EnsureFile(string path, string initialContent)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(path))
        {
            File.WriteAllText(path, initialContent, Encoding.UTF8);
        }
    }

    private static void AppendSectionLine(string path, string heading, string line)
    {
        var marker = $"## {heading}";
        var content = File.ReadAllText(path, Encoding.UTF8);
        if (!content.Contains(marker, StringComparison.Ordinal))
        {
            content = content.TrimEnd() + $"\n\n{marker}\n";
        }

        var markerIndex = content.IndexOf(marker, StringComparison.Ordinal);
        var sectionStart = content.IndexOf('\n', markerIndex);
        sectionStart = sectionStart < 0 ? content.Length : sectionStart + 1;

        var nextMarkerIndex = content.IndexOf("\n## ", sectionStart, StringComparison.Ordinal);
        var sectionEnd = nextMarkerIndex >= 0 ? nextMarkerIndex : content.Length;
        var existingSection = content[sectionStart..sectionEnd].Trim('\r', '\n');
        var updatedSection = string.IsNullOrWhiteSpace(existingSection)
            ? $"- {line}\n"
            : $"{existingSection}\n- {line}\n";
        var updatedContent = content[..sectionStart] + updatedSection + content[sectionEnd..].TrimStart('\r', '\n');
        File.WriteAllText(path, updatedContent, Encoding.UTF8);
    }

    private static string FormatNoteLine(GameStatePayload state, string note)
    {
        var floorPart = state.run?.floor > 0 ? $"floor={state.run.floor}" : "floor=unknown";
        return $"{UtcTimestamp()} | run_id={state.run_id} | {floorPart} | screen={state.screen} | {note}";
    }

    private static string NormalizeSegment(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch is '.' or '_' or '+' or '-')
            {
                normalized.Append(ch);
            }
            else
            {
                normalized.Append('_');
            }
        }

        return normalized.ToString().Trim('.', '_', '+', '-') is { Length: > 0 } cleaned
            ? cleaned
            : fallback;
    }

    private static string Sanitize(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static string UtcTimestamp()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    private static string BuildSnippet(string path, string label)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        var content = File.ReadAllText(path, Encoding.UTF8).Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        if (content.Length > PromptSnippetLimit)
        {
            content = $"{content[..PromptSnippetLimit]}...";
        }

        return $"{label} ({path})\n{content}";
    }
}
