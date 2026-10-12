// Conquer.Shared — Transport abstraction for GameServer ↔ NpcServer.
// OPUS §5, §6.3: Two implementations: TCP (default) and in-process Channels (tests/single-box).

namespace Conquer.Shared.Transport;

/// <summary>
/// Transport contract for bidirectionally exchanging NpcPackets between GameServer and NpcServer.
/// </summary>
public interface INpcTransport : IAsyncDisposable
{
    /// <summary>
    /// Gets whether the underlying transport channel/connection is active and connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Sends an NPC packet across the transport.
    /// </summary>
    ValueTask SendAsync(NpcPacket packet, CancellationToken ct = default);

    /// <summary>
    /// Asynchronously streams incoming NPC packets received across the transport.
    /// </summary>
    IAsyncEnumerable<NpcPacket> ReceiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Closes the transport.
    /// </summary>
    ValueTask CloseAsync(CancellationToken ct = default);
}
