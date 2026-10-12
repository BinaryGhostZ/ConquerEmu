// Conquer.NpcServer — Central NPC world, manager, and message router.
// Original: my_server/NpcKernel/NpcWorld.cpp and NpcManager.cpp.

using System.Collections.Concurrent;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages.Packets;
using Conquer.Shared.Transport;

namespace Conquer.NpcServer;

/// <summary>
/// Root manager and simulation engine for the NPC server.
/// Original: my_server/NpcKernel/NpcWorld.cpp CNpcWorld.
/// </summary>
public sealed class NpcWorld
{
    private readonly INpcTransport? _transport;
    private readonly ConcurrentDictionary<uint, NpcWorldMap> _maps = new();
    private readonly ConcurrentDictionary<uint, NpcUser> _users = new();
    private readonly ConcurrentDictionary<uint, NpcBrain> _npcs = new();
    private readonly ConcurrentDictionary<ushort, NpcTypeData> _types = new();
    private readonly List<NpcGenerator> _generators = [];

    private uint _nextNpcId = IdRanges.MonsterIdFirst;
    private int _step = TimingConstants.MaxNpcPerOnTimer;
    private int _currentGenIndex;

    public IEnumerable<NpcBrain> Npcs => _npcs.Values;
    public IEnumerable<NpcUser> Users => _users.Values;
    public IReadOnlyCollection<NpcGenerator> Generators => _generators;

    public NpcWorld(INpcTransport? transport = null)
    {
        _transport = transport;
    }

    public void AddMap(NpcWorldMap map) => _maps[map.MapId] = map;
    public void AddMap(uint mapId, int width = 1000, int height = 1000) => AddMap(new NpcWorldMap(mapId, width, height));
    public NpcWorldMap? GetMap(uint mapId) => _maps.GetValueOrDefault(mapId);

    public void RegisterType(NpcTypeData type) => _types[type.TypeId] = type;
    public NpcTypeData? GetType(ushort typeId) => _types.GetValueOrDefault(typeId);

    public void AddGenerator(NpcGenerator generator) => _generators.Add(generator);

    public NpcUser? GetUser(uint userId) => _users.GetValueOrDefault(userId);
    public NpcBrain? GetNpc(uint npcId) => _npcs.GetValueOrDefault(npcId);

    public uint GenerateNpcId() => Interlocked.Increment(ref _nextNpcId);

    public void RegisterNpc(NpcBrain npc)
    {
        _npcs[npc.Id] = npc;
        if (_maps.TryGetValue(npc.MapId, out var map))
        {
            map.EnterNpc(npc.Id, npc.PosX, npc.PosY);
        }
    }

    public void RemoveNpc(uint npcId)
    {
        if (_npcs.TryRemove(npcId, out var npc))
        {
            npc.IsAlive = false;
            npc.Generator?.OnNpcDeath(npcId);
            if (_maps.TryGetValue(npc.MapId, out var map))
            {
                map.LeaveNpc(npc.Id, npc.PosX, npc.PosY);
            }
        }
    }

    /// <summary>
    /// Periodic 500ms simulation tick.
    /// Original: CNpcManager::OnTimer and CNpcWorld::OnTimer.
    /// </summary>
    public void OnTimer(long nowMs, Random? random = null)
    {
        random ??= Random.Shared;

        // 1. Monster Generators with adaptive step budget
        if (_generators.Count > 0)
        {
            int maxSpawn = TimingConstants.MaxNpcPerOnTimer;
            int count = _generators.Count;

            for (int i = 0; i < count; i++)
            {
                var gen = _generators[_currentGenIndex];
                int spawnBudget = Math.Min(maxSpawn, _step);
                int spawned = gen.Generator(
                    nowMs,
                    spawnBudget,
                    () => GenerateNpcId(),
                    info =>
                    {
                        // Create local brain and send MsgAiNpcInfo creation request
                        var type = GetType(info.NpcType) ?? new NpcTypeData { TypeId = info.NpcType };
                        var brain = new NpcBrain(info.NpcId, type, info.MapId, info.PosX, info.PosY)
                        {
                            Generator = gen,
                        };
                        RegisterNpc(brain);

                        byte[] packetBytes = MsgAiNpcInfo.Encode(info);
                        SendPacket(new NpcPacket(info.NpcId, PacketType.AiNpcInfo, packetBytes));
                    },
                    random);

                maxSpawn -= spawned;
                _currentGenIndex = (_currentGenIndex + 1) % _generators.Count;

                if (maxSpawn <= 0)
                    break;
            }

            // Adaptive step pacing
            if (maxSpawn <= 0)
            {
                _step = Math.Max(1, _step - 1);
            }
            else
            {
                int activeGen = (TimingConstants.MaxNpcPerOnTimer - maxSpawn) / Math.Max(1, _step);
                if (activeGen > 0)
                    _step = Math.Clamp(TimingConstants.MaxNpcPerOnTimer / activeGen, 1, TimingConstants.MaxNpcPerOnTimer);
                else
                    _step = TimingConstants.MaxNpcPerOnTimer;
            }
        }

        // 2. Loop over awake monsters only
        foreach (var npc in _npcs.Values)
        {
            if (!npc.IsDormant && npc.IsAlive)
            {
                if (_maps.TryGetValue(npc.MapId, out var map))
                {
                    npc.OnTimer(nowMs, map, id => GetUser(id), packet => SendPacket(packet), random);
                }
            }
        }
    }

