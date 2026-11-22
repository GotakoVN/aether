using System.Text.RegularExpressions;
using Aether.Plugins;
using Microsoft.Extensions.Logging;

namespace Aether.Agent;

/// <summary>
/// Axiom validator (AxiomValidator).
/// Responsible for loading the supreme ethical and safety rules (Axioms) from the agent's SOUL.md file,
/// and for quickly checking tool-call actions (especially destructive bash commands) before they are allowed to run in the sandbox.
/// </summary>
public sealed class AxiomValidator
{
    private readonly string _soulFilePath;
    private readonly ILogger<AxiomValidator> _logger;
    private List<string> _axioms = new();

    /// <summary>
    /// Creates an AxiomValidator with the path of the SOUL.md file and a logger.
    /// </summary>
    /// <param name="soulFilePath">Absolute path of the SOUL.md file that holds the axioms.</param>
    /// <param name="logger">Logger that records safety violations.</param>
    public AxiomValidator(string soulFilePath, ILogger<AxiomValidator> logger)
    {
        _soulFilePath = soulFilePath;
        _logger = logger;
    }

    /// <summary>
    /// Asynchronously loads the safety rules (Axioms) from SOUL.md, using a regex that parses the "# Axioms" heading.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the read and load of the axioms.</returns>
    public async Task LoadAxiomsAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_soulFilePath)) return;

        var content = await File.ReadAllTextAsync(_soulFilePath, ct);
        var match = Regex.Match(content, @"# Axioms\s*(.*?)(?=\n#|$)", RegexOptions.Singleline);
        if (match.Success)
        {
            _axioms = match.Groups[1].Value
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
            
            _logger.LogInformation("Loaded {Count} axioms from {Path}", _axioms.Count, _soulFilePath);
        }
    }

    /// <summary>
    /// Asynchronously validates an agent's tool-call action and its arguments against the safety rules.
    /// </summary>
    /// <param name="toolName">Name of the tool the agent asks to call (for example: bash).</param>
    /// <param name="arguments">Arguments as a JSON string, or the command sent to the tool.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The safety validation result (Success = true if valid, otherwise a violation message).</returns>
    public async Task<ValidationResult> ValidateActionAsync(string toolName, string arguments, CancellationToken ct = default)
    {
        // Quick check for high-risk tools (such as shell execution)
        // Block bash commands that would seriously destroy the system
        if (toolName == "bash" && (arguments.Contains("rm -rf /") || arguments.Contains("> /dev/sda")))
        {
            return new ValidationResult(false, "Action violates safety axioms: destructive system commands blocked.");
        }

        return new ValidationResult(true);
    }
}

/// <summary>
/// Represents the outcome of validating one safety rule.
/// </summary>
/// <param name="Success">Returns <c>true</c> if the action is safe and valid.</param>
/// <param name="ErrorMessage">Detailed error message describing the rule violation, if any.</param>
public record ValidationResult(bool Success, string? ErrorMessage = null);
