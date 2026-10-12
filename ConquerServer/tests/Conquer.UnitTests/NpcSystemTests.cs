// Conquer.UnitTests — Phase 5 NPC System Unit Tests.
// Tests AI state transitions, dormancy, 9-block spatial hashing, generators, and wire framing.

using Conquer.NpcServer;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages.Packets;
using Conquer.Shared.Transport;

namespace Conquer.UnitTests;

public class NpcSystemTests
{
    [Fact]
    public void NpcWireProtocol_Encode_Decode_RoundTrip()
    {
        uint npcId = 400010;
        PacketType packetType = PacketType.Walk;
        byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8];

        var packet = new NpcPacket(npcId, packetType, payload);
        byte[] encoded = NpcWireProtocol.Encode(packet);

        Assert.Equal(NpcWireProtocol.EnvelopeHeaderSize + payload.Length, encoded.Length);
        Assert.Equal(8 + payload.Length, encoded.Length);

        bool decoded = NpcWireProtocol.TryDecode(encoded, out var decodedPacket, out int consumed);

        Assert.True(decoded);
        Assert.Equal(encoded.Length, consumed);
        Assert.Equal(npcId, decodedPacket.NpcId);
        Assert.Equal(packetType, decodedPacket.PacketType);
        Assert.True(payload.AsSpan().SequenceEqual(decodedPacket.Payload.Span));
    }

    [Fact]
    public void NpcWireProtocol_TryDecode_InsufficientBuffer_ReturnsFalse()
    {
        byte[] incomplete = [10, 0, 5, 0]; // Less than 8 bytes
        bool success = NpcWireProtocol.TryDecode(incomplete, out _, out int consumed);

        Assert.False(success);
        Assert.Equal(0, consumed);
    }

    [Fact]
    public void MsgAiNpcInfo_Packet_Serialization_RoundTrip()
    {
        var info = new MsgAiNpcInfo(
            Action: AiNpcAction.CreateNew,
            NpcId: 400100,
            GenId: 101,
            NpcType: 55,
            MapId: 1002,
            PosX: 320,
            PosY: 450,
            OwnerId: 0,
            Data: 1234,
            OwnerType: 0);

        byte[] bytes = MsgAiNpcInfo.Encode(info);
        var decoded = MsgAiNpcInfo.Decode(bytes);

        Assert.Equal(AiNpcAction.CreateNew, decoded.Action);
        Assert.Equal(400100u, decoded.NpcId);
        Assert.Equal(101u, decoded.GenId);
        Assert.Equal((ushort)55, decoded.NpcType);
        Assert.Equal(1002u, decoded.MapId);
        Assert.Equal((ushort)320, decoded.PosX);
        Assert.Equal((ushort)450, decoded.PosY);
        Assert.Equal(1234, decoded.Data);
    }

    [Fact]
    public void NpcGenerator_SpawnBudget_AndCapacity_Adherence()
    {
        // Bound 20x20, grid 2 -> MaxNpc = (20/2)*(20/2) = 100. RestSecs = 10, MaxPerGen = 5
        var gen = new NpcGenerator(
            id: 1,
            mapId: 1002,
            boundX: 100,
            boundY: 100,
            boundCX: 20,
            boundCY: 20,
            npcType: 10,
            grid: 2,
            restSecs: 10,
            maxPerGen: 5);

        Assert.Equal(100, gen.MaxNpc);

        List<MsgAiNpcInfo> spawned = [];
        uint nextId = 400001;

        // Tick with budget = 3 (less than MaxPerGen = 5)
        int count1 = gen.Generator(nowMs: 1000, spawnBudget: 3, () => nextId++, spawned.Add);
        Assert.Equal(3, count1);
        Assert.Equal(3, gen.Amount);

        // Next tick with budget = 10 -> only 2 remaining in this wave
        int count2 = gen.Generator(nowMs: 1000, spawnBudget: 10, () => nextId++, spawned.Add);
        Assert.Equal(2, count2);
        Assert.Equal(5, gen.Amount);

        // Next tick without time rest elapsed -> 0 spawned
        int count3 = gen.Generator(nowMs: 2000, spawnBudget: 5, () => nextId++, spawned.Add);
        Assert.Equal(0, count3);
        Assert.Equal(5, gen.Amount);

        // Advance 10s -> new wave of 5 allowed
        int count4 = gen.Generator(nowMs: 12000, spawnBudget: 5, () => nextId++, spawned.Add);
        Assert.Equal(5, count4);
        Assert.Equal(10, gen.Amount);
    }

    [Fact]
    public void NpcGenerator_BoundsAndCenter_Calculations()
    {
        var gen = new NpcGenerator(
            id: 1,
            mapId: 1002,
            boundX: 100,
            boundY: 200,
            boundCX: 50,
            boundCY: 60,
            npcType: 1);

        var (cx, cy) = gen.GetCenter();
        Assert.Equal(125, cx);
        Assert.Equal(230, cy);

        Assert.True(gen.IsInRegion(125, 230));
        Assert.True(gen.IsInRegion(100, 200));
        Assert.False(gen.IsInRegion(50, 50));

        Assert.False(gen.IsTooFar(125, 230, 20));
        Assert.True(gen.IsTooFar(300, 300, 20));
    }

    [Fact]
    public void NpcDormancy_9Blocks_Detection()
    {
        var map = new NpcWorldMap(1002, 1000, 1000);
        int blockX = 10;
        int blockY = 10;
        int posX = blockX * TimingConstants.CellsPerBlock + 5;
        int posY = blockY * TimingConstants.CellsPerBlock + 5;

        // Initially no players anywhere: block can sleep
        bool canSleep = map.SetDormancy(posX, posY);
        Assert.True(canSleep);
        Assert.True(map.GetBlock(posX, posY).IsDormant);

        // Add user to an adjacent block (blockX + 1, blockY)
        int userPosX = (blockX + 1) * TimingConstants.CellsPerBlock + 5;
        int userPosY = blockY * TimingConstants.CellsPerBlock + 5;
        var user = new NpcUser(1001, 1002, (ushort)userPosX, (ushort)userPosY);

        map.EnterUser(user, _ => { });

        // Now SetDormancy should return false because a player is in the 9-block grid
        map.GetBlock(posX, posY).IsDormant = false;
        bool canSleepWithUser = map.SetDormancy(posX, posY);
        Assert.False(canSleepWithUser);
        Assert.False(map.GetBlock(posX, posY).IsDormant);

        // Remove user: can sleep again
        map.LeaveUser(user);
        bool canSleepAfterLeave = map.SetDormancy(posX, posY);
        Assert.True(canSleepAfterLeave);
        Assert.True(map.GetBlock(posX, posY).IsDormant);
    }

    [Fact]
    public void NpcDormancy_WakeMonster_Trigger()
    {
        var map = new NpcWorldMap(1002, 1000, 1000);
        int posX = 180;
        int posY = 180;

        uint monsterId = 400050;
        map.EnterNpc(monsterId, posX, posY);
        map.SetDormancy(posX, posY);
        Assert.True(map.GetBlock(posX, posY).IsDormant);

        bool awakened = false;
        // Player enters an adjacent block within 9-grid (e.g. 180 + 10 = 190)
        var user = new NpcUser(2001, 1002, 190, 190);
        map.EnterUser(user, id =>
        {
            if (id == monsterId)
                awakened = true;
        });

        Assert.True(awakened);
        Assert.False(map.GetBlock(posX, posY).IsDormant);
    }

    [Fact]
    public void NpcBrain_AI_StateTransitions_Idle_To_Forward_To_Attack()
    {
        var map = new NpcWorldMap(1002, 1000, 1000);
        var typeData = new NpcTypeData
        {
            TypeId = 1,
            ViewRange = 10,
            AttackRange = 1,
            AttackSpeedMs = 1000,
            MoveSpeedMs = 0, // Instant step for test
            AtkUserActive = true,
        };

        var brain = new NpcBrain(400001, typeData, 1002, 100, 100);
        map.EnterNpc(brain.Id, brain.PosX, brain.PosY);

        var user = new NpcUser(1001, 1002, 105, 100); // 5 tiles away
        map.EnterUser(user, _ => { });

        List<NpcPacket> sentPackets = [];

        // 1. Tick at nowMs = 10000 (after think interval) -> spots user, transitions to Forward and takes 1 step
        brain.OnTimer(nowMs: 10000, map, id => id == user.Id ? user : null, sentPackets.Add);

        Assert.Equal(NpcMode.Forward, brain.ActMode);
        Assert.Equal(user.Id, brain.MoveTargetId);
        Assert.True(brain.PosX > 100); // Moved closer (towards 105)
        Assert.Contains(sentPackets, p => p.PacketType == PacketType.Walk);

        // Move user directly adjacent to monster (distance 1)
        user.PosX = (ushort)(brain.PosX + 1);
        user.PosY = brain.PosY;

        // 2. Next tick: monster sees user in AttackRange -> transitions to Attack
        brain.OnTimer(nowMs: 11000, map, id => id == user.Id ? user : null, sentPackets.Add);

        Assert.Equal(NpcMode.Attack, brain.ActMode);
    }

    [Fact]
    public void NpcBrain_AI_PacedAttack_EmitsInteractPacket()
    {
        var map = new NpcWorldMap(1002, 1000, 1000);
        var typeData = new NpcTypeData
        {
            TypeId = 1,
            ViewRange = 10,
            AttackRange = 1,
            AttackSpeedMs = 1000,
        };

        var brain = new NpcBrain(400001, typeData, 1002, 100, 100);
        var user = new NpcUser(1001, 1002, 101, 100);
        map.EnterUser(user, _ => { });

        brain.ChangeMode(NpcMode.Attack);
        brain.ActTargetId = user.Id;

        List<NpcPacket> sentPackets = [];

        // 1. First attack tick at 1000ms
        brain.OnTimer(nowMs: 1000, map, id => id == user.Id ? user : null, sentPackets.Add);

        Assert.Single(sentPackets);
        Assert.Equal(PacketType.Interact, sentPackets[0].PacketType);

        var interact = MsgInteract.Decode(sentPackets[0].Payload.Span);
        Assert.Equal(InteractType.Attack, interact.Type);
        Assert.Equal(brain.Id, interact.SenderId);
        Assert.Equal(user.Id, interact.TargetId);

        // 2. Second tick at 1500ms (500ms < 1000ms attack speed) -> no new packet
        brain.OnTimer(nowMs: 1500, map, id => id == user.Id ? user : null, sentPackets.Add);
        Assert.Single(sentPackets);

        // 3. Third tick at 2100ms (cooldown ready) -> second attack packet emitted
        brain.OnTimer(nowMs: 2100, map, id => id == user.Id ? user : null, sentPackets.Add);
        Assert.Equal(2, sentPackets.Count);
    }

    [Fact]
    public void NpcBrain_AI_Dormancy_Transition_WhenNoPlayers()
    {
        var map = new NpcWorldMap(1002, 1000, 1000);
        var typeData = new NpcTypeData { TypeId = 1 };
        var brain = new NpcBrain(400001, typeData, 1002, 100, 100);

        map.EnterNpc(brain.Id, brain.PosX, brain.PosY);
        Assert.Equal(NpcMode.Idle, brain.ActMode);

        // Tick within dormancy cooldown (< 60s) -> stays Idle
        brain.OnTimer(nowMs: 30000, map, _ => null, _ => { });
        Assert.Equal(NpcMode.Idle, brain.ActMode);

        // Tick after 60s (TimingConstants.DormancySecs) with no players -> enters Dormancy
        brain.OnTimer(nowMs: 65000, map, _ => null, _ => { });
        Assert.Equal(NpcMode.Dormancy, brain.ActMode);
        Assert.True(brain.IsDormant);

        // Awakening
        brain.WakeUp(nowMs: 66000);
        Assert.Equal(NpcMode.Idle, brain.ActMode);
        Assert.False(brain.IsDormant);
    }

    [Fact]
    public void NpcBrain_Pathfinding_DetectPath_8Way()
    {
        var map = new NpcWorldMap(1002, 1000, 1000);
        var brain = new NpcBrain(400001, new NpcTypeData(), 1002, 50, 50);

        // Target East (55, 50)
        bool foundEast = brain.DetectPath(55, 50, map, out Direction dirEast);
        Assert.True(foundEast);
        Assert.Equal(Direction.East, dirEast);

        // Target South (50, 55)
        bool foundSouth = brain.DetectPath(50, 55, map, out Direction dirSouth);
        Assert.True(foundSouth);
        Assert.Equal(Direction.South, dirSouth);

        // Target NorthWest (45, 45)
        bool foundNW = brain.DetectPath(45, 45, map, out Direction dirNW);
        Assert.True(foundNW);
        Assert.Equal(Direction.NorthWest, dirNW);
    }

    [Fact]
    public async Task ChannelNpcTransport_Bidirectional_Delivery()
    {
        var (server, client) = ChannelNpcTransport.CreatePair();

        var packetToServer = new NpcPacket(400001, PacketType.Walk, new byte[] { 1, 2, 3 });
        var packetToClient = new NpcPacket(400002, PacketType.Interact, new byte[] { 4, 5, 6 });

        await client.SendAsync(packetToServer);
        await server.SendAsync(packetToClient);

        // Server receives packet from client
        await using var serverEnumerator = server.ReceiveAsync().GetAsyncEnumerator();
        Assert.True(await serverEnumerator.MoveNextAsync());
        Assert.Equal(packetToServer.NpcId, serverEnumerator.Current.NpcId);
        Assert.Equal(packetToServer.PacketType, serverEnumerator.Current.PacketType);
        Assert.Equal(packetToServer.Payload.ToArray(), serverEnumerator.Current.Payload.ToArray());

        // Client receives packet from server
        await using var clientEnumerator = client.ReceiveAsync().GetAsyncEnumerator();
        Assert.True(await clientEnumerator.MoveNextAsync());
        Assert.Equal(packetToClient.NpcId, clientEnumerator.Current.NpcId);
        Assert.Equal(packetToClient.PacketType, clientEnumerator.Current.PacketType);
        Assert.Equal(packetToClient.Payload.ToArray(), clientEnumerator.Current.Payload.ToArray());

        await server.CloseAsync();
        await client.CloseAsync();
    }
}
