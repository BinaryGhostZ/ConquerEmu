// Conquer.Runtime — Item representation, classification, and sell calculations.
// Original: MapGroupKernel/Item.h and Item.cpp

using Conquer.Data;
using Conquer.Shared.Messages.Packets;

namespace Conquer.Runtime;

/// <summary>
/// In-memory runtime item entity.
/// Original: MapGroupKernel/Item.h CItem.
/// </summary>
public sealed class Item
{
    public uint Id { get; set; }
    public uint Type { get; set; }
    public ItemTypeModel TypeData { get; set; }
    public uint OwnerId { get; set; }
    public uint PlayerId { get; set; }
    public ushort Amount { get; set; }
    public ushort AmountLimit { get; set; }
    public byte Ident { get; set; }
    public ItemPosition Position { get; set; }
    public byte Gem1 { get; set; }
    public byte Gem2 { get; set; }
    public byte Magic1 { get; set; }
    public byte Magic2 { get; set; }
    public byte Magic3 { get; set; }
    public int Data { get; set; }

    public Item(uint id, ItemTypeModel typeData, uint ownerId = 0, ItemPosition position = ItemPosition.Backpack)
    {
        Id = id;
        Type = typeData.Id;
        TypeData = typeData;
        OwnerId = ownerId;
        Amount = typeData.Amount;
        AmountLimit = typeData.AmountLimit;
        Ident = typeData.Ident;
        Position = position;
        Gem1 = typeData.Gem1;
        Gem2 = typeData.Gem2;
        Magic1 = typeData.Magic1;
        Magic2 = typeData.Magic2;
        Magic3 = typeData.Magic3;
    }

    public Item(ItemModel model, ItemTypeModel typeData)
    {
        Id = model.Id;
        Type = model.Type;
        TypeData = typeData;
        OwnerId = model.OwnerId;
        PlayerId = model.PlayerId;
        Amount = model.Amount;
        AmountLimit = model.AmountLimit;
        Ident = model.Ident;
        Position = (ItemPosition)model.Position;
        Gem1 = model.Gem1;
        Gem2 = model.Gem2;
        Magic1 = model.Magic1;
        Magic2 = model.Magic2;
        Magic3 = model.Magic3;
        Data = model.Data;
    }

    /// <summary>
    /// Item sort prefix: (idType % 10000000) / 100000.
    /// Original: Item.h line 359 CItem::GetItemSort.
    /// </summary>
    public int GetItemSort() => (int)((Type % 10000000) / 100000);

    /// <summary>
    /// Item sub type: (idType % 1000000) / 1000.
    /// </summary>
    public int GetSubType() => (int)((Type % 1000000) / 1000);

    /// <summary>
    /// Item quality: last digit (idType % 10).
    /// </summary>
    public int GetQuality() => (int)(Type % 10);

    public bool IsArrowSort() => GetSubType() == 50 || Type == 170001 || Type == 171001;

    public bool IsWeapon1()
    {
        int sort = GetItemSort();
        return sort == 4 || sort == 5;
    }

    public bool IsWeapon2()
    {
        int subType = GetSubType();
        // Two-handed weapons: bows (subType 500), polearms (510..580)
        return subType >= 500 && subType < 600;
    }

    public bool IsWeapon() => IsWeapon1() || IsWeapon2();

    public bool IsBow() => GetSubType() == 500;

    public bool IsShield() => Type / 1000 == 900;

    public bool IsHelmet() => Type / 10000 == 11;

    public bool IsNecklace() => Type / 10000 == 12;

    public bool IsArmor() => Type / 10000 == 13;

    public bool IsRing() => Type / 10000 == 15;

    public bool IsShoes() => Type / 10000 == 16;

    public bool IsMantle() => Type / 10000 == 20;

    public bool IsMedicine() => TypeData.Life > 0 || TypeData.Mana > 0;

    public bool IsEquipment() =>
        !IsArrowSort() && (IsHelmet() || IsNecklace() || IsArmor() || IsWeapon() || IsShield() || IsRing() || IsShoes() || IsMantle());

    /// <summary>
    /// Determines the default target equipment slot for this item.
    /// Original: User_Item.cpp lines 705-755 CUser::EquipItem.
    /// </summary>
    public ItemPosition ResolveEquipPosition(bool hasWeaponR = false)
    {
        if (IsHelmet()) return ItemPosition.Helmet;
        if (IsNecklace()) return ItemPosition.Necklace;
        if (IsArmor()) return ItemPosition.Armor;
        if (IsArrowSort() || IsShield()) return ItemPosition.WeaponL;
        if (IsWeapon2()) return ItemPosition.WeaponR;
        if (IsWeapon1()) return hasWeaponR ? ItemPosition.WeaponL : ItemPosition.WeaponR;
        if (IsRing()) return ItemPosition.RingR;
        if (IsShoes()) return ItemPosition.Shoes;
        if (IsMantle()) return ItemPosition.Mantle;

        return ItemPosition.None;
    }

    /// <summary>
    /// Calculates sale price to NPC shops.
    /// Original: MapGroupKernel/Item.cpp line 261 CItem::GetSellPrice.
    /// </summary>
    public uint GetSellPrice()
    {
        if (TypeData.AmountLimit == 0 || AmountLimit == 0)
            return 0;

        if (IsArrowSort())
            return 0;

        int nAmount = Math.Min((int)Amount, (int)AmountLimit);
        long dwPrice = TypeData.Price;

        // Quality bonus: dwPrice + (dwPrice * quality * 5 / 100)
        dwPrice += (dwPrice * GetQuality() * 5) / 100;

        // Base resale is 1/3, scaled by durability ratio: (dwPrice / 3) * nAmount / maxDurability
        long sellPrice = ((dwPrice / 3) * nAmount) / TypeData.AmountLimit;
        return (uint)Math.Max(0, sellPrice);
    }

    public ItemModel ToModel()
    {
        return new ItemModel
        {
            Id = Id,
            Type = Type,
            OwnerId = OwnerId,
            PlayerId = PlayerId,
            Amount = Amount,
            AmountLimit = AmountLimit,
            Ident = Ident,
            Position = (byte)Position,
            Gem1 = Gem1,
            Gem2 = Gem2,
            Magic1 = Magic1,
            Magic2 = Magic2,
            Magic3 = Magic3,
            Data = Data
        };
    }

    public MsgItemInfo ToMsgItemInfo(ItemInfoAction action = ItemInfoAction.AddItem)
    {
        return new MsgItemInfo(
            Id: Id,
            ItemType: Type,
            Amount: Amount,
            AmountLimit: AmountLimit,
            Action: action,
            Ident: Ident,
            Position: Position,
            Gem1: Gem1,
            Gem2: Gem2,
            Magic1: Magic1,
            Magic2: Magic2,
            Magic3: Magic3,
            Data: Data,
            Name: TypeData.Name);
    }
}
