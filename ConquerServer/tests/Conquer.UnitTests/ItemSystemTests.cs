// Conquer.UnitTests — Phase 6: Unit tests for items, equipment, inventory, and shops.
// Ported from original C++ rules:
// - MapGroupKernel/Network/MsgItem.h (_MSG_ITEM = 1009)
// - MapGroupKernel/Network/MsgItemInfo.h (_MSG_ITEMINFO = 1008)
// - MapGroupKernel/UserPackage.h (_MAX_USERITEMSIZE = 40)
// - MapGroupKernel/User_Item.cpp (EquipItem, UnEquipItem, BuyItem, SellItem)
// - MapGroupKernel/Item.cpp (GetSellPrice formula)

using Conquer.Data;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages.Packets;
using Xunit;

namespace Conquer.UnitTests;

public class ItemSystemTests
{
    private static ItemTypeModel CreateTestItemType(
        uint id,
        string name,
        uint price,
        byte reqLevel = 1,
        byte reqProfession = 0,
        byte reqSex = 0,
        ushort minAtk = 0,
        ushort maxAtk = 0,
        ushort defense = 0,
        short life = 0,
        ushort maxDur = 100,
        ushort atkRange = 1,
        ushort atkSpeed = 1000)
    {
        return new ItemTypeModel
        {
            Id = id,
            Name = name,
            ReqLevel = reqLevel,
            ReqProfession = reqProfession,
            ReqSex = reqSex,
            Price = price,
            AttackMin = minAtk,
            AttackMax = maxAtk,
            Defense = defense,
            Life = life,
            Amount = maxDur,
            AmountLimit = maxDur,
            AtkRange = atkRange,
            AtkSpeed = atkSpeed
        };
    }

