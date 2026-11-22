using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Agents;

/// <summary>
/// Integrity verification status of a file.
/// </summary>
public enum IntegrityStatus
{
    /// <summary>Valid signature, data intact.</summary>
    Valid,
    /// <summary>Data was modified or the signature is invalid.</summary>
    Invalid,
    /// <summary>The file has not been signed.</summary>
    Unsigned,
    /// <summary>Public key or private key not found.</summary>
    KeyMissing
}

/// <summary>
/// Represents the result of an integrity check of one file.
/// </summary>
/// <param name="Status">Detailed verification status.</param>
/// <param name="Error">Message describing the error in detail, if any.</param>
public sealed record IntegrityResult(IntegrityStatus Status, string? Error);

/// <summary>
/// Digital signature controller and verifier (IntegritySigner) for an agent.
/// Implements the ECDSA P-256 (SHA-256) digital signature algorithm on .NET to protect the integrity
/// of the agent's constitution and core identity files, against code injection or accidental edits.
/// </summary>
public sealed class IntegritySigner
{
    private readonly string _agentDir;
    private readonly ILogger<IntegritySigner> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Creates a digital signature controller for an agent.
    /// </summary>
    /// <param name="agentDir">Root working directory of the agent.</param>
    /// <param name="logger">System logger.</param>
    public IntegritySigner(string agentDir, ILogger<IntegritySigner> logger)
    {
        _agentDir = agentDir;
        _logger = logger;
    }

    private string IntegrityDir => Path.Combine(_agentDir, "_INTEGRITY");
    private string PublicKeyPath => Path.Combine(IntegrityDir, "public.key");
    private string SignaturesDir => Path.Combine(IntegrityDir, "signatures");

    /// <summary>
    /// Overrides the specific path of the private key.
    /// If not set, the system looks first at ~/keys/{agent}-private.key before falling back to _INTEGRITY/private.key.
    /// </summary>
    public string? PrivateKeyPathOverride { get; set; }

    /// <summary>
    /// Resolves the exact path of the private key from the configuration and environment variables.
    /// </summary>
    private string ResolvePrivateKeyPath()
    {
        if (PrivateKeyPathOverride is not null && File.Exists(PrivateKeyPathOverride))
            return PrivateKeyPathOverride;

        var agentName = Path.GetFileName(_agentDir);
        var home = Environment.GetEnvironmentVariable("HOME")
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var keyFile = Path.Combine(home, "keys", $"{agentName}-private.key");
        if (File.Exists(keyFile)) return keyFile;

        return Path.Combine(IntegrityDir, "private.key");
    }

    /// <summary>
    /// Creates the agent's ECDSA P-256 key pair.
    /// The method is idempotent: it is skipped automatically if the key pair already exists.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A PEM string with the public key that was just loaded or created.</returns>
    public async Task<string> InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(IntegrityDir);
        Directory.CreateDirectory(SignaturesDir);

        if (File.Exists(PublicKeyPath) && File.Exists(ResolvePrivateKeyPath()))
        {
            var existing = await ReadAllTextAsync(PublicKeyPath, ct);
            _logger.LogDebug("Keypair already exists for agent at {Dir}", _agentDir);
            return existing;
        }

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = ecdsa.ExportSubjectPublicKeyInfoPem();
        var privateKey = ecdsa.ExportECPrivateKeyPem();

        await File.WriteAllTextAsync(PublicKeyPath, publicKey, ct);
        await File.WriteAllTextAsync(ResolvePrivateKeyPath(), privateKey, ct);

        // Restrict read permission on the private key file (chmod 600) on Unix-like operating systems
        if (!OperatingSystem.IsWindows())
        {
            try { System.Diagnostics.Process.Start("chmod", $"600 \"{ResolvePrivateKeyPath()}\"")?.WaitForExit(1000); }
            catch { /* best effort */ }
        }