    /// <summary>
    /// Dispatches an event received from GameServer.
    /// Original: Kernel.ProcessMsg(idPacket, msg, size, idNpc).
    /// </summary>
    public void ProcessGameServerPacket(NpcPacket packet, long nowMs)
    {
        switch (packet.PacketType)
        {
            case PacketType.Player:
            {
                var player = MsgPlayer.Decode(packet.Payload.Span);
                uint mapId = packet.NpcId != 0 ? packet.NpcId : 1002;
                var user = _users.GetOrAdd(player.EntityId, id => new NpcUser(id, mapId, player.PosX, player.PosY)
                {
                    Name = player.Name,
                    Life = player.Life,
                });
                user.PosX = player.PosX;
                user.PosY = player.PosY;
                user.Life = player.Life;
                user.MapId = mapId;

                if (_maps.TryGetValue(mapId, out var map))
                {
                    map.EnterUser(user, npcId =>
                    {
                        if (_npcs.TryGetValue(npcId, out var brain))
                            brain.WakeUp(nowMs);
                    });
                }
                break;
            }

            case PacketType.Walk:
            {
                var walk = MsgWalk.Decode(packet.Payload.Span);
                if (_users.TryGetValue(walk.EntityId, out var user))
                {
                    var (dx, dy) = DirectionOffsets.GetOffset(walk.Direction);
                    ushort newX = (ushort)(user.PosX + dx);
                    ushort newY = (ushort)(user.PosY + dy);

                    if (_maps.TryGetValue(user.MapId, out var map))
                    {
                        map.MoveUser(user, newX, newY, npcId =>
                        {
                            if (_npcs.TryGetValue(npcId, out var brain))
                                brain.WakeUp(nowMs);
                        });
                    }
                    else
                    {
                        user.PosX = newX;
                        user.PosY = newY;
                    }
                }
                break;
            }

            case PacketType.Action:
            {
                var action = MsgAction.Decode(packet.Payload.Span);
                if (action.Action == ActionType.Die)
                {
                    if (_users.TryGetValue(action.EntityId, out var user))
                    {
                        user.Life = 0;
                    }
                    else if (_npcs.TryGetValue(action.EntityId, out var npc))
                    {
                        RemoveNpc(npc.Id);
                    }
                }
                else if (action.Action == ActionType.Jump)
                {
                    if (_users.TryGetValue(action.EntityId, out var user))
                    {
                        ushort targetX = action.Data != 0 ? (ushort)(action.Data & 0xFFFF) : action.PosX;
                        ushort targetY = action.Data != 0 ? (ushort)(action.Data >> 16) : action.PosY;

                        if (_maps.TryGetValue(user.MapId, out var map))
                        {
                            map.MoveUser(user, targetX, targetY, npcId =>
                            {
                                if (_npcs.TryGetValue(npcId, out var brain))
                                    brain.WakeUp(nowMs);
                            });
                        }
                    }
                }
                else if (action.Action is ActionType.DelRole or ActionType.FlyMap)
                {
                    if (_users.TryRemove(action.EntityId, out var user))
                    {
                        if (_maps.TryGetValue(user.MapId, out var map))
                        {
                            map.LeaveUser(user);
                        }
                    }
                }
                break;
            }

            case PacketType.Interact:
            {
                var interact = MsgInteract.Decode(packet.Payload.Span);
                if (interact.Type == InteractType.Kill)
                {
                    if (_users.TryGetValue(interact.TargetId, out var user))
                        user.Life = 0;
                    else if (_npcs.TryGetValue(interact.TargetId, out var npc))
                        RemoveNpc(npc.Id);
                }
                break;
            }

            case PacketType.AiNpcInfo:
            {
                var info = MsgAiNpcInfo.Decode(packet.Payload.Span);
                if (info.Action == AiNpcAction.Delete)
                {
                    RemoveNpc(info.NpcId);
                }
                break;
            }
        }
    }

    private void SendPacket(NpcPacket packet)
    {
        // Enqueue sending across INpcTransport asynchronously without blocking loop
        if (_transport != null)
        {
            _ = _transport.SendAsync(packet).AsTask();
        }
    }
}
