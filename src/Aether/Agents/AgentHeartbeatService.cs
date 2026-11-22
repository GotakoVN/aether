using Aether.Agent;
using Aether.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Agents;

/// <summary>
/// Periodic biological heartbeat service that runs in the background as a .NET IHostedService.
/// Reads the HEARTBEAT.md file periodically and sends its content through AetherSoul for processing.
/// Implements the OC heartbeat model: poll → execute tasks → report.
/// </summary>
public sealed class AgentHeartbeatService : IHostedService, IDisposable
{
    private readonly AgentProfile _profile;
    private readonly AetherSoul _soul;
    private readonly AgentConfig _config;
    private readonly ILogger<AgentHeartbeatService> _logger;
    private readonly HookEngine? _hooks;
    private readonly TimeSpan _interval;
    private int _tickNumber;
    private DateTimeOffset? _lastTickAt;
    private Timer? _timer;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Creates a new AgentHeartbeatService instance.
    /// </summary>
    /// <param name="profile">Identity profile of the active agent.</param>
    /// <param name="soul">AetherSoul thinking core that processes the heartbeat prompt.</param>
    /// <param name="config">Configuration that holds the heartbeat file path.</param>
    /// <param name="logger">Service logger.</param>
    /// <param name="interval">Interval between beats (default: 30 minutes).</param>
    /// <param name="hooks">Hook engine that coordinates the plugin system.</param>
    public AgentHeartbeatService(
        AgentProfile profile,
        AetherSoul soul,
        AgentConfig config,
        ILogger<AgentHeartbeatService> logger,
        TimeSpan? interval = null,
        HookEngine? hooks = null)
    {
        _profile = profile;
        _soul = soul;
        _config = config;
        _logger = logger;
        _hooks = hooks;
        _interval = interval ?? TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// Starts the periodic background heartbeat service.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken tied to the system startup lifetime.</param>
    /// <returns>A task representing the startup.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_config.HeartbeatFile is null)
        {
            _logger.LogInformation("Heartbeat disabled for agent {AgentName}", _profile.Name);
            return Task.CompletedTask;
        }

        _logger.LogInformation("Heartbeat starting for agent {AgentName} every {Interval}",
            _profile.Name, _interval);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timer = new Timer(async _ => await TickAsync(_cts.Token), null,
            TimeSpan.Zero, _interval);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Safely stops the background heartbeat service.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken that controls the shutdown.</param>
    /// <returns>A task representing the shutdown.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Heartbeat stopping for agent {AgentName}", _profile.Name);
        _timer?.Change(Timeout.Infinite, 0);
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Triggered on every Timer cycle to load HEARTBEAT.md, fire the OnHeartbeatTick hook,
    /// and send a silent self-reflection message through AetherSoul.
    /// </summary>
    /// <param name="ct">Cancellation token if the system shuts down.</param>
    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var tickNumber = Interlocked.Increment(ref _tickNumber);
            var timeSinceLastTick = _lastTickAt is null ? TimeSpan.Zero : now - _lastTickAt.Value;
            _lastTickAt = now;

            var heartbeatContent = await _profile.LoadFileAsync(_config.HeartbeatFile!, ct);
            if (heartbeatContent is null)
            {
                _logger.LogDebug("No heartbeat file found for {AgentName}", _profile.Name);
                return;
            }

            if (_hooks is not null)
            {
                var ctx = new OnHeartbeatTickContext
                {
                    AgentName = _profile.Name,
                    WorkspacePath = _profile.AgentDirectory,
                    TickNumber = tickNumber,
                    TimeSinceLastTick = timeSinceLastTick,
                    HeartbeatContent = heartbeatContent
                };
                await _hooks.RunAllAsync(HookPoint.OnHeartbeatTick, ctx, ct);
            }

            _logger.LogDebug("Heartbeat tick for {AgentName}", _profile.Name);
            // Send HEARTBEAT.md as working state, to avoid confusing it with the user's prompt.
            // This lets the agent handle recurring background tasks proactively.
            var response = await _soul.ProcessTaskAsync(_profile.Name, "Heartbeat tick.", heartbeatContent, ct);

            if (!response.Content.Contains("HEARTBEAT_OK"))
            {
                _logger.LogInformation("Heartbeat produced actionable output for {AgentName}", _profile.Name);
            }
        }
        catch (OperationCanceledException)
        {
            // The service is shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat tick failed for {AgentName}", _profile.Name);
        }
    }

    /// <summary>
    /// Releases the Timer and CancellationToken resources of the service.
    /// </summary>
    public void Dispose()
    {
        _timer?.Dispose();
        _cts?.Dispose();
    }
}
