// Conquer.Shared — Internal message envelope.
// This replaces the original MessagePort Send(portTo, packetId, varType, data) model.
// In .NET we use Channel<InternalMessage> — one per loop, single reader.
// Original: Share/protocol.h structs (CLIENTMSG_PACKET0, NPCMSG_PACKET0, etc.)

using System.Buffers;

namespace Conquer.Shared.Messages;

/// <summary>
/// A single message passed between loops via <see cref="System.Threading.Channels.Channel{T}"/>.
/// <para>
/// <b>Buffer ownership:</b> The sender rents from <see cref="ArrayPool{T}.Shared"/>.
/// The receiver MUST return the buffer in a <c>finally</c> block after handling.
/// If <see cref="RentedBuffer"/> is null, the message carries no payload (e.g., a Tick signal).
/// </para>
/// </summary>
public readonly struct InternalMessage
{
    /// <summary>Message type. Maps to the original KERNEL_*, MAPGROUP_*, SOCKET_*, WORLD_* enums.</summary>
    public InternalMessageId Id { get; init; }

    /// <summary>Source socket ID (or entity ID, depending on message type).</summary>
    public int SourceId { get; init; }

    /// <summary>Secondary identifier (e.g., packet type for CLIENTMSG, NPC id for NPCMSG).</summary>
    public int SecondaryId { get; init; }

    /// <summary>
    /// Rented buffer containing the payload. Null for signal-only messages.
    /// <b>Ownership:</b> Transferred to the reader. Reader MUST call
    /// <c>ArrayPool&lt;byte&gt;.Shared.Return(RentedBuffer)</c> when done.
    /// </summary>
    public byte[]? RentedBuffer { get; init; }

    /// <summary>Number of valid bytes in <see cref="RentedBuffer"/>.</summary>
    public int Length { get; init; }

    /// <summary>Arbitrary integer data carried inline (avoids a buffer for small messages).</summary>
    public int IntData { get; init; }

    /// <summary>Read the payload as a span. Caller must check <see cref="RentedBuffer"/> is not null first.</summary>
    public ReadOnlySpan<byte> PayloadSpan => RentedBuffer.AsSpan(0, Length);

    /// <summary>Return the rented buffer to the pool. Safe to call even if <see cref="RentedBuffer"/> is null.</summary>
    public void ReturnBuffer()
    {
        if (RentedBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(RentedBuffer, clearArray: false);
        }
    }

    /// <summary>Creates a signal-only message (no payload).</summary>
    public static InternalMessage Signal(InternalMessageId id, int sourceId = 0, int intData = 0) =>
        new() { Id = id, SourceId = sourceId, IntData = intData };

    /// <summary>
    /// Creates a message with a rented payload buffer. The buffer ownership transfers to the receiver.
    /// </summary>
    public static InternalMessage WithPayload(
        InternalMessageId id,
        int sourceId,
        int secondaryId,
        byte[] rentedBuffer,
        int length) =>
        new()
        {
            Id = id,
            SourceId = sourceId,
            SecondaryId = secondaryId,
            RentedBuffer = rentedBuffer,
            Length = length,
        };
}
