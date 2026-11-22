namespace Aether.Agents;

/// <summary>
/// Represents the startup and operating configuration of a specific agent in the Aether system.
/// Defines the file paths and directories that store the basic context.
/// </summary>
public record AgentConfig
{
    /// <summary>
    /// List of the default startup configuration files (for example: AGENTS.md).
    /// </summary>
    public List<string> StartupFiles { get; init; } = new() { "AGENTS.md" };

    /// <summary>
    /// Name of the file that stores long-term memory (default: MEMORY.md).
    /// </summary>
    public string LongTermMemoryFile { get; init; } = "MEMORY.md";

    /// <summary>
    /// Path of the file that records the biological heartbeat and activity state (default: HEARTBEAT.md).
    /// </summary>
    public string? HeartbeatFile { get; init; } = "HEARTBEAT.md";

    /// <summary>
    /// Directory that stores the agent's daily memory (default: memory).
    /// </summary>
    public string DailyMemoryDirectory { get; init; } = "memory";

    /// <summary>
    /// Path of the inbox file that holds the list of tasks to perform (default: TASK_INBOX.md).
    /// </summary>
    public string? TaskInboxFile { get; init; } = "TASK_INBOX.md";

    /// <summary>
    /// Path of the file that reports the results of executed tasks (default: TASK_REPORT.md).
    /// </summary>
    public string? TaskReportFile { get; init; } = "TASK_REPORT.md";

    /// <summary>
    /// Detailed configuration for the Boot (startup) process of the FEOFALLS cognitive architecture.
    /// </summary>
    public BootConfig? Boot { get; init; }
}

/// <summary>
/// Represents the detailed configuration for the agent's cognitive startup and memory cleanup.
/// </summary>
public record BootConfig
{
    /// <summary>
    /// List of files that protect the agent's constitution (obsolete, use ContextAssembler instead).
    /// </summary>
    [Obsolete("ConstitutionFiles is deprecated. Use ContextAssembler for dynamic file discovery instead.")]
    public List<string> ConstitutionFiles { get; init; } = new() { "AGENTS_GUARD.md" };

    /// <summary>
    /// List of core identity files (obsolete, use ContextAssembler instead).
    /// </summary>
    [Obsolete("IdentityFiles is deprecated. Use ContextAssembler for dynamic file discovery instead.")]
    public List<string> IdentityFiles { get; init; } = new() { "SOUL.md", "USER.md", "IDENTITY.md" };

    /// <summary>
    /// List of supporting cognitive files (obsolete, use ContextAssembler instead).
    /// </summary>
    [Obsolete("CognitiveFiles is deprecated. Use ContextAssembler for dynamic file discovery instead.")]
    public List<string> CognitiveFiles { get; init; } = new() { "MEMORY.md" };

    /// <summary>
    /// Journal file that records the agent's inner self-reflection (default: INTROSPECTION.md).
    /// </summary>
    public string EpisodicLogFile { get; init; } = "INTROSPECTION.md";

    /// <summary>
    /// File that records mistakes and lessons learned so the agent can change its behavior (default: MEMORY.md).
    /// </summary>
    public string MistakesFile { get; init; } = "MEMORY.md";

    /// <summary>
    /// File that stores the agent's creative ideas and dreams (default: DREAMS.md).
    /// </summary>
    public string DreamsFile { get; init; } = "DREAMS.md";

    /// <summary>
    /// Directory that holds candidate ideas and dreams that are still incubating (default: memory/dreaming).
    /// </summary>
    public string CandidatesDirectory { get; init; } = "memory/dreaming";

    /// <summary>
    /// Task inbox file (default: TASK_INBOX.md).
    /// </summary>
    public string? TaskInboxFile { get; init; } = "TASK_INBOX.md";

    /// <summary>
    /// Task execution report file (default: TASK_REPORT.md).
    /// </summary>
    public string? TaskReportFile { get; init; } = "TASK_REPORT.md";

    /// <summary>
    /// Biological heartbeat file (default: HEARTBEAT.md).
    /// </summary>
    public string? HeartbeatFile { get; init; } = "HEARTBEAT.md";

    /// <summary>
    /// Days for a memory to move from the Active state to Decaying (default: 60 days).
    /// </summary>
    public int ActiveToDecayingDays { get; init; } = 60;

    /// <summary>
    /// Days for a memory to move from Decaying to long-term Archived (default: 90 days).
    /// </summary>
    public int DecayingToArchivedDays { get; init; } = 90;

    /// <summary>
    /// Merges the obsolete legacy file lists into a single set to keep backward compatibility.
    /// </summary>
    /// <returns>A HashSet with the file names, compared case-insensitively.</returns>
#pragma warning disable CS0618
    public HashSet<string> GetLegacyFileSet()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in ConstitutionFiles) files.Add(f);
        foreach (var f in IdentityFiles) files.Add(f);
        foreach (var f in CognitiveFiles) files.Add(f);
        return files;
    }
#pragma warning restore CS0618
}
