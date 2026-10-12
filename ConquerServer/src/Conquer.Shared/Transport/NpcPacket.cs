// Conquer.Shared — NPC server packet envelope.
// Original: my_server/Share/protocol.h — CProtocolNpcMsgStruct, UniteNpcPacket / SplitNpcPacket.

namespace Conquer.Shared.Transport;

/// <summary>
/// Framed NPC packet carrying an NPC entity id, packet type, and payload.
/// Original: my_server/Share/protocol.h — CProtocolNpcMsgStruct, UniteNpcPacket / SplitNpcPacket.
/// </summary>
public readonly record struct NpcPacket(uint NpcId, PacketType PacketType, ReadOnlyMemory<byte> Payload)
{
    public int TotalWireLength => NpcWireProtocol.EnvelopeHeaderSize + Payload.Length;

    public static NpcPacket Create(uint npcId, PacketType packetType, byte[] payload) =>
        new(npcId, packetType, payload);

    public static NpcPacket Create(uint npcId, PacketType packetType, ReadOnlySpan<byte> payload) =>
        new(npcId, packetType, payload.ToArray());
}
