// Conquer.Shared — Combat and interaction packet models.
// Original: MapGroupKernel/Network/MsgInteract.h CMsgInteract (_MSG_INTERACT = 1022)
//           MapGroupKernel/Network/MsgMapItem.h CMsgMapItem (_MSG_MAPITEM = 1101)

using System.Buffers.Binary;

namespace Conquer.Shared.Messages.Packets;

/// <summary>
/// Interaction types for MsgInteract.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgInteract.h INTERACT_*</para>
/// </summary>
public enum InteractType : ushort
{
    None = 0,
    Steal = 1,
    Attack = 2,
    Heal = 3,
    Poison = 4,
    Assassinate = 5,
    Freeze = 6,
    Unfreeze = 7,
    Court = 8,
    Marry = 9,
    Divorce = 10,
    PresentMoney = 11,
    PresentItem = 12,
    SendFlowers = 13,
    Kill = 14,
    JoinSyn = 15,
    AcceptSynMember = 16,
    KickOutSynMember = 17,
    PresentPower = 18,
    QueryInfo = 19,
    RushAttack = 20,
    MagicAttack = 21,
    AbortMagic = 22,
    ReflectWeapon = 23,
    Bump = 24,
    Shoot = 25,
    ReflectMagic = 26,
    CutMeat = 27,
    Mine = 28,
    Quarry = 29,
    Chop = 30,
    Hustle = 31
}

/// <summary>
/// Interaction packet for combat, attacks, skills, marriage, etc.
/// <para>
/// <b>Original:</b> MapGroupKernel/Network/MsgInteract.h CMsgInteract (_MSG_INTERACT = 1022).
/// Layout: [len:2][type:2][dwTimeStamp:4][idSender:4][idTarget:4][unPosX:2][unPosY:2][unType:2][pad:2][dwData:4]
/// Total packet size: 28 bytes.
/// </para>
/// </summary>
public readonly record struct MsgInteract(
    uint Timestamp,
    uint SenderId,
    uint TargetId,
    ushort PosX,
    ushort PosY,
    InteractType Type,
    uint Data = 0)
{
    public const int PacketSize = 28;

    public ushort MagicType => (ushort)(Data & 0xFFFF);
    public ushort MagicLevel => (ushort)(Data >> 16);

    public static MsgInteract CreateAttack(uint senderId, uint targetId, ushort posX, ushort posY, uint damage, uint timestamp = 0)
    {
        return new MsgInteract(timestamp, senderId, targetId, posX, posY, InteractType.Attack, damage);
    }

    public static MsgInteract CreateKill(uint killerId, uint targetId, ushort posX, ushort posY, uint dieWay = 1, uint timestamp = 0)
    {
        return new MsgInteract(timestamp, killerId, targetId, posX, posY, InteractType.Kill, dieWay);
    }

    public static MsgInteract CreateMagicAttack(uint senderId, uint targetId, ushort posX, ushort posY, ushort magicType, ushort magicLevel, uint timestamp = 0)
    {
        uint data = (uint)(magicType | (magicLevel << 16));
        return new MsgInteract(timestamp, senderId, targetId, posX, posY, InteractType.MagicAttack, data);
    }

    public static MsgInteract Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint timestamp = reader.ReadUInt32();
        uint senderId = reader.ReadUInt32();
        uint targetId = reader.ReadUInt32();
        ushort posX = reader.ReadUInt16();
        ushort posY = reader.ReadUInt16();
        ushort type = reader.ReadUInt16();

        uint data = 0;
        // Check if there is padding before dwData (28-byte vs 26-byte struct)
        if (reader.Remaining >= 6)
        {
            reader.SeekForward(2); // Skip padding
            data = reader.ReadUInt32();
        }
        else if (reader.Remaining >= 4)
        {
            data = reader.ReadUInt32();
        }

        return new MsgInteract(timestamp, senderId, targetId, posX, posY, (InteractType)type, data);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(Timestamp);
        writer.Write(SenderId);
        writer.Write(TargetId);
        writer.Write(PosX);
        writer.Write(PosY);
        writer.Write((ushort)Type);
        writer.Write((ushort)0); // padding
        writer.Write(Data);
        writer.Finalize(PacketType.Interact);
    }

    public static byte[] Encode(MsgInteract interact)
    {
        using var writer = new PacketWriter(PacketSize + WireProtocol.TrailerSize);
        writer.Write(interact.Timestamp);
        writer.Write(interact.SenderId);
        writer.Write(interact.TargetId);
        writer.Write(interact.PosX);
        writer.Write(interact.PosY);
        writer.Write((ushort)interact.Type);
        writer.Write((ushort)0); // padding
        writer.Write(interact.Data);
        writer.Finalize(PacketType.Interact);
        return writer.AsSpan().ToArray();
    }

    public int Encode(Span<byte> destination)
    {
        if (destination.Length < PacketSize)
            throw new ArgumentException("Destination span too small for MsgInteract", nameof(destination));

        BinaryPrimitives.WriteUInt16LittleEndian(destination[0..2], PacketSize);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..4], (ushort)PacketType.Interact);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..8], Timestamp);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..12], SenderId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..16], TargetId);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[16..18], PosX);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[18..20], PosY);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[20..22], (ushort)Type);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[22..24], 0); // padding
        BinaryPrimitives.WriteUInt32LittleEndian(destination[24..28], Data);
        return PacketSize;
    }
}

