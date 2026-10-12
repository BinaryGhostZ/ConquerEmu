// Conquer.Net — IGameSocket interface.
// OPUS §4.3: IGameSocket is the only thing the world needs from a socket.

namespace Conquer.Net;

/// <summary>
/// Contract for a connected game socket.
/// </summary>
public interface IGameSocket
{
    /// <summary>
    /// Send a single packet.
    /// Implementation MUST copy the packet and encrypt the copy so the caller can reuse their buffer.
    /// </summary>
    void Send(ReadOnlySpan<byte> packet);

    /// <summary>
    /// Send a batch of packets (pre-built by the caller, e.g. from an outbox).
    /// Implementation MUST split the batch into individual packets, copy, and encrypt each in order.
    /// </summary>
    void SendBatch(ReadOnlySpan<byte> wholePackets);

    /// <summary>
    /// Disconnect the client.
    /// </summary>
    void Disconnect();

    /// <summary>
    /// Number of packets (or batches) currently waiting in the send queue.
    /// Used by the World to kick slow clients.
    /// </summary>
    int PendingSends { get; }
}
