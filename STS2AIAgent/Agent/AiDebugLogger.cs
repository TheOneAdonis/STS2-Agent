using System.Text;
using System.Text.Json;

namespace STS2AIAgent.Agent;

internal sealed record AiLlmCallContext
{
    public string purpose { get; init; } = "decision";

    public string runtime { get; init; } = string.Empty;

    public string screen { get; init; } = string.Empty;

    public string session_phase { get; init; } = string.Empty;

    public string run_id { get; init; } = string.Empty;

    public int? turn { get; init; }

    public string character_id { get; init; } = string.Empty;

    public string character_name { get; init; } = string.Empty;

    public bool auto_execute { get; init; }
}

internal static class AiDebugLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static bool TryWriteLlmExchange(AiAgentConfig config, object payload, out string? error)
    {
        if (!config.debug_mode)
        {
            error = null;
            return true;
        }

        return TryAppendJsonl(AiRuntimePaths.LlmDebugJsonlPath, payload, out error);
    }

    public static bool TryEnsureFile(string path, out string? error)
    {
        try
        {
            EnsureParentDirectory(path);
            using var _ = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = FormatError(ex);
            return false;
        }
    }

    public static bool TryAppendTextLine(string path, string line, out string? error)
    {
        try
        {
            EnsureParentDirectory(path);
            File.AppendAllText(path, $"{line}{Environment.NewLine}", Encoding.UTF8);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = FormatError(ex);
            return false;
        }
    }

    private static bool TryAppendJsonl(string path, object payload, out string? error)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            return TryAppendTextLine(path, json, out error);
        }
        catch (Exception ex)
        {
            error = FormatError(ex);
            return false;
        }
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Path '{path}' does not have a parent directory.");
        }

        Directory.CreateDirectory(directory);
    }

    private static string FormatError(Exception ex)
    {
        return $"{ex.GetType().Name}: {ex.Message}";
    }
}
