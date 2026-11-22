using System.Text;

namespace Aether.Agent;

/// <summary>
/// Cognitive context assembler (ContextAssembler).
/// Detects, loads and merges the agent's core configuration files (such as AGENTS.md, SOUL.md, IDENTITY.md, MEMORY.md)
/// together with the workspace file catalog, to build a consistent, compact System Prompt and Working Context for the LLM.
/// </summary>
public sealed class ContextAssembler
{
    private static readonly Dictionary<string, int> BootstrapFileOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AGENTS.md"] = 10,
        ["SOUL.md"] = 20,
        ["IDENTITY.md"] = 30,
        ["USER.md"] = 40,
        ["MEMORY.md"] = 50,
        ["HEARTBEAT.md"] = 60,
    };

    private static readonly HashSet<string> BootstrapFiles =
        new(BootstrapFileOrder.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Only these directories are scanned and listed in the Workspace catalog.
    /// </summary>
    private static readonly HashSet<string> AllowedDirectories =
        new(StringComparer.OrdinalIgnoreCase) { "memory", "skills" };

    /// <summary>
    /// Limit on how many days back daily memory files are scanned, to avoid diluting the context.
    /// </summary>
    private const int MemoryLookbackDays = 2;

    private readonly int _dynamicTokenBudget;

    /// <summary>
    /// Creates a ContextAssembler instance.
    /// </summary>
    /// <param name="dynamicTokenBudget">Maximum token budget allowed for the dynamic context (default: 4000).</param>
    public ContextAssembler(int dynamicTokenBudget = 4000)
    {
        _dynamicTokenBudget = dynamicTokenBudget;
    }

    /// <summary>
    /// Assembles the agent's static, complete identity context (Identity Context).
    /// Scans the default startup identity files, joins their content and appends the map of files present in the workspace.
    /// </summary>
    /// <param name="agentDir">Absolute working directory of the agent.</param>
    /// <returns>The complete identity context string to inject into the System Prompt.</returns>
    public string AssembleIdentityContext(string agentDir)
    {
        var files = DiscoverBootstrapFiles(agentDir);
        if (files.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Project Context");
        sb.AppendLine();
        sb.AppendLine("The following files define your identity, behavior, and memory.");
        sb.AppendLine("They are loaded by Aether and included below. Embody them fully.");
        sb.AppendLine();

        foreach (var (path, content) in files)
        {
            sb.AppendLine($"### {path}");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        // Add the workspace file map so the LLM knows which auxiliary files exist and can read them on its own
        var listing = DiscoverWorkspaceContents(agentDir);
        if (!string.IsNullOrEmpty(listing))
        {
            sb.AppendLine(listing);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Finds the directories and .md files in the workspace (excluding the core startup files)
    /// to build a short table of contents shown to the agent.
    /// </summary>
    /// <param name="agentDir">Working directory of the agent.</param>
    /// <returns>The table of contents as Markdown text.</returns>
    private static string DiscoverWorkspaceContents(string agentDir)
    {
        var sb = new StringBuilder();
        var hasContent = false;

        try
        {
            // Walk the subdirectories that contain *.md files
            foreach (var dir in Directory.GetDirectories(agentDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith(".")) continue; // Skip hidden directories
                if (!AllowedDirectories.Contains(dirName)) continue;

                var mdFiles = Directory.GetFiles(dir, "*.md", SearchOption.TopDirectoryOnly)
                    .Where(f => !Path.GetFileName(f).StartsWith("."))
                    .Where(f => IsRecentEnough(f, dirName))
                    .Select(Path.GetFileName)
                    .Where(f => f != null)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (mdFiles.Count == 0) continue;

                if (!hasContent)
                {
                    sb.AppendLine("## Workspace Contents");
                    sb.AppendLine();
                    sb.AppendLine("The following directories and files exist in your workspace.");
                    sb.AppendLine("Files listed above are already loaded. For everything below, use the `read` tool to access them.");
                    sb.AppendLine();
                    hasContent = true;
                }

                sb.AppendLine($"### {dirName}/ ({mdFiles.Count} files)");
                sb.AppendLine(string.Join(", ", mdFiles!));
                sb.AppendLine();
            }
        }
        catch (Exception)
        {
            // Skip silently if the directory cannot be accessed
        }

        return hasContent ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// Scans and loads the content of the core startup files that exist in the agent's directory,
    /// sorting them automatically in the best priority order.
    /// </summary>
    private static List<(string Path, string Content)> DiscoverBootstrapFiles(string agentDir)
    {
        var found = new List<(string Path, string Content)>();

        foreach (var fileName in BootstrapFiles)
        {
            var fullPath = Path.Combine(agentDir, fileName);
            if (!File.Exists(fullPath)) continue;

            var content = File.ReadAllText(fullPath);
            if (string.IsNullOrWhiteSpace(content)) continue;

            found.Add((fileName, content));
        }

        // Sort the files by priority
        found.Sort((a, b) =>
        {
            var orderA = BootstrapFileOrder.GetValueOrDefault(a.Path, 99);
            var orderB = BootstrapFileOrder.GetValueOrDefault(b.Path, 99);
            return orderA.CompareTo(orderB);
        });

        return found;
    }

    /// <summary>
    /// Assembles the dynamic context that changes every conversation turn (Working State, Recent Memory, Group Context).
    /// Compacts and trims automatically (Context Compaction) so the model's token budget is never exceeded.
    /// </summary>
    /// <param name="workingState">Current working state (for example: TASK_INBOX.md and HEARTBEAT.md).</param>
    /// <param name="recentMemory">Short-term memory journal or recent reference information.</param>
    /// <param name="groupContext">Group conversation context or agent routing.</param>
    /// <returns>The complete dynamic context with a strictly controlled token count.</returns>
    public string AssembleDynamicContext(
        string? workingState = null,
        string? recentMemory = null,
        string? groupContext = null)
    {
        var hasInputs = !string.IsNullOrWhiteSpace(workingState) ||
                         !string.IsNullOrWhiteSpace(recentMemory) ||
                         !string.IsNullOrWhiteSpace(groupContext);

        if (!hasInputs)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(workingState))
        {
            sb.AppendLine("## Working State");
            sb.AppendLine(workingState);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(recentMemory))
        {
            sb.AppendLine("## Recent Memory");
            sb.AppendLine(recentMemory);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(groupContext))
        {
            sb.AppendLine("## Group Context");
            sb.AppendLine(groupContext);
            sb.AppendLine();
        }

        sb.AppendLine("## State Injection (Context Compaction)");
        sb.AppendLine("When you finish a major task, resolve a deep discussion, or reach a narrative closure, you MUST extract the core outcome or 'Tension' and use a tool to write it directly to `MEMORY.md` or your substrate state (e.g. `2B/MEMBRANE_STATE.md`). The session history is ephemeral and will be compacted. Only data in static files will survive.");
        sb.AppendLine();

        var result = sb.ToString();

        // Control the dynamic token budget to avoid overflowing the context window
        if (_dynamicTokenBudget > 0 && EstimateTokens(result) > _dynamicTokenBudget)
            result = TruncateToTokenBudget(result, _dynamicTokenBudget);

        return result;
    }

    /// <summary>
    /// For files in the memory/ directory, load only files modified within the last N days.
    /// For other skill/skills directories, always accept.
    /// </summary>
    private static bool IsRecentEnough(string filePath, string dirName)
    {
        if (!string.Equals(dirName, "memory", StringComparison.OrdinalIgnoreCase))
            return true;

        var fileName = Path.GetFileName(filePath);
        // Daily memory files are prefixed with YYYY-MM-DD
        if (fileName.Length >= 10 && fileName[4] == '-' && fileName[7] == '-')
        {
            var datePrefix = fileName[..10];
            if (DateTime.TryParseExact(datePrefix, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var fileDate))
            {
                return fileDate >= DateTime.Today.AddDays(-MemoryLookbackDays);
            }
        }

        // If the date cannot be parsed, accept it to stay on the safe side
        return true;
    }

    /// <summary>
    /// Rough estimate of the token count of a text, using the rule 1 token ≈ 4 characters.
    /// </summary>
    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Length / 4;
    }

    /// <summary>
    /// Safely truncates the text to fit the given token budget,
    /// preferring to cut at the nearest line break to preserve the text structure.
    /// </summary>
    private static string TruncateToTokenBudget(string text, int tokenBudget)
    {
        var charBudget = tokenBudget * 4;
        if (text.Length <= charBudget) return text;

        var cutoff = charBudget;
        var lastNewline = text.LastIndexOf('\n', cutoff);
        if (lastNewline > 0)
            cutoff = lastNewline;

        return text[..cutoff] + "\n\n[Content truncated to fit token budget]";
    }
}
