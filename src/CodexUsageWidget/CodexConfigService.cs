using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexUsageWidget;

internal sealed record ContextMode(
    int Index,
    string Name,
    string ContextLabel,
    string CompactionLabel,
    string BestFor,
    int? ContextWindow,
    int? CompactLimit)
{
    internal bool IsDefault => ContextWindow is null && CompactLimit is null;
}

internal sealed record ConfigWriteResult(bool Success, string ConfigPath, string Message);

internal static partial class CodexConfigService
{
    internal static readonly IReadOnlyList<ContextMode> Modes =
    [
        new(0, "Default", "Model default", "Standard compaction", "Uses the context settings supplied by Codex", null, null),
        new(1, "Balanced", "~300K context", "Earlier compaction", "Best for normal coding tasks", 300_000, 240_000),
        new(2, "Large Codebase", "~600K context", "Moderate compaction", "Retains more repository and tool history", 600_000, 500_000),
        new(3, "Long-Running Investigation", "~1M context", "Late compaction", "Best for reverse engineering, migrations and debugging", 1_000_000, 900_000)
    ];

    private static string CodexHome =>
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } configuredHome
            ? configuredHome
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    internal static string GlobalConfigPath => Path.Combine(CodexHome, "config.toml");

    internal static ConfigWriteResult ApplyGlobal(ContextMode mode) => Apply(GlobalConfigPath, mode);

    internal static ConfigWriteResult ApplyToPath(string configPath, ContextMode mode) => Apply(configPath, mode);

    internal static ContextMode? ReadMode(string configPath)
    {
        if (!File.Exists(configPath)) return null;

        try
        {
            string content = File.ReadAllText(configPath);
            int sectionStart = FindFirstSectionStart(content);
            string topLevel = sectionStart < 0 ? content : content[..sectionStart];
            int? context = ReadNumber(topLevel, "model_context_window");
            int? compact = ReadNumber(topLevel, "model_auto_compact_token_limit");
            if (context is null) return null;

            return Modes
                .Where(mode => !mode.IsDefault)
                .OrderBy(mode => Math.Abs((long)mode.ContextWindow!.Value - context.Value) +
                                 (compact is null ? 0 : Math.Abs((long)mode.CompactLimit!.Value - compact.Value)) / 2)
                .First();
        }
        catch
        {
            return null;
        }
    }

    private static ConfigWriteResult Apply(string configPath, ContextMode mode)
    {
        string? directory = Path.GetDirectoryName(configPath);
        if (string.IsNullOrWhiteSpace(directory))
            return new ConfigWriteResult(false, configPath, "Invalid configuration path.");

        string? temporaryPath = null;
        try
        {
            if (mode.IsDefault && !File.Exists(configPath))
            {
                return new ConfigWriteResult(
                    true,
                    configPath,
                    "Default is already active; Codex is using the model's standard context settings.");
            }

            Directory.CreateDirectory(directory);
            string content = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;
            string updated = UpdateTopLevel(content, mode);

            if (File.Exists(configPath) && string.Equals(content, updated, StringComparison.Ordinal))
            {
                string message = mode.IsDefault
                    ? "Default is already active; Codex is using the model's standard context settings."
                    : $"{mode.Name} is already active.";
                return new ConfigWriteResult(true, configPath, message);
            }

            if (File.Exists(configPath)) File.Copy(configPath, configPath + ".codex-usage-widget.bak", true);

            temporaryPath = Path.Combine(directory, $".{Path.GetFileName(configPath)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, updated, new UTF8Encoding(false));
            File.Move(temporaryPath, configPath, true);
            temporaryPath = null;
            string successMessage = mode.IsDefault
                ? "Default restored. Codex will use the model's standard context settings."
                : $"{mode.Name} applied. Start a new Codex task to use it.";
            return new ConfigWriteResult(true, configPath, successMessage);
        }
        catch (UnauthorizedAccessException)
        {
            return new ConfigWriteResult(false, configPath, "Access denied while updating config.toml.");
        }
        catch (IOException exception)
        {
            return new ConfigWriteResult(false, configPath, $"Could not update config.toml: {exception.Message}");
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    private static string UpdateTopLevel(string content, ContextMode mode)
    {
        string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool endsWithNewline = content.EndsWith("\n", StringComparison.Ordinal);
        var lines = content.Length == 0
            ? new List<string>()
            : content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (endsWithNewline && lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        int sectionIndex = lines.FindIndex(line => SectionLineRegex().IsMatch(line));
        if (sectionIndex < 0) sectionIndex = lines.Count;

        if (mode.IsDefault)
        {
            RemoveTopLevelKey(lines, ref sectionIndex, "model_context_window");
            RemoveTopLevelKey(lines, ref sectionIndex, "model_auto_compact_token_limit");
        }
        else
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["model"] = "\"gpt-5.6-sol\"",
                ["model_context_window"] = mode.ContextWindow!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["model_auto_compact_token_limit"] = mode.CompactLimit!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };

            foreach ((string key, string value) in values)
            {
                var matches = FindTopLevelKeys(lines, sectionIndex, key);

                if (matches.Count > 0)
                {
                    lines[matches[0]] = $"{key} = {value}";
                    for (int index = matches.Count - 1; index >= 1; index--)
                    {
                        lines.RemoveAt(matches[index]);
                        sectionIndex--;
                    }
                }
                else
                {
                    lines.Insert(sectionIndex, $"{key} = {value}");
                    sectionIndex++;
                }
            }
        }

        if (sectionIndex < lines.Count && sectionIndex > 0 && lines[sectionIndex - 1].Length > 0)
            lines.Insert(sectionIndex, string.Empty);

        string result = string.Join(newline, lines);
        if (result.Length > 0 && !result.EndsWith(newline, StringComparison.Ordinal)) result += newline;
        return result;
    }

    private static void RemoveTopLevelKey(List<string> lines, ref int sectionIndex, string key)
    {
        List<int> matches = FindTopLevelKeys(lines, sectionIndex, key);
        for (int index = matches.Count - 1; index >= 0; index--)
        {
            lines.RemoveAt(matches[index]);
            sectionIndex--;
        }
    }

    private static List<int> FindTopLevelKeys(List<string> lines, int sectionIndex, string key)
    {
        var matches = new List<int>();
        for (int index = 0; index < sectionIndex; index++)
        {
            if (Regex.IsMatch(lines[index], $@"^\s*{Regex.Escape(key)}\s*=", RegexOptions.CultureInvariant))
                matches.Add(index);
        }

        return matches;
    }

    private static int FindFirstSectionStart(string content)
    {
        Match match = Regex.Match(content, @"(?m)^\s*\[", RegexOptions.CultureInvariant);
        return match.Success ? match.Index : -1;
    }

    private static int? ReadNumber(string content, string key)
    {
        Match match = Regex.Match(
            content,
            $@"(?m)^\s*{Regex.Escape(key)}\s*=\s*(?<value>[0-9_]+)",
            RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["value"].Value.Replace("_", string.Empty), out int value)
            ? value
            : null;
    }

    [GeneratedRegex(@"^\s*\[[^\]]+\]", RegexOptions.CultureInvariant)]
    private static partial Regex SectionLineRegex();
}
