using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class CodexConfigServiceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(), $"CodexUsageWidget-tests-{Guid.NewGuid():N}");

    public CodexConfigServiceTests() => Directory.CreateDirectory(_testDirectory);

    [Fact]
    public void ApplyCreatesTopLevelContextSettingsAndPreservesSections()
    {
        string configPath = Path.Combine(_testDirectory, "config.toml");
        File.WriteAllText(configPath, "model = \"gpt-5.4\"\n# keep this comment\n[features]\njs_repl = true\n");

        ContextMode investigation = CodexConfigService.Modes.Single(mode => mode.Name == "Long-Running Investigation");
        ConfigWriteResult result = CodexConfigService.ApplyToPath(configPath, investigation);

        Assert.True(result.Success, result.Message);
        string updated = File.ReadAllText(configPath);
        int sectionStart = updated.IndexOf("[features]", StringComparison.Ordinal);
        Assert.True(sectionStart > 0);
        string topLevel = updated[..sectionStart];
        Assert.Contains("model = \"gpt-5.4\"", topLevel);
        Assert.DoesNotContain("gpt-5.6-sol", topLevel);
        Assert.Contains("model_context_window = 1000000", topLevel);
        Assert.Contains("model_auto_compact_token_limit = 900000", topLevel);
        Assert.Contains("js_repl = true", updated);
    }

    [Fact]
    public void ReapplyingAProfileUpdatesWithoutDuplicatingKeysAndCreatesBackup()
    {
        ContextMode balanced = CodexConfigService.Modes.Single(mode => mode.Name == "Balanced");
        ContextMode large = CodexConfigService.Modes.Single(mode => mode.Name == "Large Codebase");
        string configPath = Path.Combine(_testDirectory, "config.toml");
        ConfigWriteResult first = CodexConfigService.ApplyToPath(configPath, balanced);
        ConfigWriteResult second = CodexConfigService.ApplyToPath(configPath, large);
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
    public void ReadStateTreatsNonPresetValuesAsCustomWithoutChangingTheFile()
    {
        string configPath = Path.Combine(_testDirectory, "config.toml");
        string original = "model_context_window = 590000\nmodel_auto_compact_token_limit = 490000\n";
        File.WriteAllText(configPath, original);

        ContextConfigState state = CodexConfigService.ReadState(configPath);

        Assert.True(state.IsCustom);
        Assert.Null(state.Mode);
        Assert.Equal(590_000, state.ContextWindow);
        Assert.Equal(490_000, state.CompactLimit);
        Assert.Equal(original, File.ReadAllText(configPath));
    }

    [Fact]
    public void ReadStateRecognizesOnlyAnExactPreset()
    {
        string configPath = Path.Combine(_testDirectory, "config.toml");
        File.WriteAllText(configPath, "model_context_window = 600000\nmodel_auto_compact_token_limit = 500000\n");

        ContextConfigState state = CodexConfigService.ReadState(configPath);

        Assert.False(state.IsCustom);
        Assert.Equal("Large Codebase", state.Mode!.Name);
    }

    [Fact]
    public void DefaultRemovesOnlyContextOverridesAndPreservesOtherSettings()
    {
        string configPath = Path.Combine(_testDirectory, "config.toml");
        File.WriteAllText(
            configPath,
            "model = \"gpt-5.6-sol\"\nmodel_context_window = 600000\n" +
            "model_auto_compact_token_limit = 500000\n[features]\njs_repl = true\n");

        ConfigWriteResult result = CodexConfigService.ApplyToPath(configPath, CodexConfigService.Modes[0]);

        Assert.True(result.Success, result.Message);
        string updated = File.ReadAllText(configPath);
        Assert.DoesNotContain("model_context_window", updated);
        Assert.DoesNotContain("model_auto_compact_token_limit", updated);
        Assert.Contains("model = \"gpt-5.6-sol\"", updated);
        Assert.Contains("[features]", updated);
        Assert.Contains("js_repl = true", updated);
    }

    [Fact]
    public void ApplyingCustomPresetPreservesTheUsersModelSetting()
    {
        string configPath = Path.Combine(_testDirectory, "config.toml");
        File.WriteAllText(
            configPath,
            "model = \"gpt-5.4\"\nmodel_context_window = 590000\n" +
            "model_auto_compact_token_limit = 490000\n");

        ConfigWriteResult result = CodexConfigService.ApplyToPath(configPath, CodexConfigService.Modes[2]);

        Assert.True(result.Success, result.Message);
        string updated = File.ReadAllText(configPath);
        Assert.Contains("model = \"gpt-5.4\"", updated);
        Assert.DoesNotContain("gpt-5.6-sol", updated);
        Assert.Contains("model_context_window = 600000", updated);
        Assert.Contains("model_auto_compact_token_limit = 500000", updated);
    }

    [Fact]
    public void DefaultDoesNotCreateAnEmptyConfig()
    {
        string configPath = Path.Combine(_testDirectory, "config.toml");

        ConfigWriteResult result = CodexConfigService.ApplyToPath(configPath, CodexConfigService.Modes[0]);

        Assert.True(result.Success, result.Message);
        Assert.False(File.Exists(configPath));
    }

    [Fact]
    public void CustomCatalogKeepsMaximumCeilingWhileDefaultRestoresBaseContext()
    {
        string catalogPath = Path.Combine(_testDirectory, "models.json");
        File.WriteAllText(
            catalogPath,
            """
            {
              "models": [
                {
                  "slug": "gpt-5.6-sol",
                  "context_window": 272000,
                  "max_context_window": 272000
                }
              ]
            }
            """);
        string configPath = Path.Combine(_testDirectory, "config.toml");
        File.WriteAllText(configPath, $"model_catalog_json = {JsonSerializer.Serialize(catalogPath)}\n");

        ContextMode large = CodexConfigService.Modes.Single(mode => mode.Name == "Large Codebase");
        ContextMode investigation = CodexConfigService.Modes.Single(mode => mode.Name == "Long-Running Investigation");

        Assert.True(CodexConfigService.ApplyToPath(configPath, large).Success);
        Assert.Equal(1_000_000, ReadCatalogNumber(catalogPath, "max_context_window"));
        Assert.Equal(272_000, ReadCatalogNumber(catalogPath, "context_window"));
        Assert.True(File.Exists(catalogPath + ".codex-usage-widget.bak"));
        Assert.Equal(
            272_000,
            ReadCatalogNumber(catalogPath + ".codex-usage-widget.bak", "max_context_window"));

        Assert.True(CodexConfigService.ApplyToPath(configPath, investigation).Success);
        Assert.Equal(1_000_000, ReadCatalogNumber(catalogPath, "max_context_window"));

        Assert.True(CodexConfigService.ApplyToPath(configPath, CodexConfigService.Modes[0]).Success);
        Assert.Equal(1_000_000, ReadCatalogNumber(catalogPath, "max_context_window"));
        Assert.Equal(272_000, ReadCatalogNumber(catalogPath, "context_window"));
        Assert.DoesNotContain("model_context_window", File.ReadAllText(configPath));
    }

    [Fact]
    public void InvalidCustomCatalogDoesNotModifyConfig()
    {
        string catalogPath = Path.Combine(_testDirectory, "models.json");
        File.WriteAllText(catalogPath, "not json");
        string configPath = Path.Combine(_testDirectory, "config.toml");
        string original = $"model_catalog_json = {JsonSerializer.Serialize(catalogPath)}\n";
        File.WriteAllText(configPath, original);

        ConfigWriteResult result = CodexConfigService.ApplyToPath(configPath, CodexConfigService.Modes[1]);

        Assert.False(result.Success);
        Assert.Equal(original, File.ReadAllText(configPath));
    }

    private static int ReadCatalogNumber(string catalogPath, string key)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(catalogPath))!;
        return root["models"]![0]![key]!.GetValue<int>();
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