    [Fact]
    public void MsgItem_EncodeDecode_RoundTrip()
    {
        var original = MsgItem.CreateBuy(npcId: 10050, itemType: 410001);

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            Assert.Equal(MsgItem.PacketSize + WireProtocol.TrailerSize, span.Length);

            var decoded = MsgItem.Decode(span);
            Assert.Equal(original.Id, decoded.Id);
            Assert.Equal(original.Data, decoded.Data);
            Assert.Equal(original.Action, decoded.Action);
            Assert.Equal(original.TargetId, decoded.TargetId);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void MsgItem_SpanEncode_MatchesPacketSize()
    {
        var item = MsgItem.CreateDrop(itemId: 98765, posX: 350, posY: 420);
        Span<byte> buffer = stackalloc byte[MsgItem.PacketSize];

        int written = item.Encode(buffer);
        Assert.Equal(MsgItem.PacketSize, written);

        var decoded = MsgItem.Decode(buffer);
        Assert.Equal((uint)98765, decoded.Id);
        Assert.Equal((ushort)350, decoded.PosX);
        Assert.Equal((ushort)420, decoded.PosY);
        Assert.Equal(ItemAction.Drop, decoded.Action);
    }

    [Fact]
    public void MsgItemInfo_EncodeDecode_RoundTrip()
    {
        var original = MsgItemInfo.CreateAddItem(
            id: 12345,
            itemType: 410009,
            amount: 80,
            amountLimit: 100,
            position: ItemPosition.Backpack,
            ident: 0,
            gem1: 13,
            gem2: 13,
            magic1: 1,
            magic2: 2,
            magic3: 3,
            data: 99);

        var writer = new PacketWriter();
        try
        {
            original.Encode(ref writer);
            var span = writer.AsSpan();

            var decoded = MsgItemInfo.Decode(span);
            Assert.Equal(original.Id, decoded.Id);
            Assert.Equal(original.ItemType, decoded.ItemType);
            Assert.Equal(original.Amount, decoded.Amount);
            Assert.Equal(original.AmountLimit, decoded.AmountLimit);
            Assert.Equal(original.Action, decoded.Action);
            Assert.Equal(original.Position, decoded.Position);
            Assert.Equal(original.Gem1, decoded.Gem1);
            Assert.Equal(original.Gem2, decoded.Gem2);
            Assert.Equal(original.Magic1, decoded.Magic1);
            Assert.Equal(original.Magic2, decoded.Magic2);
            Assert.Equal(original.Magic3, decoded.Magic3);
            Assert.Equal(original.Data, decoded.Data);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void InventoryPackage_CapacityLimit_Exact40Items()
    {
        var package = new InventoryPackage();
        Assert.Equal(40, InventoryPackage.MaxCapacity);
        Assert.False(package.IsFull);

        var type = CreateTestItemType(1001, "TestItem", 50);

        // Fill backpack to capacity (40 items)
        for (uint i = 1; i <= 40; i++)
        {
            var item = new Item(i, type);
            bool added = package.TryAdd(item);
            Assert.True(added);
        }

        Assert.Equal(40, package.Count);
        Assert.True(package.IsFull);

        // 41st item must be rejected
        var overflowItem = new Item(41, type);
        bool overflowAdded = package.TryAdd(overflowItem);
        Assert.False(overflowAdded);
        Assert.Equal(40, package.Count);

        // Remove one item
        bool removed = package.Remove(10, out var popped);
        Assert.True(removed);
        Assert.NotNull(popped);
        Assert.Equal((uint)10, popped.Id);
        Assert.Equal(39, package.Count);
        Assert.False(package.IsFull);

        // Now an item can be added again
        bool addedAgain = package.TryAdd(overflowItem);
        Assert.True(addedAgain);
        Assert.Equal(40, package.Count);
        Assert.True(package.IsFull);
    }

    [Fact]
    public void EquipmentManager_SlotResolution_ResolvesCorrectPositions()
    {
        var helmetType = CreateTestItemType(110001, "Helmet", 100);
        var neckType = CreateTestItemType(120001, "Necklace", 100);
        var armorType = CreateTestItemType(130001, "Armor", 100);
        var bladeType = CreateTestItemType(410001, "Blade", 100); // 1-handed sword/blade sort 4
        var spearType = CreateTestItemType(560001, "Spear", 100); // 2-handed polearm subType 560
        var ringType = CreateTestItemType(150001, "Ring", 100);
        var shoesType = CreateTestItemType(160001, "Shoes", 100);
        var mantleType = CreateTestItemType(200001, "Mantle", 100);

        var helmet = new Item(1, helmetType);
        var neck = new Item(2, neckType);
        var armor = new Item(3, armorType);
        var blade = new Item(4, bladeType);
        var spear = new Item(5, spearType);
        var ring = new Item(6, ringType);
        var shoes = new Item(7, shoesType);
        var mantle = new Item(8, mantleType);

        Assert.Equal(ItemPosition.Helmet, helmet.ResolveEquipPosition());
        Assert.Equal(ItemPosition.Necklace, neck.ResolveEquipPosition());
        Assert.Equal(ItemPosition.Armor, armor.ResolveEquipPosition());
        Assert.Equal(ItemPosition.WeaponR, blade.ResolveEquipPosition(hasWeaponR: false));
        Assert.Equal(ItemPosition.WeaponL, blade.ResolveEquipPosition(hasWeaponR: true));
        Assert.Equal(ItemPosition.WeaponR, spear.ResolveEquipPosition());
        Assert.Equal(ItemPosition.RingR, ring.ResolveEquipPosition());
        Assert.Equal(ItemPosition.Shoes, shoes.ResolveEquipPosition());
        Assert.Equal(ItemPosition.Mantle, mantle.ResolveEquipPosition());
    }

    [Fact]
    public void EquipmentManager_RequirementsValidation_EnforcesLevelAndProfession()
    {
        var player = new Player(1001, "Hero", 1)
        {
            Level = 25,
            Profession = 10, // Trojan/Warrior
            Sex = 1 // Male
        };

        var highLevelArmor = CreateTestItemType(130009, "DragonArmor", 5000, reqLevel: 50);
        var wrongProfArmor = CreateTestItemType(130008, "TaoistRobe", 5000, reqLevel: 20, reqProfession: 100); // Taoist
        var wrongSexArmor = CreateTestItemType(130007, "FemaleDress", 5000, reqLevel: 20, reqSex: 2); // Female
        var validArmor = CreateTestItemType(130006, "TrojanArmor", 5000, reqLevel: 20, reqProfession: 10, reqSex: 1);

        var item1 = new Item(1, highLevelArmor);
        var item2 = new Item(2, wrongProfArmor);
        var item3 = new Item(3, wrongSexArmor);
        var item4 = new Item(4, validArmor);

        Assert.False(EquipmentManager.CanEquip(item1, player, out var r1));
        Assert.Contains("level", r1, StringComparison.OrdinalIgnoreCase);

        Assert.False(EquipmentManager.CanEquip(item2, player, out var r2));
        Assert.Contains("profession", r2, StringComparison.OrdinalIgnoreCase);

        Assert.False(EquipmentManager.CanEquip(item3, player, out var r3));
        Assert.Contains("gender", r3, StringComparison.OrdinalIgnoreCase);

        Assert.True(EquipmentManager.CanEquip(item4, player, out _));
    }

    [Fact]
    public void EquipmentManager_EquipSwapAndUnEquip_AggregatesCombatStats()
    {
        var player = new Player(1001, "Hero", 1)
        {
            Level = 50,
            BaseMinAtk = 10,
            BaseMaxAtk = 20,
            BaseDefense = 5,
            BaseMaxLife = 100
        };

        var weaponType = CreateTestItemType(410001, "Blade", 100, minAtk: 15, maxAtk: 30, atkRange: 2, atkSpeed: 800);
        var armorType = CreateTestItemType(130001, "Armor", 100, defense: 25, life: 50);

        var weapon = new Item(1, weaponType);
        var armor = new Item(2, armorType);

        player.Package.TryAdd(weapon);
        player.Package.TryAdd(armor);

        // Equip weapon
        bool equipWpn = player.Equipment.TryEquip(weapon, ItemPosition.WeaponR, player, player.Package, out var unequippedWpn, out _);
        Assert.True(equipWpn);
        Assert.Null(unequippedWpn);
        Assert.False(player.Package.Contains(weapon.Id));

        // Equip armor
        bool equipArm = player.Equipment.TryEquip(armor, ItemPosition.Armor, player, player.Package, out var unequippedArm, out _);
        Assert.True(equipArm);
        Assert.Null(unequippedArm);
        Assert.False(player.Package.Contains(armor.Id));

        // Recalculate stats
        player.RecalculateStats();

        Assert.Equal(10 + 15, player.MinAtk);
        Assert.Equal(20 + 30, player.MaxAtk);
        Assert.Equal(5 + 25, player.Defense);
        Assert.Equal((ushort)(100 + 50), player.MaxLife);
        Assert.Equal(2, player.AttackRange);
        Assert.Equal(800, player.AttackSpeedMs);

        // Unequip weapon
        bool unequipWpnResult = player.Equipment.TryUnEquip(ItemPosition.WeaponR, player.Package, out var returnedWpn, out _);
        Assert.True(unequipWpnResult);
        Assert.NotNull(returnedWpn);
        Assert.Equal(weapon.Id, returnedWpn.Id);
        Assert.True(player.Package.Contains(weapon.Id));

        player.RecalculateStats();
        Assert.Equal(player.BaseMinAtk, player.MinAtk);
        Assert.Equal(player.BaseMaxAtk, player.MaxAtk);
        Assert.Equal(1, player.AttackRange); // Back to melee default
        Assert.Equal(1000, player.AttackSpeedMs); // Back to default
    }

    [Fact]
    public void Item_GetSellPrice_FaithfullyPortsCppFormula()
    {
        // C++ Formula: ((Price + Price * Quality * 5 / 100) / 3 * Amount) / AmountLimit
        // Arrows always return 0

        // 1. Standard quality 0 item, full durability (300 base price)
        // Price = 300, Quality = 0 -> (300 / 3 * 100) / 100 = 100
        var standardType = CreateTestItemType(410000, "NormalBlade", 300, maxDur: 100);
        var itemStandard = new Item(1, standardType) { Amount = 100, AmountLimit = 100 };
        Assert.Equal((uint)100, itemStandard.GetSellPrice());

        // 2. Half durability (50 / 100) -> 100 * 50 / 100 = 50
        itemStandard.Amount = 50;
        Assert.Equal((uint)50, itemStandard.GetSellPrice());

        // 3. Super quality (Quality 9) -> Price + (300 * 9 * 5 / 100) = 300 + 135 = 435.
        // 435 / 3 = 145.
        var superType = CreateTestItemType(410009, "SuperBlade", 300, maxDur: 100);
        var itemSuper = new Item(2, superType) { Amount = 100, AmountLimit = 100 };
        Assert.Equal((uint)145, itemSuper.GetSellPrice());

        // 4. Arrows (subType 50) -> always 0
        var arrowType = CreateTestItemType(1050000, "Arrows", 500, maxDur: 200);
        var itemArrow = new Item(3, arrowType) { Amount = 200, AmountLimit = 200 };
        Assert.Equal((uint)0, itemArrow.GetSellPrice());
    }

    [Fact]
    public void ShopManager_BuyAndSell_FullTransactionValidation()
    {
        var shopManager = new ShopManager();
        uint shopNpcId = 10050;
        uint itemTypeId = 410001;
        uint itemPrice = 300;

        shopManager.RegisterShop(shopNpcId, [itemTypeId]);
        Assert.True(shopManager.IsSoldByShop(shopNpcId, itemTypeId));
        Assert.False(shopManager.IsSoldByShop(shopNpcId, 999999));

        var typeModel = CreateTestItemType(itemTypeId, "Blade", itemPrice);
        var player = new Player(1001, "Buyer", 1) { Money = 500 };

        uint idCounter = 1000;
        uint NextId() => ++idCounter;

        // 1. Buy item successfully
        bool bought = shopManager.BuyItem(
            shopNpcId,
            itemTypeId,
            player,
            id => id == itemTypeId ? typeModel : null,
            NextId,
            out var boughtItem,
            out var reason);

        Assert.True(bought);
        Assert.NotNull(boughtItem);
        Assert.Equal((uint)200, player.Money); // 500 - 300
        Assert.Equal(1, player.Package.Count);

        // 2. Try buy with insufficient silver
        bool boughtNoMoney = shopManager.BuyItem(
            shopNpcId,
            itemTypeId,
            player,
            id => id == itemTypeId ? typeModel : null,
            NextId,
            out _,
            out var noMoneyReason);

        Assert.False(boughtNoMoney);
        Assert.Contains("money", noMoneyReason, StringComparison.OrdinalIgnoreCase);

        // 3. Sell item back to shop
        // ItemTypeId 410001 has Quality 1 (+5%): (300 + 15) / 3 = 105
        bool sold = ShopManager.SellItem(
            shopNpcId,
            boughtItem.Id,
            player,
            out uint earned,
            out var sellReason);

        Assert.True(sold);
        Assert.Equal((uint)105, earned);
        Assert.Equal((uint)305, player.Money); // 200 + 105
        Assert.Equal(0, player.Package.Count);
    }
}
