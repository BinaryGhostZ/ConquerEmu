// Conquer.Shared — Movement, action, entity spawn, and heartbeat packets.
// Original: MapGroupKernel/Network/MsgWalk.h, MsgAction.h, MsgPlayer.h, MsgTick.h

namespace Conquer.Shared.Messages.Packets;

/// <summary>
/// Movement packet for 1-step walking/running in 8 directions.
/// <para>
/// <b>Original:</b> MapGroupKernel/Network/MsgWalk.h CMsgWalk (_MSG_WALK = 1005).
/// Layout: [len:2][type:2][idUser:4][ucDir:1][ucMode:1]
/// </para>
/// </summary>
public readonly record struct MsgWalk(uint EntityId, Direction Direction, byte Mode = 0)
{
    public static MsgWalk Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint entityId = reader.ReadUInt32();
        byte dir = reader.ReadUInt8();
        byte mode = reader.Remaining > 0 ? reader.ReadUInt8() : (byte)0;
        return new MsgWalk(entityId, (Direction)(dir % 8), mode);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(EntityId);
        writer.Write((byte)Direction);
        writer.Write(Mode);
        writer.Finalize(PacketType.Walk);
    }

    public static byte[] Encode(MsgWalk walk)
    {
        using var writer = new PacketWriter();
        writer.Write(walk.EntityId);
        writer.Write((byte)walk.Direction);
        writer.Write(walk.Mode);
        writer.Finalize(PacketType.Walk);
        return writer.AsSpan().ToArray();
    }

    public MsgWalk(uint entityId, Direction direction, WalkMode mode)
        : this(entityId, direction, (byte)mode)
    {
    }
}

/// <summary>
/// Movement modes for MsgWalk.
/// Original: my_server/NpcKernel/define.h WALKMODE_* constants.
/// </summary>
public enum WalkMode : byte
{
    Walk = 0,
    Run = 1,
    Shift = 2,
    Jump = 3,
}

/// <summary>
/// Action type enumeration for CMsgAction.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgAction.h</para>
/// </summary>
public enum ActionType : ushort
{
    None = 0,
    ChangeDir = 1,
    Position = 2,
    Emotion = 3,
    BroadcastPos = 4,
    Divorce = 5,
    SelfUnfreeze = 6,
    FlyMap = 8,
    ChgWeather = 9,
    Fireworks = 10,
    Die = 11,
    QuitSyn = 12,
    Walk = 13,
    EnterMap = 14,
    Forward = 17,
    Jump = 19,
    Run = 20,
    Equip = 21,
    Unequip = 22,
    XpClear = 24,
    Reborn = 25,
    DelRole = 26,
    SetPkMode = 29,
    GetSynAttr = 30,
    QueryFriendInfo = 33,
    ChangeFace = 35,
    GetMoney = 53,
    KickBack = 57,
    SoundEffect = 60,
    Disappear = 97,
    UpLev = 132
}

/// <summary>
/// General action packet (Jump, Emotion, Direction, Stance, etc.).
/// <para>
/// <b>Original:</b> MapGroupKernel/Network/MsgAction.h CMsgAction (_MSG_ACTION = 1010).
/// Layout: [len:2][type:2][dwTimestamp:4][idUser:4][unPosX:2][unPosY:2][unDir:2][dwData:4][usAction:2]
/// For Jump: dwData holds usTargetPosX (low 16) and usTargetPosY (high 16).
/// </para>
/// </summary>
public readonly record struct MsgAction(
    uint Timestamp,
    uint EntityId,
    ushort PosX,
    ushort PosY,
    ushort Dir,
    ActionType Action,
    uint Data = 0)
{
    public ushort TargetPosX => (ushort)(Data & 0xFFFF);
    public ushort TargetPosY => (ushort)(Data >> 16);

    public static MsgAction CreateJump(uint entityId, ushort posX, ushort posY, ushort targetX, ushort targetY, ushort dir = 0, uint timestamp = 0)
    {
        uint data = (uint)(targetX | (targetY << 16));
        return new MsgAction(timestamp, entityId, posX, posY, dir, ActionType.Jump, data);
    }

    public static MsgAction CreateDisappear(uint entityId)
    {
        return new MsgAction(0, entityId, 0, 0, 0, ActionType.Disappear, 0);
    }

    public static MsgAction CreateDie(uint entityId, ushort posX, ushort posY, uint dieWay = 1)
    {
        return new MsgAction(0, entityId, posX, posY, 0, ActionType.Die, dieWay);
    }

    public static MsgAction CreateDelRole(uint entityId)
    {
        return new MsgAction(0, entityId, 0, 0, 0, ActionType.DelRole, 0);
    }

    public static MsgAction Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint timestamp = reader.ReadUInt32();
        uint entityId = reader.ReadUInt32();
        ushort posX = reader.ReadUInt16();
        ushort posY = reader.ReadUInt16();
        ushort dir = reader.ReadUInt16();
        uint data = reader.ReadUInt32();
        ushort action = reader.ReadUInt16();

        return new MsgAction(timestamp, entityId, posX, posY, dir, (ActionType)action, data);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(Timestamp);
        writer.Write(EntityId);
        writer.Write(PosX);
        writer.Write(PosY);
        writer.Write(Dir);
        writer.Write(Data);
        writer.Write((ushort)Action);
        writer.Finalize(PacketType.Action);
    }

    public static byte[] Encode(MsgAction action)
    {
        using var writer = new PacketWriter();
        writer.Write(action.Timestamp);
        writer.Write(action.EntityId);
        writer.Write(action.PosX);
        writer.Write(action.PosY);
        writer.Write(action.Dir);
        writer.Write(action.Data);
        writer.Write((ushort)action.Action);
        writer.Finalize(PacketType.Action);
        return writer.AsSpan().ToArray();
    }
}