/// <summary>
/// Actions for MsgMapItem.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgMapItem.h MSGMAPITEM_*</para>
/// </summary>
public enum MapItemAction : byte
{
    Create = 1,
    Delete = 2,
    Pick = 3,
    CastTrap = 10,
    SynchroTrap = 11,
    DropTrap = 12
}

/// <summary>
/// Packet for ground items and drops (money, equipment, gems).
/// <para>
/// <b>Original:</b> MapGroupKernel/Network/MsgMapItem.h CMsgMapItem (_MSG_MAPITEM = 1101).
/// Layout: [len:2][type:2][id:4][idType:4][unPosX:2][unPosY:2][nAction:1][pad:1]
/// </para>
/// </summary>
public readonly record struct MsgMapItem(
    uint ItemId,
    uint ItemType,
    ushort PosX,
    ushort PosY,
    MapItemAction Action)
{
    public const int PacketSize = 18;

    public static MsgMapItem CreateDrop(uint itemId, uint itemType, ushort posX, ushort posY)
    {
        return new MsgMapItem(itemId, itemType, posX, posY, MapItemAction.Create);
    }

    public static MsgMapItem CreateDelete(uint itemId, ushort posX, ushort posY)
    {
        return new MsgMapItem(itemId, 0, posX, posY, MapItemAction.Delete);
    }

    public static MsgMapItem Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint itemId = reader.ReadUInt32();
        uint itemType = reader.ReadUInt32();
        ushort posX = reader.ReadUInt16();
        ushort posY = reader.ReadUInt16();
        byte action = reader.ReadUInt8();

        return new MsgMapItem(itemId, itemType, posX, posY, (MapItemAction)action);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(ItemId);
        writer.Write(ItemType);
        writer.Write(PosX);
        writer.Write(PosY);
        writer.Write((byte)Action);
        writer.Write((byte)0); // padding
        writer.Finalize(PacketType.MapItem);
    }

    public int Encode(Span<byte> destination)
    {
        if (destination.Length < PacketSize)
            throw new ArgumentException("Destination span too small for MsgMapItem", nameof(destination));

        BinaryPrimitives.WriteUInt16LittleEndian(destination[0..2], PacketSize);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..4], (ushort)PacketType.MapItem);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..8], ItemId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..12], ItemType);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[12..14], PosX);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[14..16], PosY);
        destination[16] = (byte)Action;
        destination[17] = 0;
        return PacketSize;
    }
}
