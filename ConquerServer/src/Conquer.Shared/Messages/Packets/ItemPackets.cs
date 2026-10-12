// Conquer.Shared — Item and inventory packet models.
// Original: MapGroupKernel/Network/MsgItem.h CMsgItem (_MSG_ITEM = 1009)
//           MapGroupKernel/Network/MsgItemInfo.h CMsgItemInfo (_MSG_ITEMINFO = 1008)
//           MapGroupKernel/ItemData.h ITEMPOSITION_*

using System.Buffers.Binary;

namespace Conquer.Shared.Messages.Packets;

/// <summary>
/// Inventory and equipment slots.
/// <para><b>Original:</b> MapGroupKernel/ItemData.h ITEMPOSITION_*</para>
/// </summary>
public enum ItemPosition : byte
{
    None = 0,

    // Equipment positions (1..11)
    Helmet = 1,
    Necklace = 2,
    Armor = 3,
    WeaponR = 4,
    WeaponL = 5,
    RingR = 6,
    RingL = 7,
    Shoes = 8,
    Mount = 9,
    Sprite = 10,
    Mantle = 11,
    EquipEnd = 12,

    // Package positions
    PackBegin = 50,
    Backpack = 50,
    GhostGemPack = 51,
    EudemonEggPack = 52,
    EudemonPack = 53,
    PackEnd = 54,

    // Storage positions
    Storage = 201,
    Trunk = 202,
    Chest = 203,
    PlayerTask = 204,

    // Special positions
    Ground = 254,
    Invalid = 255
}

/// <summary>
/// Actions for MsgItem.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgItem.h ITEMACT_*</para>
/// </summary>
public enum ItemAction : ushort
{
    None = 0,
    Buy = 1,
    Sell = 2,
    Drop = 3,
    Use = 4,
    Equip = 5,
    UnEquip = 6,
    SplitItem = 7,
    CombineItem = 8,
    QueryMoneySaved = 9,
    SaveMoney = 10,
    DrawMoney = 11,
    DropMoney = 12,
    SpendMoney = 13,
    Repair = 14,
    RepairAll = 15,
    Ident = 16,
    Durability = 17,
    DropEquipment = 18,
    Improve = 19,
    UpLev = 20,
    BoothQuery = 21,
    BoothAdd = 22,
    BoothDel = 23,
    BoothBuy = 24,
    SynchroAmount = 25,
    Fireworks = 26
}

/// <summary>
/// Actions for MsgItemInfo.
/// <para><b>Original:</b> MapGroupKernel/Network/MsgItemInfo.h ITEMINFO_*</para>
/// </summary>
public enum ItemInfoAction : byte
{
    None = 0,
    AddItem = 1,
    Trade = 2,
    Update = 3,
    OtherPlayerEquipment = 4,
    Auction = 5
}

/// <summary>
/// Packet for item operations (buy, sell, use, equip, unequip, drop, repair).
/// <para>
/// <b>Original:</b> MapGroupKernel/Network/MsgItem.h CMsgItem (_MSG_ITEM = 1009).
/// Layout: [len:2][type:2][id:4][dwData:4][usAction:2][pad:2][idTarget:4] = 20 bytes.
/// </para>
/// </summary>
public readonly record struct MsgItem(
    uint Id,
    uint Data,
    ItemAction Action,
    uint TargetId = 0)
{
    public const int PacketSize = 20;

    public ushort PosX => (ushort)(Data & 0xFFFF);
    public ushort PosY => (ushort)(Data >> 16);

    public static MsgItem CreateBuy(uint npcId, uint itemType)
    {
        return new MsgItem(npcId, itemType, ItemAction.Buy);
    }

    public static MsgItem CreateSell(uint npcId, uint itemId)
    {
        return new MsgItem(npcId, itemId, ItemAction.Sell);
    }

    public static MsgItem CreateDrop(uint itemId, ushort posX, ushort posY)
    {
        uint data = (uint)(posX | (posY << 16));
        return new MsgItem(itemId, data, ItemAction.Drop);
    }

    public static MsgItem CreateDropMoney(uint amount, ushort posX, ushort posY)
    {
        uint data = (uint)(posX | (posY << 16));
        return new MsgItem(amount, data, ItemAction.DropMoney);
    }

    public static MsgItem CreateUse(uint itemId, ItemPosition position = ItemPosition.None, uint targetId = 0)
    {
        return new MsgItem(itemId, (uint)position, ItemAction.Use, targetId);
    }

    public static MsgItem CreateEquip(uint itemId, ItemPosition position)
    {
        return new MsgItem(itemId, (uint)position, ItemAction.Equip);
    }

    public static MsgItem CreateUnEquip(uint itemId, ItemPosition position)
    {
        return new MsgItem(itemId, (uint)position, ItemAction.UnEquip);
    }

    public static MsgItem CreateDurability(uint itemId, ushort currentDurability)
    {
        return new MsgItem(itemId, currentDurability, ItemAction.Durability);
    }

    public static MsgItem Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint id = reader.ReadUInt32();
        uint data = reader.ReadUInt32();
        ushort action = reader.ReadUInt16();
        reader.SeekForward(2); // padding to align uint32
        uint targetId = reader.Remaining >= 4 ? reader.ReadUInt32() : 0;

        return new MsgItem(id, data, (ItemAction)action, targetId);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(Id);
        writer.Write(Data);
        writer.Write((ushort)Action);
        writer.Write((ushort)0); // padding
        writer.Write(TargetId);
        writer.Finalize(PacketType.Item);
    }

    public int Encode(Span<byte> destination)
    {
        if (destination.Length < PacketSize)
            throw new ArgumentException("Destination span too small for MsgItem", nameof(destination));

        BinaryPrimitives.WriteUInt16LittleEndian(destination[0..2], PacketSize);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..4], (ushort)PacketType.Item);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..8], Id);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..12], Data);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[12..14], (ushort)Action);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[14..16], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..20], TargetId);
        return PacketSize;
    }
}

