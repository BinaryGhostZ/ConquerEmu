// Conquer.IntegrationTests — End-to-end integration tests for Phase 6: Items, equipment, shops, and ground drops.
// Tested under single-owner MapGroup execution:
// - NPC shop buying and selling with real silver transactions
// - Equipping weapons/armor, combat stat recalculation, appearance broadcasting
// - Unequipping items back to backpack
// - Dropping items/money to map and pickup by another player
// - Monster death silver drop and pickup

using Conquer.Data;
using Conquer.Net;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages;
using Conquer.Shared.Messages.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Conquer.IntegrationTests;

public class ItemIntegrationTests
{
    private sealed class MockSocket : IGameSocket
    {
        public List<byte[]> SentPackets { get; } = [];
        public bool Disconnected { get; private set; }
        public int PendingSends => 0;

        public void Send(ReadOnlySpan<byte> packet)
        {
            SentPackets.Add(packet.ToArray());
        }

        public void SendBatch(ReadOnlySpan<byte> wholePackets)
        {
            SentPackets.Add(wholePackets.ToArray());
        }

        public void Disconnect()
        {
            Disconnected = true;
        }
    }

    private static InternalMessage CreateClientMessage(int socketId, PacketType type, byte[] payload)
    {
        byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(rented, 0);

        return InternalMessage.WithPayload(
            InternalMessageId.KernelClientMsg,
            sourceId: socketId,
            secondaryId: (int)type,
            rentedBuffer: rented,
            length: payload.Length);
    }

    private static ItemTypeModel CreateBladeType(uint id = 410001, uint price = 300)
    {
        return new ItemTypeModel
        {
            Id = id,
            Name = "Blade",
            Price = price,
            AttackMin = 15,
            AttackMax = 30,
            AtkRange = 2,
            AtkSpeed = 750,
            Amount = 100,
            AmountLimit = 100
        };
    }

    private static ItemTypeModel CreateArmorType(uint id = 130001, uint price = 500)
    {
        return new ItemTypeModel
        {
            Id = id,
            Name = "Armor",
            Price = price,
            Defense = 20,
            Life = 50,
            Amount = 100,
            AmountLimit = 100
        };
    }

