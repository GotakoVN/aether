using Aether.Agent;
using Aether.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Agents;

/// <summary>
/// Represents the identity profile and the working-data partition of one agent (AgentProfile).
/// Responsible for the agent's name, root directory, LLM model configuration and loading its cognitive context.
/// </summary>
public class AgentProfile
{
    private readonly AgentConfig _config;
    private ContextAssembler? _contextAssembler;

    /// <summary>
    /// Unique name that identifies the agent (for example: maria, vesta, coda).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Absolute path of the directory that holds the agent's working files.
    /// </summary>
    public string AgentDirectory { get; }

    /// <summary>
    /// Detailed LLM configuration used only by this agent.
    /// </summary>
    public AgentModelConfig Model { get; }

    /// <summary>
    /// Friendly display name of the agent (for example: Maria, Aura, Vesta).
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Creates a new AgentProfile instance.
    /// </summary>
    /// <param name="name">Agent name.</param>
    /// <param name="agentDirectory">Absolute path of the working directory.</param>
    /// <param name="config">Configuration that holds the list of startup files.</param>
    /// <param name="model">LLM configuration.</param>
    /// <param name="displayName">Friendly display name.</param>
    public AgentProfile(string name, string agentDirectory, AgentConfig config, AgentModelConfig model, string? displayName = null)
    {
        Name = name;
        AgentDirectory = agentDirectory;
        _config = config;
        Model = model;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    }

    /// <summary>
    /// Creates an AgentProfile from the system configuration loader (ConfigLoader).
    /// Resolves the workspace dynamically and stays backward compatible with the legacy directory layout.
    /// </summary>
    /// <param name="name">Name of the agent to load.</param>
    /// <param name="configLoader">System configuration loader.</param>
    /// <param name="config">Base configuration.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The complete AgentProfile.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when no valid working directory is found.</exception>
    public static AgentProfile FromConfigLoader(
        string name,
        ConfigLoader configLoader,
        AgentConfig config,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        var agentConfig = configLoader.GetAgentConfig(name);
        var newPath = agentConfig?.Workspace;
        var model = agentConfig?.Model ?? new AgentModelConfig();
        var displayName = agentConfig?.DisplayName;

        if (!string.IsNullOrEmpty(newPath) && Directory.Exists(newPath))
        {
            return new AgentProfile(name, newPath, config, model, displayName);
        }

        // Backward compatible: <cwd>/agents/<name>/ (legacy layout)
        var legacyPath = Path.Combine(Environment.CurrentDirectory, "agents", name);
        if (Directory.Exists(legacyPath))
        {
            logger.LogWarning("Agent '{Name}' using legacy path {Path}. Migrate to ~/.aether/workspaces/{Name}/",
                name, legacyPath, name);
            return new AgentProfile(name, legacyPath, config, model, displayName);
        }

        throw new DirectoryNotFoundException(
            $"Agent directory not found for '{name}'. " +
            $"Tried: {newPath ?? "<no workspace in config>"} and {legacyPath}");
    }

    /// <summary>
    /// Loads and assembles the agent's complete dynamic identity context (Identity Context) from its working directory.
    /// </summary>
    /// <returns>The complete identity context string, to be used as the LLM system prompt.</returns>
    public virtual string LoadIdentityContext()
    {
        _contextAssembler ??= new ContextAssembler();
        return _contextAssembler.AssembleIdentityContext(AgentDirectory);
    }

    /// <summary>
    /// Loads the agent's initial persona (obsolete; use LoadIdentityContext instead).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Persona text assembled from the startup files.</returns>
    [Obsolete("Use LoadIdentityContext() instead.")]
    public virtual Task<string> LoadPersonaAsync(CancellationToken ct = default)
    {
        var parts = new List<string>();
        foreach (var file in _config.StartupFiles)
        {
            var content = LoadFile(file);
            if (content is not null) parts.Add(content);
        }
        return Task.FromResult(string.Join("\n\n", parts));
    }

    /// <summary>
    /// Synchronously reads the content of a local file inside the agent's working directory.
    /// </summary>
    /// <param name="relativePath">Relative file path.</param>
    /// <returns>The file content as a string, or <c>null</c> if the file does not exist.</returns>
    public virtual string? LoadFile(string relativePath)
    {
        var fullPath = Path.Combine(AgentDirectory, relativePath);
        if (!File.Exists(fullPath)) return null;
        return File.ReadAllText(fullPath);
    }

    /// <summary>
    /// Asynchronously reads the content of a local file inside the agent's working directory.
    /// </summary>
    /// <param name="relativePath">Relative file path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The file content as a string, or <c>null</c> if the file does not exist.</returns>
    public virtual async Task<string?> LoadFileAsync(string relativePath, CancellationToken ct = default)
        => LoadFile(relativePath);

    /// <summary>
    /// Synchronously loads the agent's daily memories for today and yesterday to enrich the context.
    /// </summary>
    /// <returns>Daily memory text assembled from the Markdown journal files.</returns>
    public virtual string LoadDailyMemory()
    {
        var parts = new List<string>();
        var dates = new[] { DateTime.UtcNow, DateTime.UtcNow.AddDays(-1) };
        foreach (var date in dates)
        {
            var filename = $"{date:yyyy-MM-dd}.md";
            var content = LoadFile(Path.Combine(_config.DailyMemoryDirectory, filename));
            if (content is not null) parts.Add(content);
        }
        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// Asynchronously loads the agent's daily memories for today and yesterday to enrich the context.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Daily memory text assembled together.</returns>
    public virtual async Task<string> LoadDailyMemoryAsync(CancellationToken ct = default)
        => LoadDailyMemory();
}
