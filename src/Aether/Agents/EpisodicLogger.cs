namespace Aether.Agents;

/// <summary>
/// Episodic experience logger (EpisodicLogger).
/// Responsible for recording event episodes, mistakes and lessons learned during an agent's working session
/// into the long-term storage area of the 3_LEARNING tier, using a canonical data schema.
/// </summary>
public sealed class EpisodicLogger
{
    private readonly string _agentDir;
    private readonly BootConfig _config;
    private int _sequence;

    /// <summary>
    /// Creates an EpisodicLogger for a specific agent.
    /// </summary>
    /// <param name="agentDir">Root working directory of the agent.</param>
    /// <param name="config">Configuration that holds the paths for the mistake journal and episodes.</param>
    public EpisodicLogger(string agentDir, BootConfig config)
    {
        _agentDir = agentDir;
        _config = config;
    }

    /// <summary>
    /// Asynchronously records and stores a new event or experience episode (Episode) of the agent.
    /// </summary>
    /// <param name="sessionId">Identifier of the working session in which the event happened.</param>
    /// <param name="actor">The subject that performed the main action.</param>
    /// <param name="summary">Summary describing how the event unfolded.</param>
    /// <param name="tags">Keywords or labels that help classification and search.</param>
    /// <returns>The unique identifier (Memory ID) generated for the stored episode.</returns>
    public Task<string> AppendEpisodeAsync(string sessionId, string actor, string summary,
        Dictionary<string, string>? tags = null)
    {
        return AppendEntryAsync(_config.EpisodicLogFile, "episode", sessionId, summary, tags);
    }

    /// <summary>
    /// Asynchronously records a mistake or a deviating behavior (Mistake) so the agent can remember it and improve itself.
    /// </summary>
    /// <param name="sessionId">Identifier of the working session in which the mistake happened.</param>
    /// <param name="summary">Detailed description of the mistake and the lesson learned so it is not repeated.</param>
    /// <param name="tags">Labels that classify the mistake.</param>
    /// <returns>The unique identifier (Memory ID) generated for the stored mistake.</returns>
    public Task<string> AppendMistakeAsync(string sessionId, string summary,
        Dictionary<string, string>? tags = null)
    {
        return AppendEntryAsync(_config.MistakesFile, "mistake", sessionId, summary, tags);
    }

    /// <summary>
    /// Internal core method that formats and appends structured data with YAML frontmatter to the given file.
    /// </summary>
    /// <param name="relativePath">Relative path of the file to write.</param>
    /// <param name="type">Memory entity type (episode or mistake).</param>
    /// <param name="sessionId">Working session.</param>
    /// <param name="summary">Detailed content.</param>
    /// <param name="tags">Classification tags.</param>
    /// <returns>The unique memory identifier that is generated automatically.</returns>
    private async Task<string> AppendEntryAsync(string relativePath, string type, string sessionId,
        string summary, Dictionary<string, string>? tags)
    {
        var date = DateTime.UtcNow;
        var seq = Interlocked.Increment(ref _sequence);
        var id = $"mem_{date:yyyyMMdd}_{seq:D3}";

        var tagStr = tags is { Count: > 0 }
            ? string.Join(", ", tags.Select(kv => kv.Key))
            : "";

        var entry = $"""

---
id: {id}
type: {type}
source: session
session: {sessionId}
timestamp: {date:O}
confidence: 0.50
evidence_count: 1
tags: [{tagStr}]
links: []
status: candidate
---
{summary}

""";

        var fullPath = Path.Combine(_agentDir, relativePath);
        await File.AppendAllTextAsync(fullPath, entry);
        return id;
    }
}
