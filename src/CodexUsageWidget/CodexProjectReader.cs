using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CodexUsageWidget;

internal sealed record CodexProject(string Name, string Path, DateTimeOffset LastUsed)
{
    public string DisplayName => $"{Name}  —  {Path}";
}

internal static partial class CodexProjectReader
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteDone = 101;
    private const int SqliteOpenReadOnly = 0x00000001;

    private static readonly Regex StateDatabasePattern = new(
        @"state_(?<version>\d+)\.sqlite$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string CodexHome
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
                : Environment.ExpandEnvironmentVariables(configured);
        }
    }

    internal static IReadOnlyList<CodexProject> GetProjects()
    {
        string? databasePath = FindStateDatabase();
        if (databasePath is null) return [];

        nint database = 0;
        nint statement = 0;
        try
        {
            if (sqlite3_open_v2(databasePath, out database, SqliteOpenReadOnly, null) != SqliteOk || database == 0)
                return [];

            sqlite3_busy_timeout(database, 1_500);
            const string query = """
                SELECT cwd, MAX(updated_at)
                FROM threads
                WHERE cwd IS NOT NULL AND TRIM(cwd) <> ''
                GROUP BY cwd
                ORDER BY MAX(updated_at) DESC
                LIMIT 250
                """;
            if (sqlite3_prepare_v2(database, query, -1, out statement, 0) != SqliteOk || statement == 0)
                return [];

            var projects = new List<CodexProject>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                int result = sqlite3_step(statement);
                if (result == SqliteDone) break;
                if (result != SqliteRow) return [];

                string? rawPath = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, 0));
                string? path = NormalizePath(rawPath);
                if (path is null || !Directory.Exists(path) || !seen.Add(path)) continue;

                long unixTime = sqlite3_column_int64(statement, 1);
                DateTimeOffset lastUsed;
                try
                {
                    lastUsed = DateTimeOffset.FromUnixTimeSeconds(unixTime);
                }
                catch (ArgumentOutOfRangeException)
                {
                    lastUsed = DateTimeOffset.MinValue;
                }

                string name = new DirectoryInfo(path).Name;
                if (string.IsNullOrWhiteSpace(name)) name = path;
                projects.Add(new CodexProject(name, path, lastUsed));
            }

            return projects;
        }
        catch (DllNotFoundException)
        {
            return [];
        }
        catch (EntryPointNotFoundException)
        {
            return [];
        }
        finally
        {
            if (statement != 0) sqlite3_finalize(statement);
            if (database != 0) sqlite3_close(database);
        }
    }

    private static string? FindStateDatabase()
    {
        if (!Directory.Exists(CodexHome)) return null;

        try
        {
            return Directory.EnumerateFiles(CodexHome, "state_*.sqlite", SearchOption.TopDirectoryOnly)
                .Select(path => new
                {
                    Path = path,
                    Version = ParseVersion(path),
                    LastWrite = File.GetLastWriteTimeUtc(path)
                })
                .OrderByDescending(item => item.Version)
                .ThenByDescending(item => item.LastWrite)
                .Select(item => item.Path)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static int ParseVersion(string path)
    {
        Match match = StateDatabasePattern.Match(Path.GetFileName(path));
        return match.Success && int.TryParse(match.Groups["version"].Value, out int version) ? version : -1;
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string normalized = path.Trim();
        if (normalized.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            normalized = @"\\" + normalized[8..];
        else if (normalized.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[4..];

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(normalized));
        }
        catch
        {
            return null;
        }
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
        out nint database,
        int flags,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? virtualFileSystem);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(nint database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_busy_timeout(nint database, int milliseconds);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(
        nint database,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
        int bytes,
        out nint statement,
        nint tail);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(nint statement);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint sqlite3_column_text(nint statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(nint statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(nint statement);
}
