// Conquer.NpcServer — Monster AI state machine and decision engine.
// Original: my_server/NpcKernel/Npc.cpp.

using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages.Packets;
using Conquer.Shared.Transport;

namespace Conquer.NpcServer;

/// <summary>
/// Autonomous monster agent running on NpcServer.
/// Implements 5-mode AI state machine (DORMANCY, IDLE, FORWARD, ATTACK, ESCAPE).
/// Original: my_server/NpcKernel/Npc.cpp CNpc.
/// </summary>
public sealed class NpcBrain
{
    public uint Id { get; init; }
    public NpcTypeData TypeData { get; init; }
    public uint MapId { get; set; }
    public ushort PosX { get; set; }
    public ushort PosY { get; set; }
    public Direction Dir { get; set; } = Direction.South;
    public NpcMode ActMode { get; private set; } = NpcMode.Idle;
    public NpcGenerator? Generator { get; set; }

    public uint ActTargetId { get; set; }
    public uint MoveTargetId { get; set; }

    public bool IsAlive { get; set; } = true;
    public bool IsDormant => ActMode == NpcMode.Dormancy;

    private long _lastActionTime;
    private long _lastMoveTime;
    private long _lastAttackTime;
    private long _lastDormancyCheckTime;

    public NpcBrain(uint id, NpcTypeData typeData, uint mapId, ushort posX, ushort posY)
    {
        Id = id;
        TypeData = typeData;
        MapId = mapId;
        PosX = posX;
        PosY = posY;
    }

    public void ChangeMode(NpcMode newMode)
    {
        ActMode = newMode;
        if (newMode == NpcMode.Dormancy)
        {
            ActTargetId = 0;
            MoveTargetId = 0;
        }
    }

    public void WakeUp(long nowMs)
    {
        _lastDormancyCheckTime = nowMs;
        if (ActMode == NpcMode.Dormancy)
        {
            ChangeMode(NpcMode.Idle);
        }
    }

