// Conquer.Runtime — MapGroupLoop: the core MapGroup kernel loop.
// Original: MsgServer/MapGroupThread.cpp + MapGroupKernel/MapGroupKernel.cpp + MapGroupKernel/MapGroup.cpp
// OPUS §3.6: drain message → ProcessMsg → OnTimer every 500ms → wait up to 400ms.
// OPUS §4.1, §4.2: Single owner: only the owning loop touches game state. No locks.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Conquer.Data;
using Conquer.Shared;
using Conquer.Shared.Messages;
using Conquer.Shared.Messages.Packets;
using Conquer.Shared.Transport;
using Microsoft.Extensions.Logging;

namespace Conquer.Runtime;

/// <summary>
/// A single MapGroup loop. Owns a set of maps and all players/monsters/items in them.
/// <para>
/// <b>Original:</b> MapGroupKernel/MapGroupKernel.cpp CMapGroupKernel::ProcessMsg / OnTimer.
/// </para>
/// <para>
/// <b>Dynamic object IDs (OPUS §3.6):</b> Per-group counters to avoid collisions:
/// first id = (X_FIRST - 1) + groupId, next = previous + numberOfGroups.
/// </para>
/// </summary>
public class MapGroupLoop : GameLoop
{
    private readonly int _groupIndex;
    private readonly int _totalGroups;

    // Per-group dynamic ID counters (OPUS §3.6)
    private uint _nextMonsterId;
    private uint _nextPetId;
    private uint _nextCallPetId;
    private uint _nextTrapId;
    private uint _nextDynaNpcId;

    // Maps and Entities owned by this MapGroup (accessed ONLY by this thread)
    private readonly Dictionary<uint, GameMap> _maps = [];
    private readonly Dictionary<int, Player> _playersBySocket = [];
    private readonly Dictionary<uint, Player> _playersById = [];
    private readonly Dictionary<uint, Monster> _monstersById = [];

    /// <summary>Zero-based index of this MapGroup.</summary>
    public int GroupIndex => _groupIndex;

    /// <summary>Number of maps owned by this group.</summary>
    public int MapCount => _maps.Count;

    /// <summary>Number of players currently in this group.</summary>
    public int PlayerCount => _playersById.Count;

    /// <summary>Number of monsters currently in this group.</summary>
    public int MonsterCount => _monstersById.Count;

    /// <summary>Optional transport to NPC Server for AI decisions.</summary>
    public INpcTransport? NpcTransport { get; set; }

    /// <summary>NPC Shop manager.</summary>
    public ShopManager ShopManager { get; } = new();

    /// <summary>Item type data lookup delegate.</summary>
    public Func<uint, Conquer.Data.ItemTypeModel?>? ItemTypeLookup { get; set; }

    private uint _nextItemId = 100000;
    private readonly IMagicTypeRepository? _magicTypes;

    /// <summary>Allocate the next dynamic item ID.</summary>
    public uint NextItemId() => ++_nextItemId;

    protected override int TimerIntervalMs => TimingConstants.MapGroupOnTimerMs;

    public MapGroupLoop(
        int groupIndex,
        int totalGroups,
        ILogger<MapGroupLoop> logger,
        IMeterFactory meterFactory,
        TimeProvider clock,
        IMagicTypeRepository? magicTypes = null)
        : base($"MapGroup-{groupIndex}", Conquer.Shared.PortId.MapGroup(groupIndex), logger, meterFactory, clock)
    {
        _groupIndex = groupIndex;
        _totalGroups = totalGroups;
        _magicTypes = magicTypes;

        // Initialize per-group id counters: first id = (RANGE_FIRST - 1) + groupId (1-based)
        // next = previous + totalGroups
        int gid = groupIndex + 1; // 1-based group id
        _nextMonsterId = (uint)(IdRanges.MonsterIdFirst - 1 + gid);
        _nextPetId = (uint)(IdRanges.PetIdFirst - 1 + gid);
        _nextCallPetId = (uint)(IdRanges.CallPetIdFirst - 1 + gid);
        _nextTrapId = (uint)(IdRanges.MagicTrapIdFirst - 1 + gid);
        _nextDynaNpcId = (uint)(IdRanges.DynaNpcIdFirst - 1 + gid);
    }

    /// <summary>Add a map to be managed exclusively by this MapGroup.</summary>
    public void AddMap(GameMap map)
    {
        _maps[map.Id] = map;
        Logger.LogInformation("[{Name}] Registered map {MapId} ({MapName})", Name, map.Id, map.Name);
    }

    /// <summary>Get a map managed by this group.</summary>
    public GameMap? GetMap(uint mapId) => _maps.GetValueOrDefault(mapId);

    /// <summary>Add a player to this MapGroup and place them onto the designated map.</summary>
    public bool AddPlayer(Player player, uint mapId, int x, int y)
    {
        if (!_maps.TryGetValue(mapId, out var map))
        {
            Logger.LogWarning("[{Name}] Player {PlayerId} attempted to enter unowned map {MapId}", Name, player.Id, mapId);
            return false;
        }

        if (!map.Enter(player, x, y))
        {
            Logger.LogWarning("[{Name}] Player {PlayerId} failed to enter map {MapId} at ({X}, {Y})", Name, player.Id, mapId, x, y);
            return false;
        }

        _playersById[player.Id] = player;
        if (player.SocketId != 0)
        {
            _playersBySocket[player.SocketId] = player;
        }

        var now = Clock.GetUtcNow().UtcDateTime;
        player.LastClientHeartbeat = now;
        player.LastServerHeartbeat = now;

        Logger.LogInformation("[{Name}] Player {PlayerName} ({PlayerId}) entered map {MapId} at ({X}, {Y})",
            Name, player.Name, player.Id, mapId, x, y);

        if (NpcTransport != null)
        {
            var spawnMsg = new MsgPlayer(
                EntityId: player.Id,
                LookFace: player.LookFace,
                Status: 0,
                SynIdRank: 0,
                ArmorType: 0,
                WeaponRType: 0,
                MaxLife: (ushort)player.MaxLife,
                Life: (ushort)player.Life,
                Level: (ushort)player.Level,
                PosX: (ushort)player.PosX,
                PosY: (ushort)player.PosY,
                Hair: 0,
                Dir: (byte)player.Direction,
                Pose: 0,
                Name: player.Name);
            byte[] spawnBytes = MsgPlayer.Encode(spawnMsg);
            _ = NpcTransport.SendAsync(new NpcPacket(mapId, PacketType.Player, spawnBytes)).AsTask();
        }

        return true;
    }

    /// <summary>Remove a player from this MapGroup and leave their map.</summary>
    public bool RemovePlayer(uint playerId)
    {
        if (!_playersById.Remove(playerId, out var player))
            return false;

        if (player.SocketId != 0)
        {
            _playersBySocket.Remove(player.SocketId);
        }

        if (_maps.TryGetValue(player.MapId, out var map))
        {
            map.Leave(player);
        }

        Logger.LogInformation("[{Name}] Player {PlayerName} ({PlayerId}) left map {MapId}",
            Name, player.Name, player.Id, player.MapId);

        if (NpcTransport != null)
        {
            var delAction = new MsgAction(0, player.Id, (ushort)player.PosX, (ushort)player.PosY, 0, ActionType.DelRole, 0);
            byte[] delBytes = MsgAction.Encode(delAction);
            _ = NpcTransport.SendAsync(new NpcPacket(player.MapId, PacketType.Action, delBytes)).AsTask();
        }

        return true;
    }

