using System.IO;
using System.Text.Json;

namespace CodexUsageWidget;

internal sealed record RateLimit(double UsedPercent, int WindowMinutes, long? ResetsAt);
internal sealed record UsageSnapshot(RateLimit Long, RateLimit? Short, DateTimeOffset EventTime);

internal static class UsageReader
{
    private const int TailSize = 2 * 1024 * 1024;

    internal static string SessionRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");

    internal static UsageSnapshot? GetLatestSnapshot()
    {
        if (!Directory.Exists(SessionRoot)) return null;

        FileInfo[] files;
        try
        {
            files = Directory.EnumerateFiles(SessionRoot, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(6)
                .ToArray();
        }
        catch
        {
            return null;
        }

        foreach (FileInfo file in files)
        {
            UsageSnapshot? snapshot = ReadSnapshot(file);
            if (snapshot is not null) return snapshot;
        }

        return null;
    }

    internal static DateTime GetLatestWriteTimeUtc()
    {
        if (!Directory.Exists(SessionRoot)) return DateTime.MinValue;
        try
        {
            return Directory.EnumerateFiles(SessionRoot, "*.jsonl", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static UsageSnapshot? ReadSnapshot(FileInfo file)
    {
        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long start = Math.Max(0, stream.Length - TailSize);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            if (start > 0) reader.ReadLine();

            string[] lines = reader.ReadToEnd().Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i].TrimEnd('\r');
                if (!line.Contains("token_count", StringComparison.Ordinal)) continue;

                try
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    JsonElement root = document.RootElement;
                    if (!root.TryGetProperty("payload", out JsonElement payload) ||
                        !payload.TryGetProperty("type", out JsonElement type) ||
                        type.GetString() != "token_count" ||
                        !payload.TryGetProperty("rate_limits", out JsonElement limitsElement))
                    {
                        continue;
                    }

                    var limits = new List<RateLimit>(2);
                    AddLimit(limitsElement, "primary", limits);
                    AddLimit(limitsElement, "secondary", limits);
                    if (limits.Count == 0) continue;

                    RateLimit longLimit = limits
                        .Where(limit => limit.WindowMinutes >= 1440)
                        .OrderByDescending(limit => limit.WindowMinutes)
                        .FirstOrDefault() ?? limits.OrderByDescending(limit => limit.WindowMinutes).First();
                    RateLimit? shortLimit = limits
                        .Where(limit => limit.WindowMinutes < 1440)
                        .OrderBy(limit => limit.WindowMinutes)
                        .FirstOrDefault();

                    DateTimeOffset eventTime = file.LastWriteTime;
                    if (root.TryGetProperty("timestamp", out JsonElement timestamp) &&
                        DateTimeOffset.TryParse(timestamp.GetString(), out DateTimeOffset parsed))
                    {
                        eventTime = parsed.ToLocalTime();
                    }

                    return new UsageSnapshot(longLimit, shortLimit, eventTime);
                }
                catch (JsonException)
                {
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static void AddLimit(JsonElement parent, string propertyName, ICollection<RateLimit> limits)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement element) || element.ValueKind != JsonValueKind.Object)
            return;
        if (!element.TryGetProperty("used_percent", out JsonElement usedElement) || !usedElement.TryGetDouble(out double used))
            return;

        int window = element.TryGetProperty("window_minutes", out JsonElement windowElement) && windowElement.TryGetInt32(out int value)
            ? value
            : 0;
        long? resetsAt = element.TryGetProperty("resets_at", out JsonElement resetElement) && resetElement.TryGetInt64(out long reset)
            ? reset
            : null;
        limits.Add(new RateLimit(used, window, resetsAt));
    }
}
