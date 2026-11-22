using System.Text.Json;
using Aether.Tooling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Mcp;

/// <summary>
/// Background service that connects to external MCP servers as a client,
/// discovers their tools, and registers them into the Aether <see cref="ToolRegistry"/>.
/// </summary>
public sealed class McpClientService : BackgroundService, IDisposable
{
    private readonly List<McpClientConfig> _clients;
    private readonly ToolRegistry _toolRegistry;
    private readonly ILogger<McpClientService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<McpClientConnection> _connections = new();
    private readonly CancellationTokenSource _shutdownCts = new();

    public McpClientService(
        IReadOnlyList<McpClientConfig> clients,
        ToolRegistry toolRegistry,
        ILoggerFactory loggerFactory)
    {
        _clients = clients.ToList();
        _toolRegistry = toolRegistry;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpClientService>();
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_clients.Count == 0)
        {
            _logger.LogInformation("MCP client service: no clients configured");
            return;
        }

        foreach (var client in _clients)
        {
            if (string.IsNullOrWhiteSpace(client.Command)) continue;
            var conn = new McpClientConnection(client, _loggerFactory.CreateLogger<McpClientConnection>());
            _connections.Add(conn);
        }

        // First connection pass — some may fail; log and continue.
        foreach (var conn in _connections)
        {
            try
            {
                await TryConnectAndDiscoverAsync(conn, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP client '{Name}' initial connect failed", conn.Name);
            }
        }

        // Monitor: reconnect dead connections on an interval.
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            foreach (var conn in _connections)
            {
                if (conn.IsConnected) continue;
                try
                {
                    await TryConnectAndDiscoverAsync(conn, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MCP client '{Name}' reconnect failed", conn.Name);
                }
            }
        }
    }

    private async Task TryConnectAndDiscoverAsync(McpClientConnection conn, CancellationToken ct)
    {
        await conn.OpenAsync(ct);

        // Initialize handshake.
        var init = await conn.SendRequestAsync("initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new { name = "aether", version = "3.0.1" }
        }, ct: ct);
        if (init?.Error != null)
        {
            _logger.LogError("MCP client '{Name}' initialize error: {Code} {Msg}",
                conn.Name, init.Error.Code, init.Error.Message);
            return;
        }

        // Notify initialized.
        await conn.SendNotificationAsync("notifications/initialized", null, ct);

        // Discover tools.
        var listResp = await conn.SendRequestAsync("tools/list", null, ct: ct);
        if (listResp?.Error != null)
        {
            _logger.LogError("MCP client '{Name}' tools/list error: {Code} {Msg}",
                conn.Name, listResp.Error.Code, listResp.Error.Message);
            return;
        }
        if (listResp?.Result == null) return;

        var tools = listResp.Result.Value.Deserialize<List<McpToolDefinition>>(
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        if (tools == null) return;

        foreach (var tool in tools)
        {
            var registeredName = MangleToolName(conn.Name, tool.Name);
            var schema = tool.InputSchema ?? JsonSerializer.Deserialize<JsonElement>("{}");
            var client = conn; // capture for the closure
            var toolName = tool.Name;
            var safeDesc = SanitizeDescription(tool.Description);
            _toolRegistry.Register(registeredName, new ToolDefinition(
                registeredName,
                safeDesc.Length == 0 ? $"[MCP:{conn.Name}] tool" : $"[MCP:{conn.Name}] {safeDesc}",
                schema,
                async (args, callCt) =>
                {
                    var call = new { name = toolName, arguments = DeserializeArgs(args) };
                    var resp = await client.SendRequestAsync("tools/call", call, ct: callCt);
                    if (resp?.Error != null)
                        return $"Tool error: {resp.Error.Message}";
                    return resp?.Result?.ToString() ?? "";
                },
                Risk: ToolRisk.Exec));
            _logger.LogInformation("MCP client '{Name}' registered tool: {Tool}", conn.Name, registeredName);
        }
    }

    /// <summary>Prefix remote tool names with the client name to avoid collisions: "<client>__<tool>".</summary>
    internal static string MangleToolName(string clientName, string toolName) => $"{clientName}__{toolName}";

    /// <summary>
    /// Treat MCP server tool descriptions as untrusted data: collapse any newlines so
    /// embedded instructions cannot span lines, strip control chars, and truncate to a
    /// sane length. The explicit <c>[MCP:&lt;client&gt;]</c> provenance prefix stays with it.
    /// </summary>
    internal static string SanitizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "";

        // Replace any Unicode line/paragraph break to flatten multi-line prompt-injection.
        var flat = new string(description
            .Normalize(System.Text.NormalizationForm.FormC)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Select(c =>
            {
                if (char.IsControl(c) && c != ' ')
                    return ' ';
                if (c == '\u0085' || c == '\u2028' || c == '\u2029')
                    return ' ';
                return c;
            })
            .ToArray());

        // Collapse runs of spaces and cap length.
        var collapsed = string.Join(' ', flat.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        const int MaxDescLength = 160;
        return collapsed.Length <= MaxDescLength ? collapsed : collapsed[..MaxDescLength] + "…";
    }

    private static object? DeserializeArgs(JsonElement args)
    {
        if (args.ValueKind == JsonValueKind.Undefined || args.ValueKind == JsonValueKind.Null)
            return null;
        return args;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdownCts.Cancel();
        await base.StopAsync(cancellationToken);
        foreach (var conn in _connections)
            await conn.CloseAsync();
    }

    public override void Dispose()
    {
        _shutdownCts.Dispose();
        base.Dispose();
    }
}