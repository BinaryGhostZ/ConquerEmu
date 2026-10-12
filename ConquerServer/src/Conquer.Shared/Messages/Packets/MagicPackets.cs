// Conquer.Shared — Magic, spell, and skill packet models.
// Original: MapGroupKernel/Network/MsgMagicEffect.h (_MSG_MAGICEFFECT = 1105)
//           MapGroupKernel/Network/MsgMagicInfo.h (_MSG_MAGICINFO = 1103)
//           MapGroupKernel/Network/MsgFlushExp.h (_MSG_FLUSHEXP = 1104)

using System.Buffers.Binary;

namespace Conquer.Shared.Messages.Packets;

/// <summary>
/// A role and effect data affected by a magic spell.
/// Original: MsgMagicEffect.h EffectRoleStruct { DWORD idRole; DWORD dwData; }.
/// </summary>
public readonly record struct EffectRole(uint RoleId, uint Data);

/// <summary>
/// Packet broadcasting magic effects, spells, and damage numbers to observers.
/// <para>
/// <b>Original:</b> MapGroupKernel/Network/MsgMagicEffect.h CMsgMagicEffect (_MSG_MAGICEFFECT = 1105).
/// Layout: [len:2][type:2][idUser:4][idTarget/posX,posY:4][usType:2][usLevel:2][ucDir:1][ucEffectNum:1][pad:2]
///         followed by ucEffectNum * [idRole:4][dwData:4].
/// Base header size: 20 bytes.
/// </para>
/// </summary>
public readonly record struct MsgMagicEffect(
    uint UserId,
    uint TargetId,
    ushort PosX,
    ushort PosY,
    ushort MagicType,
    ushort Level,
    byte Direction,
    EffectRole[] Targets)
{
    public const int BaseHeaderSize = 20;

    public static MsgMagicEffect CreateSingle(
        uint userId,
        ushort magicType,
        ushort level,
        uint targetId,
        uint damage,
        byte direction = 0)
    {
        return new MsgMagicEffect(
            UserId: userId,
            TargetId: targetId,
            PosX: 0,
            PosY: 0,
            MagicType: magicType,
            Level: level,
            Direction: direction,
            Targets: [new EffectRole(targetId, damage)]);
    }

    public static MsgMagicEffect CreateArea(
        uint userId,
        ushort magicType,
        ushort level,
        ushort posX,
        ushort posY,
        byte direction,
        EffectRole[] targets)
    {
        return new MsgMagicEffect(
            UserId: userId,
            TargetId: 0,
            PosX: posX,
            PosY: posY,
            MagicType: magicType,
            Level: level,
            Direction: direction,
            Targets: targets);
    }

    public static MsgMagicEffect Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint userId = reader.ReadUInt32();
        uint targetIdOrPos = reader.ReadUInt32();
        ushort magicType = reader.ReadUInt16();
        ushort level = reader.ReadUInt16();
        byte direction = reader.ReadUInt8();
        byte effectNum = reader.ReadUInt8();
        reader.SeekForward(2); // padding to 4-byte boundary

        ushort posX = (ushort)(targetIdOrPos & 0xFFFF);
        ushort posY = (ushort)(targetIdOrPos >> 16);
        uint targetId = targetIdOrPos;

        var targets = new EffectRole[effectNum];
        for (int i = 0; i < effectNum && reader.Remaining >= 8; i++)
        {
            uint roleId = reader.ReadUInt32();
            uint data = reader.ReadUInt32();
            targets[i] = new EffectRole(roleId, data);
        }

        return new MsgMagicEffect(
            UserId: userId,
            TargetId: targetId,
            PosX: posX,
            PosY: posY,
            MagicType: magicType,
            Level: level,
            Direction: direction,
            Targets: targets);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(UserId);

        if (PosX != 0 || PosY != 0)
        {
            uint posCombined = (uint)(PosX | (PosY << 16));
            writer.Write(posCombined);
        }
        else
        {
            writer.Write(TargetId);
        }

        writer.Write(MagicType);
        writer.Write(Level);
        writer.Write(Direction);
        writer.Write((byte)Targets.Length);
        writer.Write((ushort)0); // 2 bytes padding

        foreach (var target in Targets)
        {
            writer.Write(target.RoleId);
            writer.Write(target.Data);
        }

        writer.Finalize(PacketType.MagicEffect);
    }
}

/// <summary>
/// Packet informing client of a learned magic spell, level, and current experience.
/// <para>
/// <b>Original:</b> MapGroupKernel/Network/MsgMagicInfo.h CMsgMagicInfo (_MSG_MAGICINFO = 1103).
/// Layout: [len:2][type:2][idOwner:4][dwExp:4][usType:2][usLevel:2] = 16 bytes.
/// </para>
/// </summary>
public readonly record struct MsgMagicInfo(
    uint OwnerId,
    uint Exp,
    ushort MagicType,
    ushort Level)
{
    public const int PacketSize = 16;

    public static MsgMagicInfo Create(uint ownerId, ushort magicType, ushort level, uint exp)
    {
        return new MsgMagicInfo(ownerId, exp, magicType, level);
    }

    public static MsgMagicInfo Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint ownerId = reader.ReadUInt32();
        uint exp = reader.ReadUInt32();
        ushort magicType = reader.ReadUInt16();
        ushort level = reader.ReadUInt16();

        return new MsgMagicInfo(ownerId, exp, magicType, level);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(OwnerId);
        writer.Write(Exp);
        writer.Write(MagicType);
        writer.Write(Level);
        writer.Finalize(PacketType.MagicInfo);
    }

    public int Encode(Span<byte> destination)
    {
        if (destination.Length < PacketSize)
            throw new ArgumentException("Destination span too small for MsgMagicInfo", nameof(destination));

        BinaryPrimitives.WriteUInt16LittleEndian(destination[0..2], PacketSize);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..4], (ushort)PacketType.MagicInfo);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..8], OwnerId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..12], Exp);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[12..14], MagicType);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[14..16], Level);
        return PacketSize;
    }
}

/// <summary>
/// Flush exp action type for MsgFlushExp.
/// </summary>
public enum FlushExpAction : byte
{
    WeaponSkill = 0,
    Magic = 1,
    Skill = 2
}

/// <summary>
/// Packet updating client with gained experience for a spell or weapon skill.
/// <para>
/// <b>Original:</b> MapGroupKernel/Network/MsgFlushExp.h CMsgFlushExp (_MSG_FLUSHEXP = 1104).
/// Layout: [len:2][type:2][dwExp:4][usType:2][ucAction:1][pad:1] = 12 bytes.
/// </para>
/// </summary>
public readonly record struct MsgFlushExp(
    uint Exp,
    ushort MagicType,
    FlushExpAction Action)
{
    public const int PacketSize = 12;

    public static MsgFlushExp CreateMagicExp(ushort magicType, uint exp)
    {
        return new MsgFlushExp(exp, magicType, FlushExpAction.Magic);
    }

    public static MsgFlushExp Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint exp = reader.ReadUInt32();
        ushort magicType = reader.ReadUInt16();
        byte action = reader.ReadUInt8();

        return new MsgFlushExp(exp, magicType, (FlushExpAction)action);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(Exp);
        writer.Write(MagicType);
        writer.Write((byte)Action);
        writer.Write((byte)0); // padding
        writer.Finalize(PacketType.FlushExp);
    }
}
