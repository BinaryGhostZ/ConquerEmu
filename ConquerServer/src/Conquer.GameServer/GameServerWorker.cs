// Conquer.GameServer — Hosted background service orchestrating WorldHost and network listeners.
// Original: MsgServer/MsgServerDlg.cpp, MsgServer/SocketThread.cpp.
// OPUS §3.1, §3.4, §3.5.

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Conquer.Data;
using Conquer.Net;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Conquer.GameServer;

/// <summary>
/// Hosted background service managing the complete Game Server (MsgServer) execution:
/// WorldHost orchestration, network listeners for game clients, and IPC login authentication handoffs.
/// </summary>
public sealed class GameServerWorker : BackgroundService
{
    private readonly ILogger<GameServerWorker> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly WorldHostConfig _config;
    private readonly IMeterFactory _meterFactory;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<int, SecuritySocket> _activeSockets = new();
    private int _nextSocketId = 1000;

    private WorldHost? _worldHost;

    public WorldHost? WorldHost => _worldHost;

    public GameServerWorker(
        WorldHostConfig config,
        ILoggerFactory loggerFactory,
        ILogger<GameServerWorker> logger,
        IMeterFactory meterFactory,
        TimeProvider clock)
    {
        _config = config;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _meterFactory = meterFactory;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Initializing Conquer Online Game Server: {Name}", _config.ServerName);

        // 1. Initialize and start WorldHost
        _worldHost = new WorldHost(_config, _loggerFactory, _meterFactory, _clock);
        InitializeDefaultMaps(_worldHost);
        _worldHost.Start();

        // 2. Start IPC listener for AccountServer login pairing on port 5817
        const int ipcPort = 5817;
        var ipcListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        ipcListener.Bind(new IPEndPoint(IPAddress.Any, ipcPort));
        ipcListener.Listen(50);
        _logger.LogInformation("GameServer IPC listener active on port {Port}", ipcPort);

        // 3. Start Game Client listener on configured game port (default 5816)
        var clientListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        clientListener.Bind(new IPEndPoint(IPAddress.Any, _config.ListenPort));
        clientListener.Listen(500);
        _logger.LogInformation("GameServer Client listener active on port {Port}", _config.ListenPort);

        // Background accept loops
        var ipcTask = AcceptIpcLoopAsync(ipcListener, stoppingToken);
        var clientTask = AcceptClientLoopAsync(clientListener, stoppingToken);

        try
        {
            await Task.WhenAll(ipcTask, clientTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("GameServerWorker cancellation received");
        }
        finally
        {
            ipcListener.Close();
            clientListener.Close();

            // Disconnect active client sockets
            foreach (var kvp in _activeSockets)
            {
                kvp.Value.Disconnect();
            }
            _activeSockets.Clear();

            if (_worldHost is not null)
            {
                await _worldHost.DisposeAsync().ConfigureAwait(false);
            }

            _logger.LogInformation("GameServerWorker terminated cleanly");
        }
    }

    private async Task AcceptClientLoopAsync(Socket listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await listener.AcceptAsync(ct).ConfigureAwait(false);
                int socketId = Interlocked.Increment(ref _nextSocketId);

                _logger.LogInformation("Incoming game client connection accepted. Assigned socket ID: {SocketId}", socketId);

                var secSocket = new SecuritySocket(socketId, clientSocket, new PlaintextCrypto(), _loggerFactory.CreateLogger<SecuritySocket>());
                _activeSockets[socketId] = secSocket;

                // Bind socket to World loop initially
                _worldHost!.Router.Bind(socketId, _worldHost.WorldLoop.Writer);

                secSocket.Attach((sId, packetSpan) =>
                {
                    try
                    {
                        var packetType = (int)WireProtocol.ReadType(packetSpan);
                        var rented = ArrayPool<byte>.Shared.Rent(packetSpan.Length);
                        packetSpan.CopyTo(rented);

                        var msg = InternalMessage.WithPayload(
                            InternalMessageId.KernelClientMsg,
                            sId,
                            packetType,
                            rented,
                            packetSpan.Length);

                        if (!_worldHost.Router.Route(sId, msg))
                        {
                            _logger.LogWarning("Failed to route client message from socket {SocketId}", sId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing client packet from socket {SocketId}", sId);
                    }
                }, sId =>
                {
                    _logger.LogInformation("Client socket {SocketId} disconnected", sId);
                    _activeSockets.TryRemove(sId, out _);
                    _worldHost.Router.OnDisconnect(sId);
                });

                secSocket.Start();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting game client connection");
            }
        }
    }

    private async Task AcceptIpcLoopAsync(Socket listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ipcSocket = await listener.AcceptAsync(ct).ConfigureAwait(false);
                _ = ProcessIpcConnectionAsync(ipcSocket, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting IPC connection");
            }
        }
    }

    private async Task ProcessIpcConnectionAsync(Socket socket, CancellationToken ct)
    {
        using (socket)
        {
            try
            {
                var buffer = new byte[256];
                int bytesRead = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, ct).ConfigureAwait(false);
                if (bytesRead > 0)
                {
                    var rented = ArrayPool<byte>.Shared.Rent(bytesRead);
                    buffer.AsSpan(0, bytesRead).CopyTo(rented);

                    var msg = InternalMessage.WithPayload(
                        InternalMessageId.KernelClientMsg,
                        sourceId: 0,
                        secondaryId: (int)PacketType.LoginAccount,
                        rented,
                        bytesRead);

                    _worldHost!.WorldLoop.Writer.TryWrite(msg);
                    _logger.LogInformation("Processed IPC auth message from AccountServer ({Bytes} bytes)", bytesRead);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing IPC connection");
            }
        }
    }

    private static void InitializeDefaultMaps(WorldHost host)
    {
        if (host.MapGroups.Count == 0) return;

        // Partition core Conquer Online maps across MapGroups
        var defaultMaps = new (uint Id, string Name)[]
        {
            (1002, "TwinCity"),
            (1011, "PhoenixCastle"),
            (1020, "ApeMountain"),
            (1000, "DesertCity"),
            (1015, "BirdIsland")
        };

        for (int i = 0; i < defaultMaps.Length; i++)
        {
            var mapInfo = defaultMaps[i];
            var mapGroup = host.MapGroups[i % host.MapGroups.Count];
            var cellMap = new CellMap(300, 300, defaultWalkable: true);
            var map = new GameMap(new MapModel { Id = mapInfo.Id, Name = mapInfo.Name }, cellMap);
            mapGroup.AddMap(map);
        }
    }
}