/// <summary>
/// Packet informing client of item attributes, durability, and equipment slots.
/// <para>
/// <b>Original:</b> MapGroupKernel/Network/MsgItemInfo.h CMsgItemInfo (_MSG_ITEMINFO = 1008).
/// Layout: [len:2][type:2][id:4][dwType:4][usAmount:2][usAmountLimit:2][ucAction:1][ucIdent:1]
///         [ucPosition:1][ucGem1:1][ucGem2:1][ucMagic1:1][ucMagic2:1][ucMagic3:1][nData:4]
///         [dwWarGhostExp:4][dwGemAtkType:4][dwAvailableTime:4][strCount:1][str...] = 40+ bytes.
/// </para>
/// </summary>
public readonly record struct MsgItemInfo(
    uint Id,
    uint ItemType,
    ushort Amount,
    ushort AmountLimit,
    ItemInfoAction Action,
    byte Ident,
    ItemPosition Position,
    byte Gem1 = 0,
    byte Gem2 = 0,
    byte Magic1 = 0,
    byte Magic2 = 0,
    byte Magic3 = 0,
    int Data = 0,
    uint WarGhostExp = 0,
    uint GemAtkType = 0,
    uint AvailableTime = 0,
    string Name = "")
{
    public const int BaseHeaderSize = 40;

    public static MsgItemInfo CreateAddItem(
        uint id,
        uint itemType,
        ushort amount,
        ushort amountLimit,
        ItemPosition position,
        byte ident = 0,
        byte gem1 = 0,
        byte gem2 = 0,
        byte magic1 = 0,
        byte magic2 = 0,
        byte magic3 = 0,
        int data = 0)
    {
        return new MsgItemInfo(
            Id: id,
            ItemType: itemType,
            Amount: amount,
            AmountLimit: amountLimit,
            Action: ItemInfoAction.AddItem,
            Ident: ident,
            Position: position,
            Gem1: gem1,
            Gem2: gem2,
            Magic1: magic1,
            Magic2: magic2,
            Magic3: magic3,
            Data: data);
    }

    public static MsgItemInfo Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint id = reader.ReadUInt32();
        uint itemType = reader.ReadUInt32();
        ushort amount = reader.ReadUInt16();
        ushort amountLimit = reader.ReadUInt16();
        byte action = reader.ReadUInt8();
        byte ident = reader.ReadUInt8();
        byte position = reader.ReadUInt8();
        byte gem1 = reader.ReadUInt8();
        byte gem2 = reader.ReadUInt8();
        byte magic1 = reader.ReadUInt8();
        byte magic2 = reader.ReadUInt8();
        byte magic3 = reader.ReadUInt8();
        int data = reader.ReadInt32();
        uint warGhostExp = reader.Remaining >= 4 ? reader.ReadUInt32() : 0;
        uint gemAtkType = reader.Remaining >= 4 ? reader.ReadUInt32() : 0;
        uint availableTime = reader.Remaining >= 4 ? reader.ReadUInt32() : 0;

        string name = string.Empty;
        if (reader.Remaining > 0)
        {
            byte strCount = reader.ReadUInt8();
            if (strCount > 0 && reader.Remaining > 0)
            {
                byte strLen = reader.ReadUInt8();
                if (strLen > 0 && reader.Remaining >= strLen)
                {
                    name = reader.ReadString(strLen);
                }
            }
        }

        return new MsgItemInfo(
            Id: id,
            ItemType: itemType,
            Amount: amount,
            AmountLimit: amountLimit,
            Action: (ItemInfoAction)action,
            Ident: ident,
            Position: (ItemPosition)position,
            Gem1: gem1,
            Gem2: gem2,
            Magic1: magic1,
            Magic2: magic2,
            Magic3: magic3,
            Data: data,
            WarGhostExp: warGhostExp,
            GemAtkType: gemAtkType,
            AvailableTime: availableTime,
            Name: name);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(Id);
        writer.Write(ItemType);
        writer.Write(Amount);
        writer.Write(AmountLimit);
        writer.Write((byte)Action);
        writer.Write(Ident);
        writer.Write((byte)Position);
        writer.Write(Gem1);
        writer.Write(Gem2);
        writer.Write(Magic1);
        writer.Write(Magic2);
        writer.Write(Magic3);
        writer.Write(Data);
        writer.Write(WarGhostExp);
        writer.Write(GemAtkType);
        writer.Write(AvailableTime);

        if (!string.IsNullOrEmpty(Name))
        {
            writer.Write(Name);
        }

        writer.Finalize(PacketType.ItemInfo);
    }
}