    public bool CheckDormancy(long nowMs, NpcWorldMap map)
    {
        if (nowMs - _lastDormancyCheckTime < TimingConstants.DormancySecs * 1000L)
            return false;

        _lastDormancyCheckTime = nowMs;
        if (map.SetDormancy(PosX, PosY))
        {
            ChangeMode(NpcMode.Dormancy);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Executes one 500ms AI tick.
    /// Original: CNpc::OnTimer.
    /// </summary>
    public void OnTimer(
        long nowMs,
        NpcWorldMap map,
        Func<uint, NpcUser?> userLookup,
        Action<NpcPacket> sendPacket,
        Random? random = null)
    {
        random ??= Random.Shared;

        if (CheckDormancy(nowMs, map))
            return;

        if (!IsAlive || IsDormant)
            return;

        // Up to 10 AI evaluation transitions per tick
        for (int i = 0; i < 10; i++)
        {
            switch (ActMode)
            {
                case NpcMode.Escape:
                    if (ProcessEscape(nowMs, map, userLookup, sendPacket))
                        return;
                    break;

                case NpcMode.Attack:
                    if (ProcessAttack(nowMs, map, userLookup, sendPacket))
                        return;
                    break;

                case NpcMode.Forward:
                    if (ProcessForward(nowMs, map, userLookup, sendPacket))
                        return;
                    break;

                case NpcMode.Idle:
                    if (ProcessIdle(nowMs, map, userLookup, sendPacket, random))
                        return;
                    break;

                default:
                    return;
            }
        }
    }

    private bool ProcessIdle(
        long nowMs,
        NpcWorldMap map,
        Func<uint, NpcUser?> userLookup,
        Action<NpcPacket> sendPacket,
        Random random)
    {
        long thinkIntervalMs = Math.Max(TypeData.AttackSpeedMs * 3 / 1000, TimingConstants.ThinkSecs) * 1000L;
        if (nowMs - _lastActionTime < thinkIntervalMs)
            return true;

        _lastActionTime = nowMs;

        if (TypeData.AtkUserActive && FindNewTarget(map, userLookup))
        {
            NpcUser? target = userLookup(MoveTargetId);
            if (target != null && target.IsAlive)
            {
                int dist = Distance(target.PosX, target.PosY);
                if (dist <= TypeData.AttackRange)
                {
                    ChangeMode(NpcMode.Attack);
                    return false;
                }
                else
                {
                    ChangeMode(NpcMode.Forward);
                    return false;
                }
            }
        }

        // Idle wandering within generator region
        if (Generator != null && Generator.IsInRegion(PosX, PosY))
        {
            if (random.Next(4) == 0)
            {
                Direction randDir = (Direction)random.Next(8);
                if (map.IsMoveEnable(PosX, PosY, randDir))
                {
                    ExecuteMoveStep(randDir, WalkMode.Walk, map, sendPacket);
                }
            }
        }

        return true;
    }

    private bool ProcessAttack(
        long nowMs,
        NpcWorldMap map,
        Func<uint, NpcUser?> userLookup,
        Action<NpcPacket> sendPacket)
    {
        NpcUser? target = userLookup(ActTargetId);
        if (target != null && target.IsAlive && Distance(target.PosX, target.PosY) <= TypeData.AttackRange)
        {
            if (nowMs - _lastAttackTime >= TypeData.AttackSpeedMs)
            {
                _lastAttackTime = nowMs;

                // Send MsgInteract(Attack) to GameServer
                var interact = MsgInteract.CreateAttack(
                    senderId: Id,
                    targetId: ActTargetId,
                    posX: PosX,
                    posY: PosY,
                    damage: 0,
                    timestamp: (uint)nowMs);

                byte[] packetBytes = MsgInteract.Encode(interact);
                sendPacket(new NpcPacket(Id, PacketType.Interact, packetBytes));
            }
            return true;
        }

        // Target out of range or dead: re-evaluate
        if (FindNewTarget(map, userLookup))
        {
            NpcUser? newTarget = userLookup(MoveTargetId);
            if (newTarget != null && newTarget.IsAlive)
            {
                int dist = Distance(newTarget.PosX, newTarget.PosY);
                if (dist > TypeData.AttackRange)
                {
                    ChangeMode(NpcMode.Forward);
                    return true;
                }
            }
            return true;
        }

        ChangeMode(NpcMode.Idle);
        return true;
    }

    private bool ProcessForward(
        long nowMs,
        NpcWorldMap map,
        Func<uint, NpcUser?> userLookup,
        Action<NpcPacket> sendPacket)
    {
        NpcUser? target = userLookup(MoveTargetId);
        if (target == null || !target.IsAlive)
        {
            ChangeMode(NpcMode.Idle);
            return true;
        }

        int dist = Distance(target.PosX, target.PosY);
        if (dist <= TypeData.AttackRange)
        {
            ChangeMode(NpcMode.Attack);
            return true;
        }

        // Leash check: return home if pulled too far from generator center
        if (Generator != null && Generator.IsTooFar(PosX, PosY, 30))
        {
            ActTargetId = 0;
            MoveTargetId = 0;
            ChangeMode(NpcMode.Idle);
            return true;
        }

        // Paced movement step
        if (nowMs - _lastMoveTime >= TypeData.MoveSpeedMs)
        {
            _lastMoveTime = nowMs;
            if (DetectPath(target.PosX, target.PosY, map, out Direction stepDir))
            {
                ExecuteMoveStep(stepDir, WalkMode.Walk, map, sendPacket);
                if (Distance(target.PosX, target.PosY) <= TypeData.AttackRange)
                {
                    ChangeMode(NpcMode.Attack);
                }
            }
            else
            {
                ChangeMode(NpcMode.Idle);
            }
        }

        return true;
    }

    private bool ProcessEscape(
        long nowMs,
        NpcWorldMap map,
        Func<uint, NpcUser?> userLookup,
        Action<NpcPacket> sendPacket)
    {
        NpcUser? target = userLookup(ActTargetId);
        if (target == null || !target.IsAlive)
        {
            ChangeMode(NpcMode.Idle);
            return true;
        }

        int dist = Distance(target.PosX, target.PosY);
        if (dist >= TypeData.ViewRange * 2)
        {
            ChangeMode(NpcMode.Idle);
            return true;
        }

        if (nowMs - _lastMoveTime >= TypeData.RunSpeedMs)
        {
            _lastMoveTime = nowMs;
            if (DetectEscapePath(target.PosX, target.PosY, map, out Direction stepDir))
            {
                ExecuteMoveStep(stepDir, WalkMode.Run, map, sendPacket);
            }
            else
            {
                ChangeMode(NpcMode.Forward);
            }
        }

        return true;
    }

    public bool FindNewTarget(NpcWorldMap map, Func<uint, NpcUser?> userLookup)
    {
        var blocks = map.GetBlocksIn9Grid(PosX, PosY);
        int bestDist = TypeData.ViewRange + 1;
        uint bestTargetId = 0;

        foreach (var block in blocks)
        {
            foreach (var userId in block.Users)
            {
                NpcUser? user = userLookup(userId);
                if (user == null || !user.IsAlive || user.IsLurker)
                    continue;

                int dist = Distance(user.PosX, user.PosY);
                if (dist <= TypeData.ViewRange && dist < bestDist)
                {
                    bestDist = dist;
                    bestTargetId = user.Id;
                }
            }
        }

        if (bestTargetId != 0)
        {
            ActTargetId = bestTargetId;
            MoveTargetId = bestTargetId;
            return true;
        }

        ActTargetId = 0;
        MoveTargetId = 0;
        return false;
    }

    /// <summary>
    /// Evaluates 8 directions to find the best step reducing distance to target.
    /// Original: CNpc::DetectPath.
    /// </summary>
    public bool DetectPath(int targetX, int targetY, NpcWorldMap map, out Direction bestDir)
    {
        bestDir = Dir;
        int currentDist = Distance(targetX, targetY);
        int bestDist = currentDist;
        int bestSecondary = int.MaxValue;
        bool found = false;

        for (int i = 0; i < 8; i++)
        {
            Direction dir = (Direction)i;
            var (dx, dy) = DirectionOffsets.GetOffset(dir);
            int nx = PosX + dx;
            int ny = PosY + dy;

            if (map.IsMoveEnable(PosX, PosY, dir))
            {
                int dist = ChebyshevDistance(nx, ny, targetX, targetY);
                int secondary = (nx - targetX) * (nx - targetX) + (ny - targetY) * (ny - targetY);
                if (dist < bestDist || (dist == bestDist && secondary < bestSecondary && dist < currentDist))
                {
                    bestDist = dist;
                    bestSecondary = secondary;
                    bestDir = dir;
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Evaluates 8 directions to find the best step increasing distance from target.
    /// </summary>
    public bool DetectEscapePath(int targetX, int targetY, NpcWorldMap map, out Direction bestDir)
    {
        bestDir = Dir;
        int currentDist = Distance(targetX, targetY);
        int bestDist = currentDist;
        int bestSecondary = 0;
        bool found = false;

        for (int i = 0; i < 8; i++)
        {
            Direction dir = (Direction)i;
            var (dx, dy) = DirectionOffsets.GetOffset(dir);
            int nx = PosX + dx;
            int ny = PosY + dy;

            if (map.IsMoveEnable(PosX, PosY, dir))
            {
                int dist = ChebyshevDistance(nx, ny, targetX, targetY);
                int secondary = (nx - targetX) * (nx - targetX) + (ny - targetY) * (ny - targetY);
                if (dist > bestDist || (dist == bestDist && secondary > bestSecondary && dist > currentDist))
                {
                    bestDist = dist;
                    bestSecondary = secondary;
                    bestDir = dir;
                    found = true;
                }
            }
        }

        return found;
    }

    private void ExecuteMoveStep(
        Direction dir,
        WalkMode mode,
        NpcWorldMap map,
        Action<NpcPacket> sendPacket)
    {
        var (dx, dy) = DirectionOffsets.GetOffset(dir);
        ushort oldX = PosX;
        ushort oldY = PosY;
        ushort newX = (ushort)(PosX + dx);
        ushort newY = (ushort)(PosY + dy);

        PosX = newX;
        PosY = newY;
        Dir = dir;
        map.MoveNpc(Id, oldX, oldY, newX, newY);

        var walkPacket = new MsgWalk(Id, dir, mode);
        byte[] encoded = MsgWalk.Encode(walkPacket);
        sendPacket(new NpcPacket(Id, PacketType.Walk, encoded));
    }

    public int Distance(int targetX, int targetY) =>
        ChebyshevDistance(PosX, PosY, targetX, targetY);

    public static int ChebyshevDistance(int x1, int y1, int x2, int y2) =>
        Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
}
