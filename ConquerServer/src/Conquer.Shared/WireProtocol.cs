// Conquer.Shared — Wire protocol framing.
// Original: Share/networkdef.h — packet header [len:2][type:2], trailer "TQServer" (8 bytes).
// OPUS §3.9: len EXCLUDES the trailer. Client sends an 8-byte trailer too.

using System.Buffers.Binary;
using System.Text;

namespace Conquer.Shared;

/// <summary>
/// Wire protocol constants and helpers for the Conquer Online packet format.
/// <para>Packet format: <c>[len:2 LE][type:2 LE][body...][trailer:8]</c></para>
/// <para><c>len</c> = total packet size EXCLUDING the 8-byte trailer.</para>
/// </summary>
public static class WireProtocol
{
    /// <summary>Minimum packet size: 2 (len) + 2 (type) = 4 bytes (before trailer).</summary>
    public const int MinPacketSize = 4;

    /// <summary>Maximum packet size (excluding trailer). Original: _MAX_MSGSIZE = 1024, but packets can exceed this.</summary>
    public const int MaxPacketSize = 1024;

    /// <summary>Trailer size in bytes. Original: TQ_CLIENT_SIGNATURE / "TQServer".</summary>
    public const int TrailerSize = 8;

    /// <summary>Server-side trailer bytes. Original: "TQServer".</summary>
    public static ReadOnlySpan<byte> ServerTrailer => "TQServer"u8;

    /// <summary>Offset of the packet type field in the header.</summary>
    public const int TypeOffset = 2;

    /// <summary>
    /// Read the packet length from the first 2 bytes (little-endian).
    /// This is the size of the packet EXCLUDING the trailer.
    /// </summary>
    public static ushort ReadLength(ReadOnlySpan<byte> header) =>
        BinaryPrimitives.ReadUInt16LittleEndian(header);

    /// <summary>
    /// Read the packet type from bytes 2..3 (little-endian).
    /// </summary>
    public static PacketType ReadType(ReadOnlySpan<byte> packet) =>
        (PacketType)BinaryPrimitives.ReadUInt16LittleEndian(packet[TypeOffset..]);

    /// <summary>
    /// Write a packet header (length + type) into the destination span.
    /// </summary>
    public static void WriteHeader(Span<byte> destination, ushort length, PacketType type)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, length);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[TypeOffset..], (ushort)type);
    }

    /// <summary>
    /// Append the server trailer to a packet in the destination span.
    /// </summary>
    public static void WriteServerTrailer(Span<byte> destination)
    {
        ServerTrailer.CopyTo(destination);
    }

    /// <summary>
    /// Total bytes on the wire for a packet of the given content length.
    /// </summary>
    public static int WireSize(int packetLength) => packetLength + TrailerSize;

    /// <summary>
    /// Validate basic packet sanity. Returns false if the packet is malformed.
    /// </summary>
    public static bool IsValidPacket(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < MinPacketSize)
            return false;

        ushort declaredLen = ReadLength(packet);
        return declaredLen >= MinPacketSize && declaredLen <= MaxPacketSize && declaredLen <= packet.Length;
    }
}

/// <summary>
/// NPC server packet framing — wraps/unwraps packets with the NPC entity id.
/// Original: Share/protocol.h — PROTOCOL_NPCMSG_HEADSIZE = 8, CProtocolNpcMsgStruct, UniteNpcPacket / SplitNpcPacket.
/// Format: [msgSize:2 LE][packetId:2 LE][npcId:4 LE][body...]
/// </summary>
public static class NpcWireProtocol
{
    /// <summary>NPC packet envelope header size: size(2) + packetId(2) + npcId(4) = 8 bytes.</summary>
    public const int EnvelopeHeaderSize = 8;

    /// <summary>
    /// Write an NPC envelope header into the destination.
    /// </summary>
    public static void WriteEnvelope(Span<byte> destination, ushort msgSize, PacketType packetType, uint npcId)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, msgSize);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], (ushort)packetType);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], npcId);
    }

    /// <summary>
    /// Read an NPC envelope header.
    /// </summary>
    public static (ushort MsgSize, PacketType PacketType, uint NpcId) ReadEnvelope(ReadOnlySpan<byte> source)
    {
        ushort msgSize = BinaryPrimitives.ReadUInt16LittleEndian(source);
        PacketType packetType = (PacketType)BinaryPrimitives.ReadUInt16LittleEndian(source[2..]);
        uint npcId = BinaryPrimitives.ReadUInt32LittleEndian(source[4..]);
        return (msgSize, packetType, npcId);
    }

    /// <summary>
    /// Encodes an NpcPacket into a byte array with the 8-byte CProtocolNpcMsgStruct envelope header.
    /// </summary>
    public static byte[] Encode(Transport.NpcPacket packet)
    {
        int totalSize = EnvelopeHeaderSize + packet.Payload.Length;
        byte[] buffer = new byte[totalSize];
        WriteEnvelope(buffer, (ushort)totalSize, packet.PacketType, packet.NpcId);
        packet.Payload.Span.CopyTo(buffer.AsSpan(EnvelopeHeaderSize));
        return buffer;
    }

    /// <summary>
    /// Attempts to decode an NpcPacket from a buffer. Returns false if there are fewer than EnvelopeHeaderSize bytes
    /// or if the buffer does not yet contain the entire packet.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> buffer, out Transport.NpcPacket packet, out int consumedBytes)
    {
        packet = default;
        consumedBytes = 0;
        if (buffer.Length < EnvelopeHeaderSize)
            return false;

        var (msgSize, packetType, npcId) = ReadEnvelope(buffer);
        if (msgSize < EnvelopeHeaderSize || msgSize > buffer.Length)
            return false;

        consumedBytes = msgSize;
        byte[] payload = buffer.Slice(EnvelopeHeaderSize, msgSize - EnvelopeHeaderSize).ToArray();
        packet = new Transport.NpcPacket(npcId, packetType, payload);
        return true;
    }
}
