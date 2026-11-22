using System.Text;

namespace Aether.Agents;

/// <summary>
/// Static utility class that loads and joins the content of an agent's list of startup configuration files.
/// Offers both async and sync reading so it can serve different startup phases.
/// </summary>
/// <remarks>
/// This class is a slimmed-down version inherited from the old BootContract; it drops the dependency on DI and unneeded state management,
/// which speeds up reading and reduces resource use.
/// </remarks>
public static class BootLoader
{
    /// <summary>
    /// Asynchronously loads the content of every valid file in the given list of paths and joins them together.
    /// </summary>
    /// <param name="agentDir">Root directory of the agent that holds the configuration files.</param>
    /// <param name="paths">List of relative file paths to load.</param>
    /// <param name="ct">Cancellation token (CancellationToken) used to control the lifetime of the operation.</param>
    /// <returns>A merged text containing the content of all loaded files, separated by newline characters.</returns>
    public static async Task<string> LoadFilesAsync(string agentDir, IReadOnlyList<string> paths, CancellationToken ct)
    {
        var sb = new StringBuilder();
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(agentDir, path);
            if (!File.Exists(fullPath)) continue;
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(await File.ReadAllTextAsync(fullPath, ct));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Synchronously loads the content of the relative files in the given list and joins them into a single string.
    /// </summary>
    /// <param name="agentDir">Root directory of the agent that holds the configuration files.</param>
    /// <param name="paths">List of relative file paths to load.</param>
    /// <returns>A text containing the joined content of the files, or <c>null</c> if no file exists or the list is empty.</returns>
    public static string? LoadFilesSync(string agentDir, IReadOnlyList<string> paths)
    {
        var sb = new StringBuilder();
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(agentDir, path);
            if (!File.Exists(fullPath)) continue;
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(File.ReadAllText(fullPath));
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }
}