    /// <summary>Add a monster to this MapGroup and place it on the designated map.</summary>
    public bool AddMonster(Monster monster, uint mapId, int x, int y)
    {
        if (!_maps.TryGetValue(mapId, out var map))
        {
            Logger.LogWarning("[{Name}] Monster {MonsterId} attempted to enter unowned map {MapId}", Name, monster.Id, mapId);
            return false;
        }

        if (!map.Enter(monster, x, y))
        {
            Logger.LogWarning("[{Name}] Monster {MonsterId} failed to enter map {MapId} at ({X}, {Y})", Name, monster.Id, mapId, x, y);
            return false;
        }

        _monstersById[monster.Id] = monster;
        Logger.LogInformation("[{Name}] Monster {MonsterName} ({MonsterId}) entered map {MapId} at ({X}, {Y})",
            Name, monster.Name, monster.Id, mapId, x, y);
        return true;
    }

    /// <summary>Remove a monster from this MapGroup and leave its map.</summary>
    public bool RemoveMonster(uint monsterId)
    {
        if (!_monstersById.Remove(monsterId, out var monster))
            return false;

        if (_maps.TryGetValue(monster.MapId, out var map))
        {
            map.Leave(monster);
        }

        Logger.LogInformation("[{Name}] Monster {MonsterName} ({MonsterId}) left map {MapId}",
            Name, monster.Name, monster.Id, monster.MapId);
        return true;
    }

    /// <summary>Get a monster in this group by its entity ID.</summary>
    public Monster? GetMonster(uint monsterId) => _monstersById.GetValueOrDefault(monsterId);

    /// <summary>Get any entity (player or monster) by its ID.</summary>
    public IWorldEntity? GetEntity(uint entityId)
    {
        if (_playersById.TryGetValue(entityId, out var player)) return player;
        if (_monstersById.TryGetValue(entityId, out var monster)) return monster;
        return null;
    }

    /// <summary>Allocate the next monster ID for this group (no collision across groups).</summary>
    public uint NextMonsterId()
    {
        uint id = _nextMonsterId;
        _nextMonsterId += (uint)_totalGroups;
        if (_nextMonsterId > IdRanges.MonsterIdLast)
            throw new InvalidOperationException($"MapGroup {_groupIndex}: Monster ID range exhausted");
        return id;
    }

    /// <summary>Allocate the next pet ID for this group.</summary>
    public uint NextPetId()
    {
        uint id = _nextPetId;
        _nextPetId += (uint)_totalGroups;
        if (_nextPetId > IdRanges.PetIdLast)
            throw new InvalidOperationException($"MapGroup {_groupIndex}: Pet ID range exhausted");
        return id;
    }

    /// <summary>Allocate the next call pet ID for this group.</summary>
    public uint NextCallPetId()
    {
        uint id = _nextCallPetId;
        _nextCallPetId += (uint)_totalGroups;
        if (_nextCallPetId > IdRanges.CallPetIdLast)
            throw new InvalidOperationException($"MapGroup {_groupIndex}: CallPet ID range exhausted");
        return id;
    }

    /// <summary>Allocate the next magic trap ID for this group.</summary>
    public uint NextTrapId()
    {
        uint id = _nextTrapId;
        _nextTrapId += (uint)_totalGroups;
        if (_nextTrapId > IdRanges.MagicTrapIdLast)
            throw new InvalidOperationException($"MapGroup {_groupIndex}: Trap ID range exhausted");
        return id;
    }

    /// <summary>Allocate the next dynamic NPC ID for this group.</summary>
    public uint NextDynaNpcId()
    {
        uint id = _nextDynaNpcId;
        _nextDynaNpcId += (uint)_totalGroups;
        if (_nextDynaNpcId > IdRanges.DynaNpcIdLast)
            throw new InvalidOperationException($"MapGroup {_groupIndex}: DynaNpc ID range exhausted");
        return id;
    }

    protected override void ProcessMessage(in InternalMessage message)
    {
        switch (message.Id)
        {
            case InternalMessageId.KernelClientMsg:
                HandleClientMessage(message);
                break;

            case InternalMessageId.KernelCloseKernel:
                HandleClientDisconnect(message.SourceId);
                break;

            case InternalMessageId.MapGroupChangeMapGroup:
                HandleChangeMapGroup(message);
                break;

            case InternalMessageId.MapGroupBroadcastMsg:
                HandleBroadcastMsg(message);
                break;

            case InternalMessageId.MapGroupTransmitMsg:
                HandleTransmitMsg(message);
                break;

            case InternalMessageId.MapGroupSocketUserInfo:
                // Incoming player data from a group transfer
                break;

            case InternalMessageId.KernelNpcMsg:
                if (message.RentedBuffer != null && NpcWireProtocol.TryDecode(message.PayloadSpan, out var npcPacket, out _))
                {
                    ReceiveNpcPacket(npcPacket);
                }
                break;

            default:
                Logger.LogDebug("[{Name}] Unhandled message {MsgId}", Name, message.Id);
                break;
        }
    }

    /// <summary>
    /// Processes an NPC action packet commanded by NpcServer.
    /// Original: MapGroupKernel.cpp ProcessNpcMsg.
    /// </summary>
    public void ReceiveNpcPacket(NpcPacket packet)
    {
        switch (packet.PacketType)
        {
            case PacketType.Walk:
                HandleNpcWalk(packet);
                break;

            case PacketType.Interact:
                HandleNpcInteract(packet);
                break;

            case PacketType.AiNpcInfo:
                HandleNpcSpawn(packet);
                break;
        }
    }

