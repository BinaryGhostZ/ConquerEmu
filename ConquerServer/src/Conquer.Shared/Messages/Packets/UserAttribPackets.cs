// Conquer.Shared — User attribute, resource, and status sync packet models.
// Original: MapGroupKernel/Network/MsgUserAttrib.h (_MSG_USERATTRIB = 1017)

using System.Buffers.Binary;

namespace Conquer.Shared.Messages.Packets;

/// <summary>
/// Attribute types for MsgUserAttrib.
/// Original: MsgUserAttrib.h _USERATTRIB_*
/// </summary>
public enum UserAttributeType : uint
{
    Life = 0,
    MaxLife = 1,
    Mana = 2,
    MaxMana = 3,
    Money = 4,
    Exp = 5,
    Pk = 6,
    Profession = 7,
    LookFace = 12,
    Level = 13,
    KeepEffect = 26,     // Status effect flags (low 32 bits)
    Xp = 28,
    KeepEffect2 = 36     // Status effect flags (high 32 bits)
}

public readonly record struct UserAttributeEntry(UserAttributeType Type, uint Data);

/// <summary>
/// Packet updating player attributes (life, mana, xp, money, status flags).
/// <para>
/// <b>Original:</b> MapGroupKernel/Network/MsgUserAttrib.h CMsgUserAttrib (_MSG_USERATTRIB = 1017).
/// Layout: [len:2][type:2][idUser:4][dwAttributeNum:4] followed by dwAttributeNum * [ucAttributeType:4][dwAttributeData:4].
/// Base header size: 12 bytes.
/// </para>
/// </summary>
public readonly record struct MsgUserAttrib(
    uint UserId,
    UserAttributeEntry[] Attributes)
{
    public const int BaseHeaderSize = 12;

    public static MsgUserAttrib Create(uint userId, UserAttributeType type, uint data)
    {
        return new MsgUserAttrib(userId, [new UserAttributeEntry(type, data)]);
    }

    public static MsgUserAttrib CreateLife(uint userId, ushort currentLife)
    {
        return Create(userId, UserAttributeType.Life, currentLife);
    }

    public static MsgUserAttrib CreateMana(uint userId, ushort currentMana)
    {
        return Create(userId, UserAttributeType.Mana, currentMana);
    }

    public static MsgUserAttrib CreateXp(uint userId, byte xp)
    {
        return Create(userId, UserAttributeType.Xp, xp);
    }

    public static MsgUserAttrib CreateStatus(uint userId, ulong statusFlags)
    {
        uint low = (uint)(statusFlags & 0xFFFFFFFF);
        uint high = (uint)(statusFlags >> 32);

        return new MsgUserAttrib(userId, [
            new UserAttributeEntry(UserAttributeType.KeepEffect, low),
            new UserAttributeEntry(UserAttributeType.KeepEffect2, high)
        ]);
    }

    public static MsgUserAttrib Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint userId = reader.ReadUInt32();
        uint count = reader.ReadUInt32();

        var entries = new UserAttributeEntry[count];
        for (int i = 0; i < count && reader.Remaining >= 8; i++)
        {
            uint type = reader.ReadUInt32();
            uint data = reader.ReadUInt32();
            entries[i] = new UserAttributeEntry((UserAttributeType)type, data);
        }

        return new MsgUserAttrib(userId, entries);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(UserId);
        writer.Write((uint)Attributes.Length);

        foreach (var attr in Attributes)
        {
            writer.Write((uint)attr.Type);
            writer.Write(attr.Data);
        }

        writer.Finalize(PacketType.UserAttrib);
    }
}
