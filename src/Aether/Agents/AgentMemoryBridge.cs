namespace Aether.Agents;

/// <summary>
/// Agent memory bridge (AgentMemoryBridge).
/// Links and manages the structure of memory files in the OC format (daily transcripts, MEMORY.md, task inbox and report).
/// Supports FileMemory (which uses SQLite/FTS5) with readable, easily controlled Markdown file storage.
/// </summary>
public sealed class AgentMemoryBridge
{
    private readonly string _agentDir;
    private readonly AgentConfig _config;

    /// <summary>
    /// Creates the memory bridge with the agent directory and the matching configuration.
    /// </summary>
    /// <param name="agentDir">Working and storage directory of the agent.</param>
    /// <param name="config">Configuration that defines the memory file paths.</param>
    public AgentMemoryBridge(string agentDir, AgentConfig config)
    {
        _agentDir = agentDir;
        _config = config;
    }

    /// <summary>
    /// Appends new conversation or experience content to the daily memory journal file.
    /// </summary>
    /// <param name="content">Memory summary text to store.</param>
    /// <param name="sessionId">Identifier of the matching session (Session ID).</param>
    /// <returns>A task representing the file append.</returns>
    public async Task AppendDailyMemoryAsync(string content, string sessionId)
    {
        var dailyDir = Path.Combine(_agentDir, _config.DailyMemoryDirectory);
        Directory.CreateDirectory(dailyDir);

        var filename = $"{DateTime.UtcNow:yyyy-MM-dd}.md";
        var filePath = Path.Combine(dailyDir, filename);

        var entry = $"\n## {DateTime.UtcNow:HH:mm:ss} UTC | session: {sessionId}\n\n{content}\n";
        await File.AppendAllTextAsync(filePath, entry);
    }

    /// <summary>
    /// Reads the content of the long-term memory file (MEMORY.md).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The long-term memory as a string, or an empty string if the file does not exist.</returns>
    public async Task<string> ReadLongTermMemoryAsync(CancellationToken ct = default)
    {
        var filePath = Path.Combine(_agentDir, _config.LongTermMemoryFile);
        if (!File.Exists(filePath))
            return string.Empty;
        return await File.ReadAllTextAsync(filePath, ct);
    }

    /// <summary>
    /// Overwrites the long-term memory file (MEMORY.md) with new content.
    /// </summary>
    /// <param name="content">New long-term memory content to store.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the file write.</returns>
    public async Task WriteLongTermMemoryAsync(string content, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_agentDir, _config.LongTermMemoryFile);
        await File.WriteAllTextAsync(filePath, content, ct);
    }

    /// <summary>
    /// Reads the task list from the task inbox file (TASK_INBOX.md).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The task list as Markdown text.</returns>
    public async Task<string> ReadTaskInboxAsync(CancellationToken ct = default)
    {
        if (_config.TaskInboxFile is null)
            return string.Empty;
        var filePath = Path.Combine(_agentDir, _config.TaskInboxFile);
        if (!File.Exists(filePath))
            return string.Empty;
        return await File.ReadAllTextAsync(filePath, ct);
    }

    /// <summary>
    /// Writes the result report of executed tasks to the TASK_REPORT.md file.
    /// </summary>
    /// <param name="content">Detailed report content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the file write.</returns>
    public async Task WriteTaskReportAsync(string content, CancellationToken ct = default)
    {
        if (_config.TaskReportFile is null)
            return;
        var filePath = Path.Combine(_agentDir, _config.TaskReportFile);
        await File.WriteAllTextAsync(filePath, content, ct);
    }

    /// <summary>
    /// Reads the file that records the agent's self-developed ideas or dreams (DREAMS.md).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dream content as text.</returns>
    public async Task<string> ReadDreamsAsync(CancellationToken ct = default)
    {
        if (_config.Boot is null) return string.Empty;
        var filePath = Path.Combine(_agentDir, _config.Boot.DreamsFile);
        if (!File.Exists(filePath)) return string.Empty;
        return await File.ReadAllTextAsync(filePath, ct);
    }

    /// <summary>
    /// Appends a new dream or self-distilled lesson to the DREAMS.md file.
    /// </summary>
    /// <param name="content">Idea or dream content to store.</param>
    /// <returns>A task representing the file append.</returns>
    public async Task AppendDreamAsync(string content)
    {
        if (_config.Boot is null) return;
        var filePath = Path.Combine(_agentDir, _config.Boot.DreamsFile);
        var entry = $"\n---\n\n*{DateTime.UtcNow:MMMM dd, yyyy 'at' h:mm tt}*\n\n{content}\n";
        await File.AppendAllTextAsync(filePath, entry);
    }

    /// <summary>
    /// Lists the daily memory files in the agent's storage directory.
    /// </summary>
    /// <param name="since">Start of the time window used to filter files by last modification time.</param>
    /// <returns>The list of Markdown file paths that hold the memory journal, sorted newest first.</returns>
    public IReadOnlyList<string> GetMemoryFiles(DateTime? since = null)
    {
        var dailyDir = Path.Combine(_agentDir, _config.DailyMemoryDirectory);
        if (!Directory.Exists(dailyDir)) return Array.Empty<string>();
        return Directory.GetFiles(dailyDir, "*.md")
            .Where(f => !since.HasValue || File.GetLastWriteTimeUtc(f) >= since.Value)
            .OrderByDescending(f => f)
            .ToList();
    }
}