/// <summary>
/// Heartbeat tick packet sent periodically between client and server.
/// <para>
/// <b>Original:</b> MapGroupKernel/Network/MsgTick.h CMsgTick (_MSG_TICK = 1012).
/// Layout: [len:2][type:2][idPlayer:4][dwData:4][dwChkData:4]
/// </para>
/// </summary>
public readonly record struct MsgTick(uint PlayerId, uint Data = 0, uint CheckData = 0)
{
    public static MsgTick Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint playerId = reader.ReadUInt32();
        uint data = reader.ReadUInt32();
        uint chkData = reader.ReadUInt32();
        return new MsgTick(playerId, data, chkData);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(PlayerId);
        writer.Write(Data);
        writer.Write(CheckData);
        writer.Finalize(PacketType.Tick);
    }
}

/// <summary>
/// Entity spawn packet sent to clients entering view of a player.
/// <para>
/// <b>Original:</b> MapGroupKernel/Network/MsgPlayer.h CMsgPlayer (_MSG_PLAYER = 1014).
/// </para>
/// </summary>
public readonly record struct MsgPlayer(
    uint EntityId,
    uint LookFace,
    uint Status,
    uint SynIdRank,
    uint ArmorType,
    uint WeaponRType,
    ushort MaxLife,
    ushort Life,
    ushort Level,
    ushort PosX,
    ushort PosY,
    ushort Hair,
    byte Dir,
    byte Pose,
    string Name)
{
    public static MsgPlayer Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint id = reader.ReadUInt32();
        uint lookFace = reader.ReadUInt32();
        uint status = reader.ReadUInt32();
        uint synIdRank = reader.ReadUInt32();
        uint armor = reader.ReadUInt32();
        uint weaponR = reader.ReadUInt32();
        ushort maxLife = reader.ReadUInt16();
        ushort life = reader.ReadUInt16();
        ushort level = reader.ReadUInt16();
        ushort posX = reader.ReadUInt16();
        ushort posY = reader.ReadUInt16();
        ushort hair = reader.ReadUInt16();
        byte dir = reader.ReadUInt8();
        byte pose = reader.ReadUInt8();

        // Length-prefixed string list at the end
        string name = string.Empty;
        if (reader.Remaining > 0)
        {
            byte strCount = reader.ReadUInt8();
            if (strCount > 0 && reader.Remaining > 0)
            {
                byte strLen = reader.ReadUInt8();
                name = reader.ReadString(strLen);
            }
        }

        return new MsgPlayer(
            id, lookFace, status, synIdRank, armor, weaponR,
            maxLife, life, level, posX, posY, hair, dir, pose, name);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(EntityId);
        writer.Write(LookFace);
        writer.Write(Status);
        writer.Write(SynIdRank);
        writer.Write(ArmorType);
        writer.Write(WeaponRType);
        writer.Write(MaxLife);
        writer.Write(Life);
        writer.Write(Level);
        writer.Write(PosX);
        writer.Write(PosY);
        writer.Write(Hair);
        writer.Write(Dir);
        writer.Write(Pose);

        // String list: count = 1, len, string
        writer.Write(Name);
        writer.Finalize(PacketType.Player);
    }

    public static byte[] Encode(MsgPlayer player)
    {
        using var writer = new PacketWriter();
        writer.Write(player.EntityId);
        writer.Write(player.LookFace);
        writer.Write(player.Status);
        writer.Write(player.SynIdRank);
        writer.Write(player.ArmorType);
        writer.Write(player.WeaponRType);
        writer.Write(player.MaxLife);
        writer.Write(player.Life);
        writer.Write(player.Level);
        writer.Write(player.PosX);
        writer.Write(player.PosY);
        writer.Write(player.Hair);
        writer.Write(player.Dir);
        writer.Write(player.Pose);
        writer.Write(player.Name);
        writer.Finalize(PacketType.Player);
        return writer.AsSpan().ToArray();
    }
}
