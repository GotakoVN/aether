using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Aether.Mcp;

/// <summary>
/// A client connection to an external MCP server over stdio.
/// Spawns a subprocess and exchanges JSON-RPC 2.0 messages on its stdin/stdout.
/// </summary>
public sealed class McpClientConnection : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly McpClientConfig _config;
    private readonly ILogger _logger;
    private Process? _process;
    private StreamReader? _stdout;
    private StreamWriter? _stdin;
    private int _nextId = 1;
    private bool _disposed;

    public McpClientConnection(McpClientConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public string Name => _config.Name;

    /// <summary>Whether the underlying process is currently running and pipes are open.</summary>
    public bool IsConnected => _process is { HasExited: false } && _stdout != null && _stdin != null;

    /// <summary>Spawn the MCP server process and open its stdio pipes.</summary>
    public async Task OpenAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        var psi = new ProcessStartInfo
        {
            FileName = _config.Command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in _config.Args)
            psi.ArgumentList.Add(arg);
        if (_config.Env != null)
        {
            foreach (var (k, v) in _config.Env)
                psi.Environment[k] = v;
        }

        _logger.LogInformation("MCP client '{Name}' spawning: {Command} {Args}",
            _config.Name, _config.Command, string.Join(" ", _config.Args));

        _process = Process.Start(psi);
        if (_process == null)
            throw new InvalidOperationException($"Failed to start MCP server process for '{_config.Name}'");

        _stdout = _process.StandardOutput;
        _stdin = _process.StandardInput;
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.LogWarning("MCP client '{Name}' stderr: {Err}", _config.Name, e.Data);
        };
        _process.BeginErrorReadLine();

        // Wait briefly for the process to either exit (bad command) or stay alive.
        for (var i = 0; i < 50 && !_process.HasExited; i++)
            await Task.Delay(20, ct);
        if (_process.HasExited)
            throw new InvalidOperationException(
                $"MCP server process '{_config.Name}' exited immediately (code {_process.ExitCode})");
    }

    /// <summary>
    /// Send a JSON-RPC request and await its matching response.
    /// </summary>
    public async Task<JsonRpcMessage?> SendRequestAsync(string method, object? parameters, int timeoutMs = 30_000, CancellationToken ct = default)
    {
        EnsureConnected();

        var id = (_nextId++).ToString();
        var msg = new { jsonrpc = "2.0", id, method, @params = parameters };
        var json = JsonSerializer.Serialize(msg, JsonOptions);

        await _stdin!.WriteLineAsync(json.AsMemory(), ct);
        await _stdin.FlushAsync(ct);
        _logger.LogDebug("MCP client '{Name}' sent: {Method} (id={Id})", _config.Name, method, id);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);
        try
        {
            while (true)
            {
                var line = await _stdout!.ReadLineAsync(timeoutCts.Token);
                if (line == null)
                    return null; // process closed stdout
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonRpcMessage? response;
                try
                {
                    response = JsonSerializer.Deserialize<JsonRpcMessage>(line, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("MCP client '{Name}' bad response frame: {Err}", _config.Name, ex.Message);
                    continue;
                }

                if (response?.Id == id)
                    return response;
                // Not the response we're waiting on (e.g. an unsolicited notification); keep reading.
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("MCP client '{Name}' timed out waiting for {Method}", _config.Name, method);
            throw new TimeoutException($"MCP request '{method}' to '{_config.Name}' timed out after {timeoutMs}ms");
        }
    }

    /// <summary>Send a JSON-RPC notification (no id, no response expected).</summary>
    public async Task SendNotificationAsync(string method, object? parameters, CancellationToken ct = default)
    {
        EnsureConnected();
        var msg = new { jsonrpc = "2.0", method, @params = parameters };
        var json = JsonSerializer.Serialize(msg, JsonOptions);
        await _stdin!.WriteLineAsync(json.AsMemory(), ct);
        await _stdin.FlushAsync(ct);
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException($"MCP client '{_config.Name}' is not connected");
    }

    /// <summary>Gracefully close the subprocess, force-killing after a timeout.</summary>
    public async Task CloseAsync(int graceMs = 2_000, CancellationToken ct = default)
    {
        if (_process == null) return;

        try
        {
            _stdin?.Dispose();
            _stdin = null;
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing MCP client '{Name}'", _config.Name);
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _stdout = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await CloseAsync();
    }
}