    [Fact]
    public async Task Player_BuyItemFromShop_DeductsMoney_AddsToInventory()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock);
        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        uint shopNpcId = 10050;
        uint bladeTypeId = 410001;
        var bladeType = CreateBladeType(bladeTypeId, price: 300);

        mapGroup.ShopManager.RegisterShop(shopNpcId, [bladeTypeId]);
        mapGroup.ItemTypeLookup = id => id == bladeTypeId ? bladeType : null;

        var playerSocket = new MockSocket();
        var player = new Player(10001, "Buyer", 1)
        {
            Socket = playerSocket,
            SocketId = 101,
            Money = 1000
        };
        mapGroup.AddPlayer(player, 1002, 50, 50);

        mapGroup.Start();
        try
        {
            // Send Buy packet
            var buyMsg = MsgItem.CreateBuy(shopNpcId, bladeTypeId);
            var writer = new PacketWriter();
            buyMsg.Encode(ref writer);
            byte[] bytes = writer.AsSpan().ToArray();
            writer.Dispose();

            mapGroup.Post(CreateClientMessage(player.SocketId, PacketType.Item, bytes));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (player.Money == 1000 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Assertions
            Assert.Equal((uint)700, player.Money); // 1000 - 300
            Assert.Equal(1, player.Package.Count);

            var bought = player.Package.Items.First();
            Assert.Equal(bladeTypeId, bought.Type);
            Assert.Equal(ItemPosition.Backpack, bought.Position);

            // Verify player received MsgItemInfo
            bool receivedItemInfo = playerSocket.SentPackets.Any(p =>
                p.Length >= 4 && WireProtocol.ReadType(p) == PacketType.ItemInfo);
            Assert.True(receivedItemInfo);
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public async Task Player_EquipWeapon_RecalculatesStats_BroadcastsAppearance()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock);
        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var playerSocket = new MockSocket();
        var player = new Player(10001, "Warrior", 1)
        {
            Socket = playerSocket,
            SocketId = 101,
            BaseMinAtk = 10,
            BaseMaxAtk = 20
        };
        mapGroup.AddPlayer(player, 1002, 50, 50);

        var observerSocket = new MockSocket();
        var observer = new Player(10002, "Observer", 2)
        {
            Socket = observerSocket,
            SocketId = 102
        };
        mapGroup.AddPlayer(observer, 1002, 51, 50);

        // Put blade in player's backpack
        var bladeType = CreateBladeType(410001);
        var blade = new Item(777, bladeType);
        player.Package.TryAdd(blade);

        mapGroup.Start();
        try
        {
            observerSocket.SentPackets.Clear();

            // Send Equip packet (Action = Equip or Use)
            var equipMsg = MsgItem.CreateEquip(blade.Id, ItemPosition.WeaponR);
            var writer = new PacketWriter();
            equipMsg.Encode(ref writer);
            byte[] bytes = writer.AsSpan().ToArray();
            writer.Dispose();

            mapGroup.Post(CreateClientMessage(player.SocketId, PacketType.Item, bytes));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (player.Package.Count > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Assertions
            Assert.Equal(0, player.Package.Count);
            var equippedWeapon = player.Equipment.GetEquip(ItemPosition.WeaponR);
            Assert.NotNull(equippedWeapon);
            Assert.Equal((uint)777, equippedWeapon.Id);

            // Combat stats recalculated
            Assert.Equal(10 + 15, player.MinAtk);
            Assert.Equal(20 + 30, player.MaxAtk);
            Assert.Equal(2, player.AttackRange);
            Assert.Equal(750, player.AttackSpeedMs);
            Assert.Equal((uint)410001, player.WeaponRType);

            // Observer received spawn packet with updated weapon appearance
            bool observerReceivedPlayerUpdate = observerSocket.SentPackets.Any(p =>
                p.Length >= 4 && WireProtocol.ReadType(p) == PacketType.Player);
            Assert.True(observerReceivedPlayerUpdate);
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public async Task Player_UnEquipArmor_SwapsBackToBackpack_RevertsStats()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock);
        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var playerSocket = new MockSocket();
        var player = new Player(10001, "Armored", 1)
        {
            Socket = playerSocket,
            SocketId = 101,
            BaseDefense = 5,
            BaseMaxLife = 100
        };
        mapGroup.AddPlayer(player, 1002, 50, 50);

        var armorType = CreateArmorType(130001);
        var armor = new Item(888, armorType);
        player.Package.TryAdd(armor);
        player.Equipment.TryEquip(armor, ItemPosition.Armor, player, player.Package, out _, out _);
        player.RecalculateStats();

        Assert.Equal(5 + 20, player.Defense);
        Assert.Equal((ushort)(100 + 50), player.MaxLife);
        Assert.Equal(0, player.Package.Count);

        mapGroup.Start();
        try
        {
            // Send UnEquip packet
            var unequipMsg = MsgItem.CreateUnEquip(armor.Id, ItemPosition.Armor);
            var writer = new PacketWriter();
            unequipMsg.Encode(ref writer);
            byte[] bytes = writer.AsSpan().ToArray();
            writer.Dispose();

            mapGroup.Post(CreateClientMessage(player.SocketId, PacketType.Item, bytes));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (player.Package.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Assertions
            Assert.Null(player.Equipment.GetEquip(ItemPosition.Armor));
            Assert.Equal(1, player.Package.Count);
            Assert.True(player.Package.Contains(armor.Id));

            // Stats reverted
            Assert.Equal(player.BaseDefense, player.Defense);
            Assert.Equal(player.BaseMaxLife, player.MaxLife);
            Assert.Equal((uint)0, player.ArmorType);
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public async Task Player_SellItemToShop_RemovesFromBackpack_CreditsSilver()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock);
        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        uint shopNpcId = 10050;
        var playerSocket = new MockSocket();
        var player = new Player(10001, "Seller", 1)
        {
            Socket = playerSocket,
            SocketId = 101,
            Money = 100
        };
        mapGroup.AddPlayer(player, 1002, 50, 50);

        // Item with price 300, full dur, quality 1 -> resale price 105
        var bladeType = CreateBladeType(410001, price: 300);
        var blade = new Item(999, bladeType);
        player.Package.TryAdd(blade);

        mapGroup.Start();
        try
        {
            // Send Sell packet: Id = npcId, Data = itemId
            var sellMsg = MsgItem.CreateSell(shopNpcId, blade.Id);
            var writer = new PacketWriter();
            sellMsg.Encode(ref writer);
            byte[] bytes = writer.AsSpan().ToArray();
            writer.Dispose();

            mapGroup.Post(CreateClientMessage(player.SocketId, PacketType.Item, bytes));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (player.Money == 100 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Assertions
            Assert.Equal(0, player.Package.Count);
            Assert.Equal((uint)(100 + 105), player.Money); // Credited resale price
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public async Task Player_DropAndPickUpGroundItem_TransfersBetweenPlayers()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock);
        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var p1Socket = new MockSocket();
        var player1 = new Player(10001, "Dropper", 1) { Socket = p1Socket, SocketId = 101 };
        mapGroup.AddPlayer(player1, 1002, 50, 50);

        var p2Socket = new MockSocket();
        var player2 = new Player(10002, "Picker", 2) { Socket = p2Socket, SocketId = 102 };
        mapGroup.AddPlayer(player2, 1002, 51, 50);

        var bladeType = CreateBladeType(410001);
        var blade = new Item(1111, bladeType);
        player1.Package.TryAdd(blade);

        mapGroup.Start();
        try
        {
            // 1. Player 1 drops blade on ground at (51, 50)
            var dropMsg = MsgItem.CreateDrop(blade.Id, 51, 50);
            var writer = new PacketWriter();
            dropMsg.Encode(ref writer);
            byte[] dropBytes = writer.AsSpan().ToArray();
            writer.Dispose();

            mapGroup.Post(CreateClientMessage(player1.SocketId, PacketType.Item, dropBytes));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (map.GroundItems.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.Equal(0, player1.Package.Count);
            Assert.Single(map.GroundItems);

            var groundItem = map.GroundItems.Values.First();
            Assert.Equal((uint)410001, groundItem.ItemType);
            Assert.NotNull(groundItem.Item);
            Assert.Equal(blade.Id, groundItem.Item.Id);

            // 2. Player 2 picks up ground item
            var pickMsg = new MsgMapItem(groundItem.Id, 0, (ushort)player2.PosX, (ushort)player2.PosY, MapItemAction.Pick);
            var pickWriter = new PacketWriter();
            pickMsg.Encode(ref pickWriter);
            byte[] pickBytes = pickWriter.AsSpan().ToArray();
            pickWriter.Dispose();

            mapGroup.Post(CreateClientMessage(player2.SocketId, PacketType.MapItem, pickBytes));

            deadline = DateTime.UtcNow.AddSeconds(2);
            while (map.GroundItems.Count > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Assertions
            Assert.Empty(map.GroundItems);
            Assert.Equal(1, player2.Package.Count);
            Assert.True(player2.Package.Contains(blade.Id));
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }

    [Fact]
    public async Task Monster_Death_DropsSilver_PlayerPicksUpMoney()
    {
        var clock = TimeProvider.System;
        var meterFactory = new TestMeterFactory();
        var mapGroup = new MapGroupLoop(0, 1, NullLogger<MapGroupLoop>.Instance, meterFactory, clock);
        var map = new GameMap(new MapModel { Id = 1002, Name = "TwinCity" }, new CellMap(100, 100, defaultWalkable: true));
        mapGroup.AddMap(map);

        var playerSocket = new MockSocket();
        var player = new Player(10001, "Hunter", 1)
        {
            Socket = playerSocket,
            SocketId = 101,
            MinAtk = 200,
            MaxAtk = 200,
            Money = 50
        };
        mapGroup.AddPlayer(player, 1002, 50, 50);

        var monster = new Monster(50001, "Pheasant", 1)
        {
            Life = 50,
            MaxLife = 50,
            Defense = 0,
            DropMoney = 250
        };
        mapGroup.AddMonster(monster, 1002, 50, 50);

        mapGroup.Start();
        try
        {
            // Player attacks monster -> lethal damage
            var attackMsg = MsgInteract.CreateAttack(player.Id, monster.Id, 50, 50, 0);
            var writer = new PacketWriter();
            attackMsg.Encode(ref writer);
            byte[] bytes = writer.AsSpan().ToArray();
            writer.Dispose();

            mapGroup.Post(CreateClientMessage(player.SocketId, PacketType.Interact, bytes));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (map.GroundItems.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Monster died and dropped silver
            Assert.Equal(0, mapGroup.MonsterCount);
            Assert.Single(map.GroundItems);

            var groundMoney = map.GroundItems.Values.First();
            Assert.True(groundMoney.IsMoney);
            Assert.Equal((uint)250, groundMoney.Money);

            // Player picks up the money
            var pickMsg = new MsgMapItem(groundMoney.Id, 0, (ushort)player.PosX, (ushort)player.PosY, MapItemAction.Pick);
            var pickWriter = new PacketWriter();
            pickMsg.Encode(ref pickWriter);
            byte[] pickBytes = pickWriter.AsSpan().ToArray();
            pickWriter.Dispose();

            mapGroup.Post(CreateClientMessage(player.SocketId, PacketType.MapItem, pickBytes));

            deadline = DateTime.UtcNow.AddSeconds(2);
            while (map.GroundItems.Count > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Assertions
            Assert.Empty(map.GroundItems);
            Assert.Equal((uint)(50 + 250), player.Money); // 300 silver
        }
        finally
        {
            await mapGroup.StopAsync();
        }
    }
}
