// Conquer.Runtime — WorldHost: orchestrates all loops, manages MapGroup allocation.
// Original: MsgServer/MsgServerDlg.cpp — creates all threads, assigns maps to groups.
// OPUS §4.1: dedicated groups + balanced shared groups.
// OPUS §12: N = max(2, cores-2) unless config says otherwise.

using System.Diagnostics.Metrics;
using Conquer.Shared;
using Microsoft.Extensions.Logging;

namespace Conquer.Runtime;

/// <summary>
/// Configuration for the WorldHost.
/// </summary>
public sealed class WorldHostConfig
{
    /// <summary>Number of MapGroups. Default: max(2, cores-2). Original: shell.ini [System] MAPGROUP_SIZE.</summary>
    public int MapGroupCount { get; set; }

    /// <summary>Dedicated group assignments: map ID → group index. Busy maps get their own group.</summary>
    public Dictionary<int, int> DedicatedMapGroups { get; set; } = [];

    /// <summary>Server name. Original: shell.ini [AccountServer] SERVERNAME.</summary>
    public string ServerName { get; set; } = "ConquerServer";

    /// <summary>Account server IP. Original: shell.ini [AccountServer] ACCOUNT_IP.</summary>
    public string AccountServerIp { get; set; } = "127.0.0.1";

    /// <summary>Account server port. Original: shell.ini [AccountServer] ACCOUNT_PORT.</summary>
    public int AccountServerPort { get; set; } = 9865;

    /// <summary>Login key for inter-process auth. Original: shell.ini [InternetPort] LOGIN_KEY.</summary>
    public string LoginKey { get; set; } = "";

    /// <summary>Database connection string.</summary>
    public string DatabaseConnectionString { get; set; } = "";

    /// <summary>Game server listen port.</summary>
    public int ListenPort { get; set; } = 5816;
}

/// <summary>
/// Orchestrates all server loops. Creates and manages the World loop, MapGroup loops,
/// and the WorldRouter.
/// <para>
/// <b>Original:</b> MsgServer/MsgServerDlg.cpp — the main dialog creates all kernel threads.
/// </para>
/// </summary>
public sealed class WorldHost : IAsyncDisposable
{
    private readonly ILogger<WorldHost> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IMeterFactory _meterFactory;
    private readonly TimeProvider _clock;
    private readonly WorldHostConfig _config;

    private WorldLoop? _worldLoop;
    private MapGroupLoop[] _mapGroups = [];
    private WorldRouter? _router;

    public WorldLoop WorldLoop => _worldLoop ?? throw new InvalidOperationException("Not started");
    public IReadOnlyList<MapGroupLoop> MapGroups => _mapGroups;
    public WorldRouter Router => _router ?? throw new InvalidOperationException("Not started");
    public WorldHostConfig Config => _config;

    public WorldHost(
        WorldHostConfig config,
        ILoggerFactory loggerFactory,
        IMeterFactory meterFactory,
        TimeProvider clock)
    {
        _config = config;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<WorldHost>();
        _meterFactory = meterFactory;
        _clock = clock;

        if (config.MapGroupCount <= 0)
        {
            config.MapGroupCount = Math.Max(2, Environment.ProcessorCount - 2);
        }
    }

    /// <summary>
    /// Create and start all loops.
    /// </summary>
    public void Start()
    {
        _logger.LogInformation("Starting WorldHost: {GroupCount} MapGroups, server {Name}",
            _config.MapGroupCount, _config.ServerName);

        _router = new WorldRouter(_loggerFactory.CreateLogger<WorldRouter>());

        // Create World loop
        _worldLoop = new WorldLoop(
            _loggerFactory.CreateLogger<WorldLoop>(),
            _meterFactory,
            _clock);

        _router.RegisterLoop(PortId.World, _worldLoop.Writer);
        _worldLoop.Start();

        // Create MapGroup loops
        _mapGroups = new MapGroupLoop[_config.MapGroupCount];
        for (int i = 0; i < _config.MapGroupCount; i++)
        {
            var group = new MapGroupLoop(
                i,
                _config.MapGroupCount,
                _loggerFactory.CreateLogger<MapGroupLoop>(),
                _meterFactory,
                _clock);

            _mapGroups[i] = group;
            _router.RegisterLoop(PortId.MapGroup(i), group.Writer);
            group.Start();
        }

        _logger.LogInformation("WorldHost started: World + {Count} MapGroups running", _config.MapGroupCount);
    }

    /// <summary>
    /// Graceful shutdown: stop accepting, drain, save players, close loops.
    /// Original: waits up to CLOSETHREAD_MILLISECS (30s).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("WorldHost shutting down...");

        // Stop MapGroups first (they may need to flush to World)
        var stopTasks = new List<Task>();
        foreach (var group in _mapGroups)
        {
            stopTasks.Add(group.StopAsync());
        }

        if (_worldLoop is not null)
        {
            stopTasks.Add(_worldLoop.StopAsync());
        }

        try
        {
            await Task.WhenAll(stopTasks).WaitAsync(TimeSpan.FromMilliseconds(TimingConstants.CloseThreadMs));
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Some loops did not stop within {Timeout}ms", TimingConstants.CloseThreadMs);
        }

        // Dispose all loops
        foreach (var group in _mapGroups)
        {
            group.Dispose();
        }
        _worldLoop?.Dispose();

        _router?.Clear();

        _logger.LogInformation("WorldHost stopped");
    }
}