        _logger.LogInformation("Generated new keypair for agent at {Dir}", _agentDir);
        return publicKey;
    }

    /// <summary>
    /// Signs one relative local file. Writes the matching *.sig signature file into the signatures/ directory.
    /// </summary>
    /// <param name="relativePath">Relative path of the file to sign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result and status of the signing.</returns>
    public async Task<IntegrityResult> SignAsync(string relativePath, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_agentDir, relativePath);
        if (!File.Exists(filePath))
            return new IntegrityResult(IntegrityStatus.Invalid, $"File not found: {relativePath}");

        if (!File.Exists(ResolvePrivateKeyPath()))
            return new IntegrityResult(IntegrityStatus.KeyMissing, "Private key not found. Run InitializeAsync first.");

        var content = await File.ReadAllBytesAsync(filePath, ct);
        var hash = SHA256.HashData(content);

        var privateKeyPem = await ReadAllTextAsync(ResolvePrivateKeyPath(), ct);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);

        var signature = ecdsa.SignHash(hash);

        var sigFileName = $"{SanitizeFileName(relativePath)}.sig";
        var sigPath = Path.Combine(SignaturesDir, sigFileName);

        var sigEntry = new SignatureEntry
        {
            Algorithm = "ECDSA-P256-SHA256",
            File = relativePath,
            Hash = Convert.ToHexStringLower(hash),
            Signature = Convert.ToBase64String(signature),
            SignedAt = DateTimeOffset.UtcNow.ToString("o")
        };

        var json = JsonSerializer.Serialize(sigEntry, JsonOptions);
        await File.WriteAllTextAsync(sigPath, json, ct);

        return new IntegrityResult(IntegrityStatus.Valid, null);
    }

    /// <summary>
    /// Verifies the digital signature of a local file using the stored public key.
    /// </summary>
    /// <param name="relativePath">Relative path of the file to verify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The integrity verification result (Valid, Invalid, Unsigned).</returns>
    public async Task<IntegrityResult> VerifyAsync(string relativePath, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_agentDir, relativePath);
        if (!File.Exists(filePath))
            return new IntegrityResult(IntegrityStatus.Invalid, $"File not found: {relativePath}");

        var sigFileName = $"{SanitizeFileName(relativePath)}.sig";
        var sigPath = Path.Combine(SignaturesDir, sigFileName);

        if (!File.Exists(sigPath))
            return new IntegrityResult(IntegrityStatus.Unsigned, $"No signature for: {relativePath}");

        if (!File.Exists(PublicKeyPath))
            return new IntegrityResult(IntegrityStatus.KeyMissing, "Public key not found");

        var content = await File.ReadAllBytesAsync(filePath, ct);
        var currentHash = SHA256.HashData(content);

        var sigJson = await ReadAllTextAsync(sigPath, ct);
        var sigEntry = JsonSerializer.Deserialize<SignatureEntry>(sigJson, JsonOptions);
        if (sigEntry is null)
            return new IntegrityResult(IntegrityStatus.Invalid, "Corrupt signature file");

        if (!string.Equals(sigEntry.Hash, Convert.ToHexStringLower(currentHash), StringComparison.OrdinalIgnoreCase))
            return new IntegrityResult(IntegrityStatus.Invalid, "Hash mismatch — file may have been modified");

        var signature = Convert.FromBase64String(sigEntry.Signature);
        var publicKeyPem = await ReadAllTextAsync(PublicKeyPath, ct);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(publicKeyPem);

        var valid = ecdsa.VerifyHash(currentHash, signature);
        return valid
            ? new IntegrityResult(IntegrityStatus.Valid, null)
            : new IntegrityResult(IntegrityStatus.Invalid, "Signature verification failed");
    }

    /// <summary>
    /// Verifies in bulk all files that have been signed in the signatures/ directory.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The list of files that failed verification, with detailed reasons.</returns>
    public async Task<IReadOnlyList<(string file, IntegrityResult result)>> VerifyAllAsync(CancellationToken ct = default)
    {
        var results = new List<(string, IntegrityResult)>();
        if (!Directory.Exists(SignaturesDir)) return results;

        foreach (var sigFile in Directory.GetFiles(SignaturesDir, "*.sig"))
        {
            var json = await ReadAllTextAsync(sigFile, ct);
            var entry = JsonSerializer.Deserialize<SignatureEntry>(json, JsonOptions);
            if (entry?.File is null) continue;

            var result = await VerifyAsync(entry.File, ct);
            if (result.Status != IntegrityStatus.Valid)
                results.Add((entry.File, result));
        }

        return results;
    }

    /// <summary>
    /// Signs in bulk the BootFiles (constitution and identity) defined in BootConfig.
    /// </summary>
    /// <param name="config">Startup configuration that contains the boot files.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SignBootFilesAsync(BootConfig config, CancellationToken ct = default)
    {
#pragma warning disable CS0618
        foreach (var file in config.ConstitutionFiles.Concat(config.IdentityFiles))
        {
            var result = await SignAsync(file, ct);
            if (result.Status == IntegrityStatus.Valid)
                _logger.LogDebug("Signed: {File}", file);
            else
                _logger.LogWarning("Failed to sign {File}: {Error}", file, result.Error);
        }
#pragma warning restore CS0618
    }

    private static string SanitizeFileName(string path)
    {
        return path.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
    }

    private static async Task<string> ReadAllTextAsync(string path, CancellationToken ct)
    {
        return await File.ReadAllTextAsync(path, ct);
    }

    private sealed record SignatureEntry
    {
        public string Algorithm { get; init; } = "";
        public string File { get; init; } = "";
        public string Hash { get; init; } = "";
        public string Signature { get; init; } = "";
        public string SignedAt { get; init; } = "";
    }
}