    private void HandleNpcWalk(NpcPacket packet)
    {
        var walk = MsgWalk.Decode(packet.Payload.Span);
        var monster = GetMonster(walk.EntityId);
        if (monster == null || !monster.IsAlive)
            return;

        if (!_maps.TryGetValue(monster.MapId, out var map))
            return;

        int dir = (int)walk.Direction % 8;
        int newX = monster.PosX + MapCoordinates.DeltaX[dir];
        int newY = monster.PosY + MapCoordinates.DeltaY[dir];

        if (MapCoordinates.ChebyshevDistance(monster.PosX, monster.PosY, newX, newY) != 1)
            return;

        if (!map.Grid.IsWalkable(newX, newY))
            return;

        monster.Direction = walk.Direction;
        var writer = new PacketWriter();
        try
        {
            var broadcastWalk = new MsgWalk(monster.Id, monster.Direction, walk.Mode);
            broadcastWalk.Encode(ref writer);
            map.Move(monster, newX, newY, writer.AsSpan());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private void HandleNpcInteract(NpcPacket packet)
    {
        var interact = MsgInteract.Decode(packet.Payload.Span);
        if (interact.Type == InteractType.Attack)
        {
            var attacker = GetMonster(interact.SenderId);
            if (attacker == null || !attacker.IsAlive)
                return;

            var target = GetEntity(interact.TargetId);
            if (target == null || !target.IsAlive || target.MapId != attacker.MapId)
                return;

            int dist = MapCoordinates.ChebyshevDistance(attacker.PosX, attacker.PosY, target.PosX, target.PosY);
            if (dist > attacker.AttackRange)
                return;

            ExecuteAttack(attacker, target);
        }
    }

    private void HandleNpcSpawn(NpcPacket packet)
    {
        var info = MsgAiNpcInfo.Decode(packet.Payload.Span);
        if (info.Action == AiNpcAction.CreateNew)
        {
            if (!_maps.TryGetValue(info.MapId, out var map))
                return;

            var monster = new Monster(info.NpcId, $"Monster_{info.NpcType}", info.NpcType)
            {
                Life = 100,
                MaxLife = 100,
                MinAtk = 10,
                MaxAtk = 20,
                Defense = 5,
                MapId = info.MapId,
                PosX = info.PosX,
                PosY = info.PosY,
                DropMoney = 50,
            };

            AddMonster(monster, info.MapId, info.PosX, info.PosY);
        }
    }

    private void HandleClientMessage(in InternalMessage message)
    {
        int socketId = message.SourceId;
        if (!_playersBySocket.TryGetValue(socketId, out var player))
        {
            Logger.LogWarning("[{Name}] Received client message from unknown socket {SocketId}", Name, socketId);
            return;
        }

        var packetType = (PacketType)message.SecondaryId;
        var payload = message.PayloadSpan;

        switch (packetType)
        {
            case PacketType.Walk:
                HandleWalk(player, payload);
                break;

            case PacketType.Action:
                HandleAction(player, payload);
                break;

            case PacketType.Tick:
                HandleTick(player, payload);
                break;

            case PacketType.Interact:
                HandleInteract(player, payload);
                break;

            case PacketType.Item:
                HandleItem(player, payload);
                break;

            case PacketType.MapItem:
                HandleMapItem(player, payload);
                break;

            default:
                Logger.LogDebug("[{Name}] Unhandled packet {PacketType} from player {PlayerId}",
                    Name, packetType, player.Id);
                break;
        }
    }

    private void HandleWalk(Player player, ReadOnlySpan<byte> payload)
    {
        var walk = MsgWalk.Decode(payload);
        int dir = (int)walk.Direction % 8;

        int newX = player.PosX + MapCoordinates.DeltaX[dir];
        int newY = player.PosY + MapCoordinates.DeltaY[dir];

        // Original: MsgWalk.cpp step validation
        if (MapCoordinates.ChebyshevDistance(player.PosX, player.PosY, newX, newY) != 1)
        {
            Logger.LogWarning("[{Name}] Player {PlayerId} attempted invalid step delta from ({X}, {Y}) to ({NewX}, {NewY})",
                Name, player.Id, player.PosX, player.PosY, newX, newY);
            return;
        }

        if (!_maps.TryGetValue(player.MapId, out var map))
            return;

        player.Direction = (Direction)dir;

        var writer = new PacketWriter();
        try
        {
            var broadcastWalk = new MsgWalk(player.Id, player.Direction, walk.Mode);
            broadcastWalk.Encode(ref writer);
            map.Move(player, newX, newY, writer.AsSpan());
        }
        finally
        {
            writer.Dispose();
        }

        _ = NpcTransport?.SendAsync(new NpcPacket(player.MapId, PacketType.Walk, payload.ToArray())).AsTask();
    }

    private void HandleAction(Player player, ReadOnlySpan<byte> payload)
    {
        var action = MsgAction.Decode(payload);
        if (!_maps.TryGetValue(player.MapId, out var map))
            return;

        switch (action.Action)
        {
            case ActionType.Jump:
                // Original: MsgAction.cpp line 607:
                // if (pRole->GetDistance(targetX, targetY) >= CELLS_PER_BLOCK) reject/sync
                int jumpDist = MapCoordinates.ChebyshevDistance(player.PosX, player.PosY, action.TargetPosX, action.TargetPosY);
                if (jumpDist >= TimingConstants.CellsPerBlock)
                {
                    Logger.LogWarning("[{Name}] Player {PlayerId} attempted jump too far ({Dist} >= {Max})",
                        Name, player.Id, jumpDist, TimingConstants.CellsPerBlock);
                    return;
                }

                if (map.Grid.IsWalkable(action.TargetPosX, action.TargetPosY))
                {
                    var writer = new PacketWriter();
                    try
                    {
                        var jumpBroadcast = MsgAction.CreateJump(
                            player.Id,
                            (ushort)player.PosX,
                            (ushort)player.PosY,
                            action.TargetPosX,
                            action.TargetPosY,
                            action.Dir,
                            action.Timestamp);
                        jumpBroadcast.Encode(ref writer);
                        map.Move(player, action.TargetPosX, action.TargetPosY, writer.AsSpan());
                    }
                    finally
                    {
                        writer.Dispose();
                    }

                    _ = NpcTransport?.SendAsync(new NpcPacket(player.MapId, PacketType.Action, payload.ToArray())).AsTask();
                }
                break;

            case ActionType.ChangeDir:
                player.Direction = (Direction)(action.Dir % 8);
                var dirWriter = new PacketWriter();
                try
                {
                    var dirBroadcast = new MsgAction(
                        action.Timestamp,
                        player.Id,
                        (ushort)player.PosX,
                        (ushort)player.PosY,
                        action.Dir,
                        ActionType.ChangeDir,
                        action.Data);
                    dirBroadcast.Encode(ref dirWriter);
                    map.Broadcast(player.PosX, player.PosY, dirWriter.AsSpan(), excludeEntityId: player.Id);
                }
                finally
                {
                    dirWriter.Dispose();
                }
                break;

            default:
                break;
        }
    }

    private void HandleTick(Player player, ReadOnlySpan<byte> payload)
    {
        var tick = MsgTick.Decode(payload);
        player.LastClientHeartbeat = Clock.GetUtcNow().UtcDateTime;
    }

    private void HandleClientDisconnect(int socketId)
    {
        if (_playersBySocket.TryGetValue(socketId, out var player))
        {
            RemovePlayer(player.Id);
        }
    }

    private void HandleChangeMapGroup(in InternalMessage message)
    {
        // Transfer player between MapGroups (OPUS §4.2 rule 3)
        uint playerId = (uint)message.SourceId;
        if (_playersById.TryGetValue(playerId, out var player))
        {
            RemovePlayer(playerId);
            // Dest group will receive PlayerArrived
        }
    }

    private void HandleBroadcastMsg(in InternalMessage message)
    {
        // Broadcast packet to all players in all maps of this group
        if (message.RentedBuffer != null)
        {
            foreach (var player in _playersById.Values)
            {
                player.Send(message.PayloadSpan);
            }
        }
    }

    private void HandleTransmitMsg(in InternalMessage message)
    {
        uint targetId = (uint)message.SourceId;
        if (_playersById.TryGetValue(targetId, out var player) && message.RentedBuffer != null)
        {
            player.Send(message.PayloadSpan);
        }
    }

    private void HandleInteract(Player player, ReadOnlySpan<byte> payload)
    {
        var interact = MsgInteract.Decode(payload);

        switch (interact.Type)
        {
            case InteractType.Attack:
            case InteractType.Shoot:
                ProcessAttackRequest(player, interact);
                break;

            case InteractType.MagicAttack:
                ProcessMagicAttackRequest(player, interact);
                break;

            case InteractType.AbortMagic:
                player.TargetId = 0;
                break;

            default:
                Logger.LogDebug("[{Name}] Unhandled interact type {Type} from {PlayerId}",
                    Name, interact.Type, player.Id);
                break;
        }
    }

    private void ProcessAttackRequest(Player player, in MsgInteract interact)
    {
        // 1. Alive check (MsgInteract.cpp:110)
        if (!player.IsAlive)
            return;

        // 2. Position synchronization anti-cheat check (OPUS §3.6, MsgInteract.cpp:136)
        if (Math.Abs(player.PosX - interact.PosX) > 1 || Math.Abs(player.PosY - interact.PosY) > 1)
        {
            Logger.LogWarning("[{Name}] Position sync mismatch for player {PlayerId}: server=({SX},{SY}), client=({CX},{CY})",
                Name, player.Id, player.PosX, player.PosY, interact.PosX, interact.PosY);
            return;
        }

        // 3. Resolve target entity
        var target = GetEntity(interact.TargetId);
        if (target == null || !target.IsAlive || target.MapId != player.MapId)
        {
            player.TargetId = 0;
            return;
        }

        // 4. Set auto-attack target (MsgInteract.cpp:163 SetAttackTarget)
        player.TargetId = target.Id;

        // 5. Crime check on attack intent against innocent player (User.cpp:2033 CheckCrime)
        if (target is Player targetPlayer && !targetPlayer.IsEvil)
        {
            player.CrimeExpireTime = Clock.GetUtcNow().UtcDateTime.AddSeconds(CombatConstants.CrimeKeepSecs);
        }

        // 6. If attack cooldown is ready, attack immediately; otherwise auto-attack will execute on timer
        var now = Clock.GetUtcNow().UtcDateTime;
        if (now - player.LastAttackTime >= TimeSpan.FromMilliseconds(player.AttackSpeedMs))
        {
            ExecuteAttack(player, target);
        }
    }

    private void ProcessMagicAttackRequest(Player player, in MsgInteract interact)
    {
        if (!player.IsAlive)
            return;

        ushort magicType = interact.MagicType;
        ushort level = interact.MagicLevel;

        var spell = player.Magics.GetSpell(magicType);
        var typeData = spell?.TypeData ?? _magicTypes?.Get(magicType, (byte)level);
        if (typeData == null)
        {
            Logger.LogDebug("[{Name}] Player {PlayerId} attempted unknown spell {MagicType}", Name, player.Id, magicType);
            return;
        }

        var now = Clock.GetUtcNow().UtcDateTime;

        // 1. Cooldown check
        if (player.Magics.IsCoolingDown(magicType, now))
        {
            return;
        }

        // 2. MP check
        if (player.Mana < typeData.UseMp)
        {
            return;
        }

        // 3. XP check
        if (typeData.UseXp != 0 && player.Magics.Xp < MagicManager.MaxXp)
        {
            return;
        }

        // Deduct MP & XP
        if (typeData.UseMp > 0)
        {
            player.Mana = (ushort)(player.Mana - typeData.UseMp);
            SendManaUpdate(player);
        }

        if (typeData.UseXp != 0)
        {
            player.Magics.ResetXp();
            player.Statuses.DetachStatus(StatusEffectType.XpFull);
            SendXpUpdate(player);
            BroadcastStatus(player);
        }

        // Set cooldown
        player.Magics.SetCooldown(magicType, typeData.Delay, now);

        // Execute spell logic based on sort
        var sort = (MagicSort)typeData.Sort;
        switch (sort)
        {
            case MagicSort.Recruit:
                ExecuteMagicRecruit(player, spell, typeData, interact, now);
                break;

            case MagicSort.AttachStatus:
                ExecuteMagicAttachStatus(player, spell, typeData, interact, now);
                break;

            case MagicSort.Cross:
            case MagicSort.Fan:
            case MagicSort.Bomb:
            case MagicSort.Square:
            case MagicSort.Line:
                ExecuteMagicArea(player, spell, typeData, interact, now);
                break;

            case MagicSort.Attack:
            default:
                ExecuteMagicSingleAttack(player, spell, typeData, interact, now);
                break;
        }
    }

    private void ExecuteMagicRecruit(Player player, LearnedMagic? spell, MagicTypeModel typeData, in MsgInteract interact, DateTime now)
    {
        uint targetId = interact.TargetId == 0 ? player.Id : interact.TargetId;
        var target = targetId == player.Id ? player : GetEntity(targetId);
        if (target == null || !target.IsAlive || target.MapId != player.MapId)
            return;

        int healAmount = typeData.Power > 0 ? typeData.Power : 100;
        target.Life = (ushort)Math.Min((int)target.MaxLife, target.Life + healAmount);
        SendLifeUpdate(target);

        var effect = MsgMagicEffect.CreateSingle(player.Id, typeData.Type, typeData.Level, target.Id, (uint)healAmount, (byte)player.Direction);
        BroadcastMagicEffect(player, effect);

        AwardSpellExp(player, spell, 10);
    }

    private void ExecuteMagicAttachStatus(Player player, LearnedMagic? spell, MagicTypeModel typeData, in MsgInteract interact, DateTime now)
    {
        uint targetId = interact.TargetId == 0 ? player.Id : interact.TargetId;
        var target = targetId == player.Id ? player : GetEntity(targetId);
        if (target == null || !target.IsAlive || target.MapId != player.MapId)
            return;

        var statusType = (StatusEffectType)typeData.Status;
        int durationSecs = typeData.Step > 0 ? typeData.Step : 20;
        int intervalMs = statusType == StatusEffectType.Poison ? 2000 : 0;

        target.Statuses.AttachStatus(statusType, typeData.Power, durationSecs, intervalMs, now);
        if (target is Player tp)
        {
            tp.RecalculateStats();
        }

        BroadcastStatus(target);

        var effect = MsgMagicEffect.CreateSingle(player.Id, typeData.Type, typeData.Level, target.Id, 0, (byte)player.Direction);
        BroadcastMagicEffect(player, effect);

        AwardSpellExp(player, spell, 10);
    }

    private void ExecuteMagicSingleAttack(Player player, LearnedMagic? spell, MagicTypeModel typeData, in MsgInteract interact, DateTime now)
    {
        var target = GetEntity(interact.TargetId);
        if (target == null || !target.IsAlive || target.MapId != player.MapId)
            return;

        int range = typeData.Distance > 0 ? typeData.Distance : 15;
        if (MapCoordinates.ChebyshevDistance(player.PosX, player.PosY, target.PosX, target.PosY) > range)
            return;

        player.Direction = MapCoordinates.GetDirection(player.PosX, player.PosY, target.PosX, target.PosY);

        if (target is Player targetPlayer && !targetPlayer.IsEvil)
        {
            player.CrimeExpireTime = now.AddSeconds(CombatConstants.CrimeKeepSecs);
        }

        int damage = BattleEngine.CalculateMagicDamage(player, target, typeData.Power);
        int actualLoss = Math.Min(damage, (int)target.Life);
        target.Life = (ushort)Math.Max(0, (int)target.Life - actualLoss);
        SendLifeUpdate(target);

        var effect = MsgMagicEffect.CreateSingle(player.Id, typeData.Type, typeData.Level, target.Id, (uint)damage, (byte)player.Direction);
        BroadcastMagicEffect(player, effect);

        AwardSpellExp(player, spell, (uint)damage);

        if (target.Life == 0)
        {
            ExecuteDeath(player, target);
        }
    }

    private void ExecuteMagicArea(Player player, LearnedMagic? spell, MagicTypeModel typeData, in MsgInteract interact, DateTime now)
    {
        int centerX = interact.TargetId != 0 && GetEntity(interact.TargetId) is { } targetEntity
            ? targetEntity.PosX
            : interact.PosX;
        int centerY = interact.TargetId != 0 && GetEntity(interact.TargetId) is { } targetEntity2
            ? targetEntity2.PosY
            : interact.PosY;

        int range = typeData.Range > 0 ? typeData.Range : 5;
        if (!_maps.TryGetValue(player.MapId, out var map))
            return;

        var effectRoles = new List<EffectRole>();
        var killedEntities = new List<IWorldEntity>();

        foreach (var monster in _monstersById.Values)
        {
            if (!monster.IsAlive || monster.MapId != player.MapId) continue;
            if (MapCoordinates.ChebyshevDistance(centerX, centerY, monster.PosX, monster.PosY) <= range)
            {
                int dmg = BattleEngine.CalculateMagicDamage(player, monster, typeData.Power);
                int actualLoss = Math.Min(dmg, (int)monster.Life);
                monster.Life = (ushort)Math.Max(0, (int)monster.Life - actualLoss);
                effectRoles.Add(new EffectRole(monster.Id, (uint)dmg));
                if (monster.Life == 0)
                {
                    killedEntities.Add(monster);
                }
            }
        }

        foreach (var otherPlayer in _playersById.Values)
        {
            if (otherPlayer.Id == player.Id || !otherPlayer.IsAlive || otherPlayer.MapId != player.MapId)
                continue;

            if (MapCoordinates.ChebyshevDistance(centerX, centerY, otherPlayer.PosX, otherPlayer.PosY) <= range)
            {
                if (!otherPlayer.IsEvil)
                {
                    player.CrimeExpireTime = now.AddSeconds(CombatConstants.CrimeKeepSecs);
                }
                int dmg = BattleEngine.CalculateMagicDamage(player, otherPlayer, typeData.Power);
                int actualLoss = Math.Min(dmg, (int)otherPlayer.Life);
                otherPlayer.Life = (ushort)Math.Max(0, (int)otherPlayer.Life - actualLoss);
                SendLifeUpdate(otherPlayer);
                effectRoles.Add(new EffectRole(otherPlayer.Id, (uint)dmg));
                if (otherPlayer.Life == 0)
                {
                    killedEntities.Add(otherPlayer);
                }
            }
        }

        var effect = MsgMagicEffect.CreateArea(player.Id, typeData.Type, typeData.Level, (ushort)centerX, (ushort)centerY, (byte)player.Direction, effectRoles.ToArray());
        BroadcastMagicEffect(player, effect);

        uint totalDamage = 0;
        foreach (var role in effectRoles) totalDamage += role.Data;
        AwardSpellExp(player, spell, Math.Max(10, totalDamage));

        foreach (var victim in killedEntities)
        {
            ExecuteDeath(player, victim);
        }
    }

    private void AwardSpellExp(Player player, LearnedMagic? spell, uint amount)
    {
        if (spell == null) return;
        if (spell.AddExp(amount, (t, l) => _magicTypes?.Get(t, l), out bool leveledUp))
        {
            SendFlushExp(player, spell.Type, spell.Exp);
            if (leveledUp)
            {
                SendMagicInfo(player, spell.Type, spell.Level, spell.Exp);
                Logger.LogInformation("[{Name}] Player {PlayerId} leveled up spell {Type} to {Level}",
                    Name, player.Id, spell.Type, spell.Level);
            }
        }
    }

    private void BroadcastStatus(IWorldEntity entity)
    {
        if (_maps.TryGetValue(entity.MapId, out var map))
        {
            var writer = new PacketWriter();
            try
            {
                var statusMsg = MsgUserAttrib.CreateStatus(entity.Id, (ulong)entity.Statuses.Flags);
                statusMsg.Encode(ref writer);
                map.Broadcast(entity.PosX, entity.PosY, writer.AsSpan());
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    private void BroadcastMagicEffect(IWorldEntity user, in MsgMagicEffect effect)
    {
        if (_maps.TryGetValue(user.MapId, out var map))
        {
            var writer = new PacketWriter();
            try
            {
                effect.Encode(ref writer);
                map.Broadcast(user.PosX, user.PosY, writer.AsSpan());
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    private static void SendLifeUpdate(IWorldEntity entity)
    {
        if (entity is Player p)
        {
            var writer = new PacketWriter();
            try
            {
                var msg = MsgUserAttrib.CreateLife(p.Id, p.Life);
                msg.Encode(ref writer);
                p.Send(writer.AsSpan());
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    private static void SendManaUpdate(Player player)
    {
        var writer = new PacketWriter();
        try
        {
            var msg = MsgUserAttrib.CreateMana(player.Id, player.Mana);
            msg.Encode(ref writer);
            player.Send(writer.AsSpan());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void SendXpUpdate(Player player)
    {
        var writer = new PacketWriter();
        try
        {
            var msg = MsgUserAttrib.CreateXp(player.Id, player.Magics.Xp);
            msg.Encode(ref writer);
            player.Send(writer.AsSpan());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void SendFlushExp(Player player, ushort magicType, uint exp)
    {
        var writer = new PacketWriter();
        try
        {
            var msg = MsgFlushExp.CreateMagicExp(magicType, exp);
            msg.Encode(ref writer);
            player.Send(writer.AsSpan());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void SendMagicInfo(Player player, ushort magicType, byte level, uint exp)
    {
        var writer = new PacketWriter();
        try
        {
            var msg = MsgMagicInfo.Create(player.Id, magicType, level, exp);
            msg.Encode(ref writer);
            player.Send(writer.AsSpan());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private void HandleItem(Player player, ReadOnlySpan<byte> payload)
    {
        var msg = MsgItem.Decode(payload);
        if (!_maps.TryGetValue(player.MapId, out var map))
            return;

        switch (msg.Action)
        {
            case ItemAction.Buy:
                ProcessItemBuy(player, msg);
                break;

            case ItemAction.Sell:
                ProcessItemSell(player, msg);
                break;

            case ItemAction.Drop:
                ProcessItemDrop(player, map, msg);
                break;

            case ItemAction.DropMoney:
                ProcessItemDropMoney(player, map, msg);
                break;

            case ItemAction.Use:
            case ItemAction.Equip:
                ProcessItemUseOrEquip(player, map, msg);
                break;

            case ItemAction.UnEquip:
                ProcessItemUnEquip(player, map, msg);
                break;

            default:
                Logger.LogDebug("[{Name}] Unhandled item action {Action} from player {PlayerId}",
                    Name, msg.Action, player.Id);
                break;
        }
    }

    private void ProcessItemBuy(Player player, in MsgItem msg)
    {
        if (ItemTypeLookup == null)
            return;

        uint npcId = msg.Id;
        uint itemTypeId = msg.Data;

        if (ShopManager.BuyItem(npcId, itemTypeId, player, ItemTypeLookup, NextItemId, out var boughtItem, out var reason))
        {
            SendItemInfoMsg(player, boughtItem!.ToMsgItemInfo(ItemInfoAction.AddItem));
            SendItemMsg(player, MsgItem.CreateBuy(npcId, itemTypeId));
        }
        else
        {
            Logger.LogDebug("[{Name}] Player {PlayerId} failed to buy {ItemType} from NPC {NpcId}: {Reason}",
                Name, player.Id, itemTypeId, npcId, reason);
        }
    }

    private void ProcessItemSell(Player player, in MsgItem msg)
    {
        uint npcId = msg.Id;
        uint itemId = msg.Data != 0 ? msg.Data : msg.Id;

        if (ShopManager.SellItem(npcId, itemId, player, out var earned, out var reason))
        {
            SendItemMsg(player, new MsgItem(itemId, earned, ItemAction.Sell));
        }
        else
        {
            Logger.LogDebug("[{Name}] Player {PlayerId} failed to sell item {ItemId} to NPC {NpcId}: {Reason}",
                Name, player.Id, itemId, npcId, reason);
        }
    }

    private void ProcessItemDrop(Player player, GameMap map, in MsgItem msg)
    {
        uint itemId = msg.Id;
        int targetX = msg.PosX != 0 ? msg.PosX : player.PosX;
        int targetY = msg.PosY != 0 ? msg.PosY : player.PosY;

        if (MapCoordinates.ChebyshevDistance(player.PosX, player.PosY, targetX, targetY) > 2 || !map.Grid.IsWalkable(targetX, targetY))
        {
            targetX = player.PosX;
            targetY = player.PosY;
        }

        // 1. Check if item is in backpack
        if (player.Package.Remove(itemId, out var bagItem))
        {
            uint groundId = NextTrapId();
            var ground = new GroundItem
            {
                Id = groundId,
                ItemType = bagItem.Type,
                PosX = targetX,
                PosY = targetY,
                Item = bagItem
            };
            map.AddGroundItem(ground);

            var dropWriter = new PacketWriter();
            try
            {
                var dropMsg = MsgMapItem.CreateDrop(groundId, bagItem.Type, (ushort)targetX, (ushort)targetY);
                dropMsg.Encode(ref dropWriter);
                map.Broadcast(targetX, targetY, dropWriter.AsSpan());
            }
            finally
            {
                dropWriter.Dispose();
            }

            SendItemMsg(player, new MsgItem(itemId, 0, ItemAction.Drop));
            return;
        }

        // 2. Check if item is equipped
        var equipped = player.Equipment.EquippedItems.FirstOrDefault(i => i.Id == itemId);
        if (equipped != null)
        {
            var pos = equipped.Position;
            if (player.Equipment.TryUnEquip(pos, player.Package, out var unequipped, out _) && unequipped != null)
            {
                player.Package.Remove(unequipped.Id, out _);
                player.RecalculateStats();

                uint groundId = NextTrapId();
                var ground = new GroundItem
                {
                    Id = groundId,
                    ItemType = unequipped.Type,
                    PosX = targetX,
                    PosY = targetY,
                    Item = unequipped
                };
                map.AddGroundItem(ground);

                var dropWriter = new PacketWriter();
                try
                {
                    var dropMsg = MsgMapItem.CreateDrop(groundId, unequipped.Type, (ushort)targetX, (ushort)targetY);
                    dropMsg.Encode(ref dropWriter);
                    map.Broadcast(targetX, targetY, dropWriter.AsSpan());
                }
                finally
                {
                    dropWriter.Dispose();
                }

                SendItemMsg(player, new MsgItem(itemId, (uint)pos, ItemAction.DropEquipment));
                BroadcastPlayerAppearance(player);
            }
        }
    }

    private void ProcessItemDropMoney(Player player, GameMap map, in MsgItem msg)
    {
        uint amount = msg.Id != 0 ? msg.Id : msg.Data;
        if (amount == 0 || player.Money < amount)
            return;

        int targetX = msg.PosX != 0 ? msg.PosX : player.PosX;
        int targetY = msg.PosY != 0 ? msg.PosY : player.PosY;

        if (MapCoordinates.ChebyshevDistance(player.PosX, player.PosY, targetX, targetY) > 2 || !map.Grid.IsWalkable(targetX, targetY))
        {
            targetX = player.PosX;
            targetY = player.PosY;
        }

        player.Money -= amount;
        uint groundId = NextTrapId();
        var ground = new GroundItem
        {
            Id = groundId,
            Money = amount,
            PosX = targetX,
            PosY = targetY
        };
        map.AddGroundItem(ground);

        var dropWriter = new PacketWriter();
        try
        {
            var dropMsg = MsgMapItem.CreateDrop(groundId, amount, (ushort)targetX, (ushort)targetY);
            dropMsg.Encode(ref dropWriter);
            map.Broadcast(targetX, targetY, dropWriter.AsSpan());
        }
        finally
        {
            dropWriter.Dispose();
        }

        SendItemMsg(player, new MsgItem(amount, 0, ItemAction.DropMoney));
    }

    private void ProcessItemUseOrEquip(Player player, GameMap map, in MsgItem msg)
    {
        uint itemId = msg.Id;
        var item = player.Package.GetItem(itemId);
        if (item == null)
            return;

        if (item.IsEquipment())
        {
            ItemPosition targetPos = (ItemPosition)msg.Data;
            if (player.Equipment.TryEquip(item, targetPos, player, player.Package, out var unequipped, out var reason))
            {
                player.RecalculateStats();
                SendItemMsg(player, MsgItem.CreateEquip(item.Id, item.Position));

                if (unequipped != null)
                {
                    SendItemInfoMsg(player, unequipped.ToMsgItemInfo(ItemInfoAction.AddItem));
                }

                BroadcastPlayerAppearance(player);
            }
            else
            {
                Logger.LogDebug("[{Name}] Player {PlayerId} failed to equip item {ItemId}: {Reason}",
                    Name, player.Id, itemId, reason);
            }
        }
        else if (item.IsMedicine())
        {
            if (item.TypeData.Life > 0)
            {
                player.Life = (ushort)Math.Min((int)player.MaxLife, (int)player.Life + item.TypeData.Life);
            }
            player.Package.Remove(item.Id, out _);
            SendItemMsg(player, MsgItem.CreateUse(item.Id));
        }
    }

    private void ProcessItemUnEquip(Player player, GameMap map, in MsgItem msg)
    {
        ItemPosition slot = (ItemPosition)msg.Data;
        if (slot == ItemPosition.None && msg.Id <= 11)
        {
            slot = (ItemPosition)msg.Id;
        }

        if (player.Equipment.TryUnEquip(slot, player.Package, out var unequipped, out var reason) && unequipped != null)
        {
            player.RecalculateStats();
            SendItemMsg(player, MsgItem.CreateUnEquip(unequipped.Id, slot));
            SendItemInfoMsg(player, unequipped.ToMsgItemInfo(ItemInfoAction.AddItem));
            BroadcastPlayerAppearance(player);
        }
        else
        {
            Logger.LogDebug("[{Name}] Player {PlayerId} failed to unequip slot {Slot}: {Reason}",
                Name, player.Id, slot, reason);
        }
    }

    private void HandleMapItem(Player player, ReadOnlySpan<byte> payload)
    {
        var msg = MsgMapItem.Decode(payload);
        if (!_maps.TryGetValue(player.MapId, out var map))
            return;

        if (msg.Action == MapItemAction.Pick)
        {
            if (!map.RemoveGroundItem(msg.ItemId, out var groundItem) || groundItem == null)
                return;

            int dist = MapCoordinates.ChebyshevDistance(player.PosX, player.PosY, groundItem.PosX, groundItem.PosY);
            if (dist > 2)
            {
                // Too far, restore to map
                map.AddGroundItem(groundItem);
                return;
            }

            if (groundItem.IsMoney)
            {
                player.Money += groundItem.Money;

                var delWriter = new PacketWriter();
                try
                {
                    var delMsg = MsgMapItem.CreateDelete(groundItem.Id, (ushort)groundItem.PosX, (ushort)groundItem.PosY);
                    delMsg.Encode(ref delWriter);
                    map.Broadcast(groundItem.PosX, groundItem.PosY, delWriter.AsSpan());
                }
                finally
                {
                    delWriter.Dispose();
                }
            }
            else if (groundItem.Item != null)
            {
                if (player.Package.IsFull)
                {
                    // Backpack full, restore to map
                    map.AddGroundItem(groundItem);
                    return;
                }

                groundItem.Item.Position = ItemPosition.Backpack;
                player.Package.TryAdd(groundItem.Item);

                var delWriter = new PacketWriter();
                try
                {
                    var delMsg = MsgMapItem.CreateDelete(groundItem.Id, (ushort)groundItem.PosX, (ushort)groundItem.PosY);
                    delMsg.Encode(ref delWriter);
                    map.Broadcast(groundItem.PosX, groundItem.PosY, delWriter.AsSpan());
                }
                finally
                {
                    delWriter.Dispose();
                }

                SendItemInfoMsg(player, groundItem.Item.ToMsgItemInfo(ItemInfoAction.AddItem));
            }
        }
    }

    private static void SendItemMsg(Player player, in MsgItem msg)
    {
        var writer = new PacketWriter();
        try
        {
            var copy = msg;
            copy.Encode(ref writer);
            player.Send(writer.AsSpan());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void SendItemInfoMsg(Player player, in MsgItemInfo msg)
    {
        var writer = new PacketWriter();
        try
        {
            var copy = msg;
            copy.Encode(ref writer);
            player.Send(writer.AsSpan());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private void BroadcastPlayerAppearance(Player player)
    {
        if (_maps.TryGetValue(player.MapId, out var map))
        {
            var spawnPacket = player.CreateSpawnPacket();
            var writer = new PacketWriter();
            try
            {
                spawnPacket.Encode(ref writer);
                map.Broadcast(player.PosX, player.PosY, writer.AsSpan(), excludeEntityId: player.Id);
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    /// <summary>
    /// Execute a single attack between attacker and target.
    /// <para><b>Single-Owner Rule:</b> Executed exclusively on this MapGroup thread, preventing concurrency races.</para>
    /// </summary>
    public void ExecuteAttack(IWorldEntity attacker, IWorldEntity target)
    {
        if (!attacker.IsAlive || !target.IsAlive || attacker.MapId != target.MapId)
            return;

        // Range check (Chebyshev distance)
        int dist = MapCoordinates.ChebyshevDistance(attacker.PosX, attacker.PosY, target.PosX, target.PosY);
        if (dist > attacker.AttackRange)
            return;

        attacker.LastAttackTime = Clock.GetUtcNow().UtcDateTime;

        // Authoritative damage calculation
        int damage = BattleEngine.CalculateDamage(attacker, target);
        int actualLoss = Math.Min(damage, (int)target.Life);
        target.Life = (ushort)Math.Max(0, (int)target.Life - actualLoss);

        // Broadcast attack packet with damage dealt to observers
        if (_maps.TryGetValue(attacker.MapId, out var map))
        {
            var writer = new PacketWriter();
            try
            {
                var attackMsg = MsgInteract.CreateAttack(
                    attacker.Id,
                    target.Id,
                    (ushort)attacker.PosX,
                    (ushort)attacker.PosY,
                    (uint)damage);
                attackMsg.Encode(ref writer);
                map.Broadcast(attacker.PosX, attacker.PosY, writer.AsSpan());
            }
            finally
            {
                writer.Dispose();
            }
        }

        // Crime check on damage dealt to innocent player (User.cpp:2223)
        if (attacker is Player attackerPlayer && target is Player targetPlayer && !targetPlayer.IsEvil)
        {
            attackerPlayer.CrimeExpireTime = Clock.GetUtcNow().UtcDateTime.AddSeconds(CombatConstants.CrimeKeepSecs);
        }

        // Single-owner death resolution: prevents double-kill race conditions
        if (target.Life == 0)
        {
            ExecuteDeath(attacker, target);
        }
    }

    private void ExecuteDeath(IWorldEntity killer, IWorldEntity victim)
    {
        victim.TargetId = 0;
        killer.TargetId = 0;

        // Broadcast kill and death action packets
        if (_maps.TryGetValue(victim.MapId, out var map))
        {
            var writerKill = new PacketWriter();
            try
            {
                var killMsg = MsgInteract.CreateKill(killer.Id, victim.Id, (ushort)victim.PosX, (ushort)victim.PosY, (uint)DieMode.Normal);
                killMsg.Encode(ref writerKill);
                map.Broadcast(victim.PosX, victim.PosY, writerKill.AsSpan());
            }
            finally
            {
                writerKill.Dispose();
            }

            var writerDie = new PacketWriter();
            try
            {
                var dieMsg = MsgAction.CreateDie(victim.Id, (ushort)victim.PosX, (ushort)victim.PosY, (uint)DieMode.Normal);
                dieMsg.Encode(ref writerDie);
                map.Broadcast(victim.PosX, victim.PosY, writerDie.AsSpan());
            }
            finally
            {
                writerDie.Dispose();
            }
        }

        // PK scoring: killing innocent player adds PkValueOneKill (User.cpp:2659 AddPk)
        if (killer is Player killerPlayer && victim is Player victimPlayer && !victimPlayer.IsEvil)
        {
            killerPlayer.PkPoints += CombatConstants.PkValueOneKill;
            killerPlayer.CrimeExpireTime = Clock.GetUtcNow().UtcDateTime.AddSeconds(CombatConstants.CrimeKeepSecs);
            Logger.LogInformation("[{Name}] Player {Killer} killed innocent {Victim}. PK points: {Pk}",
                Name, killerPlayer.Name, victimPlayer.Name, killerPlayer.PkPoints);
        }

        // Monster death: drop silver and remove from map
        if (victim is Monster monster)
        {
            if (monster.DropMoney > 0 && _maps.TryGetValue(monster.MapId, out var dropMap))
            {
                uint dropItemId = NextTrapId();
                var groundMoney = new GroundItem
                {
                    Id = dropItemId,
                    Money = monster.DropMoney,
                    PosX = monster.PosX,
                    PosY = monster.PosY
                };
                dropMap.AddGroundItem(groundMoney);

                var dropWriter = new PacketWriter();
                try
                {
                    var dropMsg = MsgMapItem.CreateDrop(dropItemId, monster.DropMoney, (ushort)monster.PosX, (ushort)monster.PosY);
                    dropMsg.Encode(ref dropWriter);
                    dropMap.Broadcast(monster.PosX, monster.PosY, dropWriter.AsSpan());
                }
                finally
                {
                    dropWriter.Dispose();
                }
            }

            RemoveMonster(monster.Id);
        }

        if (NpcTransport != null)
        {
            var killPacket = MsgInteract.CreateKill(killer.Id, victim.Id, (ushort)victim.PosX, (ushort)victim.PosY, (uint)DieMode.Normal);
            byte[] killBytes = MsgInteract.Encode(killPacket);
            _ = NpcTransport.SendAsync(new NpcPacket(victim.MapId, PacketType.Interact, killBytes)).AsTask();
        }
    }

    private void ProcessAutoAttacks(DateTime now)
    {
        // 1. Process player auto-attacks (CUser::ProcessAutoAttack)
        foreach (var player in _playersById.Values)
        {
            if (player.TargetId == 0 || !player.IsAlive)
                continue;

            var target = GetEntity(player.TargetId);
            if (target == null || !target.IsAlive || target.MapId != player.MapId)
            {
                player.TargetId = 0;
                continue;
            }

            if (now - player.LastAttackTime < TimeSpan.FromMilliseconds(player.AttackSpeedMs))
                continue;

            int dist = MapCoordinates.ChebyshevDistance(player.PosX, player.PosY, target.PosX, target.PosY);
            if (dist > player.AttackRange)
                continue;

            ExecuteAttack(player, target);
        }

        // 2. Process monster auto-attacks
        foreach (var monster in _monstersById.Values)
        {
            if (monster.TargetId == 0 || !monster.IsAlive)
                continue;

            var target = GetEntity(monster.TargetId);
            if (target == null || !target.IsAlive || target.MapId != monster.MapId)
            {
                monster.TargetId = 0;
                continue;
            }

            if (now - monster.LastAttackTime < TimeSpan.FromMilliseconds(monster.AttackSpeedMs))
                continue;

            int dist = MapCoordinates.ChebyshevDistance(monster.PosX, monster.PosY, target.PosX, target.PosY);
            if (dist > monster.AttackRange)
                continue;

            ExecuteAttack(monster, target);
        }
    }

    protected override void OnTimer()
    {
        // Original order (MapGroupKernel/MapGroup.cpp CMapGroup::OnTimer):
        // 1. RoleManager.OnTimer
        // 2. MapManager.OnTimer
        // 3. UserManager.OnTimer (Heartbeat, auto-attack, etc.)
        // 4. LeaveWord.OnTimer
        // 5. ProcessEvent (when its own timer fires)
        // 6. MercenaryTask.OnTimer

        var now = Clock.GetUtcNow().UtcDateTime;

        // Auto-attack repeating loop (OPUS §3.6, CUser::ProcessAutoAttack)
        ProcessAutoAttacks(now);

        var timeoutLimit = TimeSpan.FromSeconds(TimingConstants.TickSecs * TimingConstants.HeartbeatTimeoutFactor);
        var serverTickInterval = TimeSpan.FromSeconds(TimingConstants.TickSecs);

        List<uint>? toKick = null;

        foreach (var player in _playersById.Values)
        {
            // Crime status expiry check
            if (player.CrimeExpireTime.HasValue && player.CrimeExpireTime.Value <= now)
            {
                player.CrimeExpireTime = null;
            }

            // 1. Server heartbeat tick check (every TICK_SECS = 10s)
            if (now - player.LastServerHeartbeat >= serverTickInterval)
            {
                var writer = new PacketWriter();
                try
                {
                    var tickPacket = new MsgTick(player.Id);
                    tickPacket.Encode(ref writer);
                    player.Send(writer.AsSpan());
                    player.LastServerHeartbeat = now;
                }
                finally
                {
                    writer.Dispose();
                }
            }

            // 2. Client heartbeat inactivity timeout check (2.5 * TICK_SECS = 25s)
            if (now - player.LastClientHeartbeat >= timeoutLimit)
            {
                toKick ??= [];
                toKick.Add(player.Id);
            }

            // 3. Status effect ticks & expiration (Poison ticks, buffs expiring)
            player.Statuses.OnTimer(now, out var expired, out var periodicDamage);
            if (expired.Count > 0)
            {
                player.RecalculateStats();
                BroadcastStatus(player);
            }
            if (periodicDamage.Count > 0)
            {
                foreach (int dmg in periodicDamage)
                {
                    player.Life = (ushort)Math.Max(1, (int)player.Life - dmg);
                    SendLifeUpdate(player);
                }
            }

            // 4. XP accumulation (every 3 seconds, +1 XP up to 100)
            if (now - player.Magics.LastXpTick >= TimeSpan.FromSeconds(3))
            {
                player.Magics.LastXpTick = now;
                if (player.Magics.AddXp(1, out bool reachedFull))
                {
                    SendXpUpdate(player);
                    if (reachedFull)
                    {
                        player.Statuses.AttachStatus(StatusEffectType.XpFull, 0, 20, now: now);
                        BroadcastStatus(player);
                    }
                }
            }
        }

        // Monster status ticks (e.g. poison damage)
        foreach (var monster in _monstersById.Values)
        {
            if (!monster.IsAlive) continue;
            monster.Statuses.OnTimer(now, out _, out var monsterDmg);
            if (monsterDmg.Count > 0)
            {
                foreach (int dmg in monsterDmg)
                {
                    monster.Life = (ushort)Math.Max(0, (int)monster.Life - dmg);
                    if (!monster.IsAlive)
                    {
                        ExecuteDeath(monster, monster);
                        break;
                    }
                }
            }
        }

        if (toKick != null)
        {
            foreach (var pid in toKick)
            {
                if (_playersById.TryGetValue(pid, out var player))
                {
                    Logger.LogWarning("[{Name}] Kicking player {PlayerName} ({PlayerId}) due to heartbeat silence > 25s",
                        Name, player.Name, player.Id);
                    player.Kick("Heartbeat timeout");
                    RemovePlayer(pid);
                }
            }
        }
    }

    protected override void OnStart()
    {
        Logger.LogInformation("[{Name}] Started (group {Index}/{Total})", Name, _groupIndex, _totalGroups);
    }

    protected override void OnStop()
    {
        Logger.LogInformation("[{Name}] Stopped", Name);
    }
}
