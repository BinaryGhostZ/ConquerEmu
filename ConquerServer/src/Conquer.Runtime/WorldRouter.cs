// Conquer.Runtime — WorldRouter: maps socket IDs to owning loops, routes packets.
// Original: SOCKET_SETPROCESSID records which kernel owns a socket.
// OPUS §3.4: incoming client packets are routed to the owning kernel via KERNEL_CLIENTMSG.
// OPUS §4.2.3: transfer protocol — rebind SECOND, forward table for gap packets.

using System.Collections.Concurrent;
using System.Threading.Channels;
using Conquer.Shared;
using Conquer.Shared.Messages;
using Microsoft.Extensions.Logging;

namespace Conquer.Runtime;

/// <summary>
/// Routes incoming client packets to the correct owning loop (MapGroup or World).
/// Thread-safe: called from the socket thread.
/// <para>
/// <b>Transfer protocol (OPUS §4.2.3):</b>
/// When a player moves between groups, the old group calls <see cref="Rebind"/> which:
/// 1. Sets the new owner
/// 2. Keeps a short-lived forward entry so packets arriving during the gap are forwarded
/// </para>
/// </summary>
public sealed class WorldRouter
{
    private readonly ILogger<WorldRouter> _logger;

    /// <summary>
    /// Maps socket ID → owning loop writer. A socket starts bound to World, then moves to a MapGroup.
    /// </summary>
    private readonly ConcurrentDictionary<int, ChannelWriter<InternalMessage>> _bindings = new();

    /// <summary>
    /// Short-lived forward entries for sockets in transit.
    /// Key: socket ID, Value: new owner. Checked only when the primary binding fails.
    /// </summary>
    private readonly ConcurrentDictionary<int, ChannelWriter<InternalMessage>> _forwards = new();

    /// <summary>All registered loop writers by port id.</summary>
    private readonly ConcurrentDictionary<int, ChannelWriter<InternalMessage>> _loops = new();

    public WorldRouter(ILogger<WorldRouter> logger)
    {
        _logger = logger;
    }

    /// <summary>Register a loop so it can receive messages by port id.</summary>
    public void RegisterLoop(int portId, ChannelWriter<InternalMessage> writer)
    {
        _loops[portId] = writer;
    }

    /// <summary>Get a loop writer by port id.</summary>
    public ChannelWriter<InternalMessage>? GetLoop(int portId)
    {
        return _loops.GetValueOrDefault(portId);
    }

    /// <summary>
    /// Bind a socket to an owning loop. Called when a new connection is accepted (→ World)
    /// or when SOCKET_SETPROCESSID is processed.
    /// </summary>
    public void Bind(int socketId, ChannelWriter<InternalMessage> owner)
    {
        _bindings[socketId] = owner;
    }

    /// <summary>
    /// Rebind a socket from one owner to another during a MapGroup transfer.
    /// The forward entry ensures packets in flight reach the new owner.
    /// </summary>
    public void Rebind(int socketId, ChannelWriter<InternalMessage> newOwner)
    {
        _forwards[socketId] = newOwner;
        _bindings[socketId] = newOwner;

        // Clean up the forward entry after a short delay.
        // In practice the forward is only needed for a few message cycles.
        _ = Task.Delay(TimeSpan.FromMilliseconds(TimingConstants.MapGroupOnTimerMs * 2))
            .ContinueWith(t => _forwards.TryRemove(socketId, out _), TaskScheduler.Default);
    }

    /// <summary>
    /// Route a client packet to the owning loop.
    /// Returns false if the socket has no binding (orphan — should be disconnected).
    /// </summary>
    public bool Route(int socketId, InternalMessage message)
    {
        if (_bindings.TryGetValue(socketId, out var writer))
        {
            return writer.TryWrite(message);
        }

        // Check forward table
        if (_forwards.TryGetValue(socketId, out var forward))
        {
            return forward.TryWrite(message);
        }

        _logger.LogWarning("No binding for socket {SocketId}, dropping message {MsgId}", socketId, message.Id);
        message.ReturnBuffer();
        return false;
    }

    /// <summary>
    /// Handle a disconnection. Posts PlayerLeft to the owning loop.
    /// If the socket has already been rebound, forward the disconnect.
    /// </summary>
    public void OnDisconnect(int socketId)
    {
        if (_bindings.TryRemove(socketId, out var owner))
        {
            var msg = InternalMessage.Signal(InternalMessageId.KernelCloseKernel, sourceId: socketId);
            owner.TryWrite(msg);
        }

        // Also clean up any forward entry
        _forwards.TryRemove(socketId, out _);
    }

    /// <summary>Remove all bindings (shutdown).</summary>
    public void Clear()
    {
        _bindings.Clear();
        _forwards.Clear();
    }
}
