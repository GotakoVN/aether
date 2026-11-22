using Aether.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class CronFrontmatterParserTests : IDisposable
{
    private readonly string _tempDir;

    public CronFrontmatterParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cron_parser_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteTaskFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task WellFormedFrontmatter_ParsesCorrectly()
    {
        var path = WriteTaskFile("valid.md", """
            ---
            schedule: "*/15 * * * *"
            agent: maria
            channel: telegram
            enabled: true
            ---
            Run the daily memory check.
            """);

        var task = await CronFrontmatterParser.ParseAsync(path, NullLogger.Instance);

        Assert.NotNull(task);
        Assert.Equal("*/15 * * * *", task!.Schedule);
        Assert.Equal("maria", task.Agent);
        Assert.Equal("telegram", task.Channel);
        Assert.True(task.Enabled);
        Assert.Equal("Run the daily memory check.", task.Body);
        Assert.Equal(path, task.FilePath);
    }

    [Fact]
    public async Task MissingFrontmatter_FallsBackToDefaults()
    {
        var path = WriteTaskFile("no-frontmatter.md", "Just a body without frontmatter.");

        var task = await CronFrontmatterParser.ParseAsync(path, NullLogger.Instance);

        Assert.NotNull(task);
        Assert.Equal("0 * * * *", task!.Schedule); // default hourly
        Assert.Equal("default", task.Agent);
        Assert.Equal("telegram", task.Channel);
        Assert.True(task.Enabled);
        Assert.Equal("Just a body without frontmatter.", task.Body);
    }

    [Fact]
    public async Task EnabledFalse_StillReturnsTaskWithEnabledFalse()
    {
        var path = WriteTaskFile("disabled.md", """
            ---
            schedule: "0 2 * * *"
            enabled: false
            ---
            Disabled check.
            """);

        var task = await CronFrontmatterParser.ParseAsync(path, NullLogger.Instance);

        Assert.NotNull(task);
        Assert.False(task!.Enabled);
    }

    [Fact]
    public async Task EmptyBody_ReturnsNull()
    {
        var path = WriteTaskFile("empty.md", """
            ---
            schedule: "0 5 * * *"
            enabled: true
            ---
            """);

        var task = await CronFrontmatterParser.ParseAsync(path, NullLogger.Instance);

        Assert.Null(task);
    }

    [Fact]
    public async Task MissingFile_ReturnsNull()
    {
        var task = await CronFrontmatterParser.ParseAsync(Path.Combine(_tempDir, "does-not-exist.md"), NullLogger.Instance);
        Assert.Null(task);
    }
}