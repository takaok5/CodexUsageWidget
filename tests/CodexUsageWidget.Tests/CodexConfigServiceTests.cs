using System.Text.RegularExpressions;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class CodexConfigServiceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(), $"CodexUsageWidget-tests-{Guid.NewGuid():N}");

    public CodexConfigServiceTests() => Directory.CreateDirectory(_testDirectory);

    [Fact]
    public void ApplyProjectCreatesTopLevelContextSettingsAndPreservesSections()
    {
        string codexDirectory = Path.Combine(_testDirectory, ".codex");
        Directory.CreateDirectory(codexDirectory);
        string configPath = Path.Combine(codexDirectory, "config.toml");
        File.WriteAllText(configPath, "# keep this comment\n[features]\njs_repl = true\n");

        ContextMode investigation = CodexConfigService.Modes.Single(mode => mode.Name == "Long-Running Investigation");
        ConfigWriteResult result = CodexConfigService.ApplyProject(_testDirectory, investigation);

        Assert.True(result.Success, result.Message);
        string updated = File.ReadAllText(configPath);
        int sectionStart = updated.IndexOf("[features]", StringComparison.Ordinal);
        Assert.True(sectionStart > 0);
        string topLevel = updated[..sectionStart];
        Assert.Contains("model = \"gpt-5.6-sol\"", topLevel);
        Assert.Contains("model_context_window = 1000000", topLevel);
        Assert.Contains("model_auto_compact_token_limit = 900000", topLevel);
        Assert.Contains("js_repl = true", updated);
    }

    [Fact]
    public void ReapplyingAProfileUpdatesWithoutDuplicatingKeysAndCreatesBackup()
    {
        ContextMode balanced = CodexConfigService.Modes.Single(mode => mode.Name == "Balanced");
        ContextMode large = CodexConfigService.Modes.Single(mode => mode.Name == "Large Codebase");
        ConfigWriteResult first = CodexConfigService.ApplyProject(_testDirectory, balanced);
        ConfigWriteResult second = CodexConfigService.ApplyProject(_testDirectory, large);
        string configPath = CodexConfigService.GetProjectConfigPath(_testDirectory);
        string updated = File.ReadAllText(configPath);

        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        Assert.Single(Regex.Matches(updated, @"(?m)^model_context_window\s*=").Cast<Match>());
        Assert.Single(Regex.Matches(updated, @"(?m)^model_auto_compact_token_limit\s*=").Cast<Match>());
        Assert.Contains("model_context_window = 600000", updated);
        Assert.Contains("model_auto_compact_token_limit = 500000", updated);
        Assert.True(File.Exists(configPath + ".codex-usage-widget.bak"));
    }

    [Fact]
    public void ReadModeFindsTheNearestWorkMode()
    {
        string codexDirectory = Path.Combine(_testDirectory, ".codex");
        Directory.CreateDirectory(codexDirectory);
        string configPath = Path.Combine(codexDirectory, "config.toml");
        File.WriteAllText(configPath, "model_context_window = 590000\nmodel_auto_compact_token_limit = 490000\n");

        ContextMode? mode = CodexConfigService.ReadMode(configPath);

        Assert.NotNull(mode);
        Assert.Equal("Large Codebase", mode.Name);
    }

    [Fact]
    public void DefaultRemovesOnlyContextOverridesAndPreservesOtherSettings()
    {
        string codexDirectory = Path.Combine(_testDirectory, ".codex");
        Directory.CreateDirectory(codexDirectory);
        string configPath = Path.Combine(codexDirectory, "config.toml");
        File.WriteAllText(
            configPath,
            "model = \"gpt-5.6-sol\"\nmodel_context_window = 600000\n" +
            "model_auto_compact_token_limit = 500000\n[features]\njs_repl = true\n");

        ConfigWriteResult result = CodexConfigService.ApplyProject(_testDirectory, CodexConfigService.Modes[0]);

        Assert.True(result.Success, result.Message);
        string updated = File.ReadAllText(configPath);
        Assert.DoesNotContain("model_context_window", updated);
        Assert.DoesNotContain("model_auto_compact_token_limit", updated);
        Assert.Contains("model = \"gpt-5.6-sol\"", updated);
        Assert.Contains("[features]", updated);
        Assert.Contains("js_repl = true", updated);
    }

    [Fact]
    public void DefaultDoesNotCreateAnEmptyProjectConfig()
    {
        string configPath = CodexConfigService.GetProjectConfigPath(_testDirectory);

        ConfigWriteResult result = CodexConfigService.ApplyProject(_testDirectory, CodexConfigService.Modes[0]);

        Assert.True(result.Success, result.Message);
        Assert.False(File.Exists(configPath));
    }

    [Fact]
    public void ProjectReaderReturnsOnlyExistingDistinctDirectories()
    {
        IReadOnlyList<CodexProject> projects = CodexProjectReader.GetProjects();

        Assert.All(projects, project => Assert.True(Directory.Exists(project.Path)));
        Assert.Equal(
            projects.Count,
            projects.Select(project => project.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    public void Dispose()
    {
        string resolved = Path.GetFullPath(_testDirectory);
        string tempRoot = Path.GetFullPath(Path.GetTempPath());
        if (resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(resolved).StartsWith("CodexUsageWidget-tests-", StringComparison.Ordinal))
        {
            Directory.Delete(resolved, true);
        }
    }
}
