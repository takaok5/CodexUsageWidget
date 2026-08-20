using System.IO;
using System.Text.Json;

namespace CodexUsageWidget;

internal sealed record RateLimit(double UsedPercent, int WindowMinutes, long? ResetsAt);
internal sealed record UsageSnapshot(RateLimit Long, RateLimit? Short, DateTimeOffset EventTime);
internal sealed record CachedSnapshot(DateTime LastWriteTimeUtc, long Length, UsageSnapshot? Snapshot);

internal static class UsageReader
{
    private const int TailSize = 2 * 1024 * 1024;
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, CachedSnapshot> SnapshotCache =
        new(StringComparer.OrdinalIgnoreCase);

    internal static string SessionRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");

    internal static UsageSnapshot? GetLatestSnapshot(string? sessionRoot = null)
    {
        string root = Path.GetFullPath(sessionRoot ?? SessionRoot);
        if (!Directory.Exists(root)) return null;

        FileInfo[] files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .ToArray();
        }
        catch
        {
            return null;
        }

        lock (CacheLock)
        {
            var currentPaths = files.Select(file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string rootPrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            SnapshotCache.Keys
                .Where(path => path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) && !currentPaths.Contains(path))
                .ToList()
                .ForEach(path => SnapshotCache.Remove(path));

            var snapshots = new List<UsageSnapshot>(files.Length);
            foreach (FileInfo file in files)
            {
                try
                {
                    if (!SnapshotCache.TryGetValue(file.FullName, out CachedSnapshot? cached) ||
                        cached.LastWriteTimeUtc != file.LastWriteTimeUtc || cached.Length != file.Length)
                    {
                        cached = new CachedSnapshot(file.LastWriteTimeUtc, file.Length, ReadSnapshot(file));
                        SnapshotCache[file.FullName] = cached;
                    }

                    if (cached.Snapshot is not null) snapshots.Add(cached.Snapshot);
                }
                catch (IOException)
                {
                    SnapshotCache.Remove(file.FullName);
                }
                catch (UnauthorizedAccessException)
                {
                    SnapshotCache.Remove(file.FullName);
                }
            }

            return snapshots.OrderByDescending(snapshot => snapshot.EventTime).FirstOrDefault();
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
