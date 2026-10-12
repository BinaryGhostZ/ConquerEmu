// Conquer.Runtime — GameConnection.
// OPUS §4.3: Wires IGameSocket (Send/SendBatch/Kick) to the WorldRouter.

using Conquer.Net;
using Conquer.Shared.Messages;
using Microsoft.Extensions.Logging;

namespace Conquer.Runtime;

/// <summary>
/// Bridges a connected client socket (<see cref="IGameSocket"/>) to the internal <see cref="WorldRouter"/>.
/// Handles inbound packet routing and slow-client disconnection.
/// </summary>
public sealed class GameConnection
{
    private readonly int _socketId;
    private readonly IGameSocket _socket;
    private readonly WorldRouter _router;
    private readonly ILogger _logger;

    /// <summary>
    /// Kick limit: if the send queue grows beyond this, the client is assumed dead/slow and is dropped.
    /// Original: 10,000 packets.
    /// </summary>
    public const int MaxPendingSends = 10000;

    public int SocketId => _socketId;

    public GameConnection(int socketId, IGameSocket socket, WorldRouter router, ILogger logger)
    {
        _socketId = socketId;
        _socket = socket;
        _router = router;
        _logger = logger;
    }

    /// <summary>
    /// Attach the socket to this connection and start receiving.
    /// </summary>
    public void Attach(SecuritySocket securitySocket)
    {
        securitySocket.Attach(OnPacketReceived, OnDisconnect);
        securitySocket.Start();
    }

    private void OnPacketReceived(int socketId, ReadOnlySpan<byte> packet)
    {
        // OPUS §4.3: Kick a client whose PendingSends exceeds the limit.
        if (_socket.PendingSends > MaxPendingSends)
        {
            _logger.LogWarning("Socket {SocketId} exceeded max pending sends ({Pending}). Kicking.", socketId, _socket.PendingSends);
            _socket.Disconnect();
            return;
        }

        // OPUS §4.2.2: Socket thread copies the packet, posts it, and never touches game objects.
        // We create an InternalMessage (KernelClientMsg) with a copied buffer.
        byte[] copy = System.Buffers.ArrayPool<byte>.Shared.Rent(packet.Length);
        packet.CopyTo(copy);

        var msg = InternalMessage.WithPayload(
            id: InternalMessageId.KernelClientMsg,
            sourceId: socketId,
            secondaryId: 0,
            rentedBuffer: copy,
            length: packet.Length);

        // OPUS §3.4: Route to the owning kernel (World or MapGroup)
        if (!_router.Route(socketId, msg))
        {
            _logger.LogWarning("Socket {SocketId} has no owning loop binding. Disconnecting.", socketId);
            _socket.Disconnect();
        }
    }

    private void OnDisconnect(int socketId)
    {
        // OPUS §4.2.4: router.OnDisconnect posts PlayerLeft.
        _router.OnDisconnect(socketId);
    }

    /// <summary>
    /// Send a single packet to the client.
    /// </summary>
    public void Send(ReadOnlySpan<byte> packet) => _socket.Send(packet);

    /// <summary>
    /// Send a pre-built batch of packets to the client.
    /// </summary>
    public void SendBatch(ReadOnlySpan<byte> batch) => _socket.SendBatch(batch);

    /// <summary>
    /// Forcefully disconnect the client.
    /// </summary>
    public void Kick() => _socket.Disconnect();
}
