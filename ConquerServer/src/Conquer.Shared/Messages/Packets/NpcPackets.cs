// Conquer.Shared — NPC server packet models.
// Original: my_server/NpcKernel/NetWork/MsgAiNpcInfo.h

namespace Conquer.Shared.Messages.Packets;

/// <summary>
/// Action types for MsgAiNpcInfo.
/// Original: my_server/NpcKernel/NetWork/MsgAiNpcInfo.h MSGAINPCINFO_CREATENEW = 1.
/// </summary>
public enum AiNpcAction : ushort
{
    None = 0,
    CreateNew = 1,
    Delete = 2,
}

/// <summary>
/// MsgAiNpcInfo (20001) — NPC server ↔ MsgServer monster generator and creation contract.
/// Original: my_server/NpcKernel/NetWork/MsgAiNpcInfo.h
/// Packet layout (34 bytes):
/// [len:2][type:2][id:4][idMap:4][idGen:4][idOwner:4][data:4][posX:2][posY:2][action:2][npcType:2][ownerType:2]
/// </summary>
public readonly record struct MsgAiNpcInfo(
    AiNpcAction Action,
    uint NpcId,
    uint GenId,
    ushort NpcType,
    uint MapId,
    ushort PosX,
    ushort PosY,
    uint OwnerId = 0,
    int Data = 0,
    ushort OwnerType = 0)
{
    public const int PacketSize = 34;

    public static MsgAiNpcInfo Decode(ReadOnlySpan<byte> packet)
    {
        PacketReader reader = new(packet);
        uint id = reader.ReadUInt32();
        uint idMap = reader.ReadUInt32();
        uint idGen = reader.ReadUInt32();
        uint idOwner = reader.ReadUInt32();
        int data = reader.ReadInt32();
        ushort posX = reader.ReadUInt16();
        ushort posY = reader.ReadUInt16();
        AiNpcAction action = (AiNpcAction)reader.ReadUInt16();
        ushort npcType = reader.ReadUInt16();
        ushort ownerType = reader.ReadUInt16();

        return new MsgAiNpcInfo(
            Action: action,
            NpcId: id,
            GenId: idGen,
            NpcType: npcType,
            MapId: idMap,
            PosX: posX,
            PosY: posY,
            OwnerId: idOwner,
            Data: data,
            OwnerType: ownerType);
    }

    public static byte[] Encode(MsgAiNpcInfo info)
    {
        using var writer = new PacketWriter(PacketSize + WireProtocol.TrailerSize);
        writer.Write(info.NpcId);
        writer.Write(info.MapId);
        writer.Write(info.GenId);
        writer.Write(info.OwnerId);
        writer.Write(info.Data);
        writer.Write(info.PosX);
        writer.Write(info.PosY);
        writer.Write((ushort)info.Action);
        writer.Write(info.NpcType);
        writer.Write(info.OwnerType);
        writer.Finalize(PacketType.AiNpcInfo);
        return writer.AsSpan().ToArray();
    }
}
