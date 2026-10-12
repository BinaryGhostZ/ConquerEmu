// Conquer.Shared — Timing constants ported from the original C++ source.
// Original: Share/define.h, MapGroupKernel/MapGroup.cpp, MsgServer/SocketThread.cpp,
//           MsgServer/WorldThread.cpp, NpcKernel/NpcManager.cpp, NpcKernel/Npc.cpp

namespace Conquer.Shared;

/// <summary>
/// Timing constants extracted from the original server source.
/// Every value here is exact — do not change without logging in DEVIATIONS.md.
/// </summary>
public static class TimingConstants
{
    /// <summary>MapGroup OnTimer period (ms). Original: MapGroupKernel/MapGroup.cpp ONTIMER interval.</summary>
    public const int MapGroupOnTimerMs = 500;

    /// <summary>Socket kernel OnTimer period (ms). Original: MsgServer/SocketThread.cpp.</summary>
    public const int SocketKernelOnTimerMs = 1000;

    /// <summary>World kernel OnTimer period (ms). Original: MsgServer/WorldThread.cpp.</summary>
    public const int WorldKernelOnTimerMs = 1000;

    /// <summary>Socket polling period (ms). Original: MsgServer/SocketThread.cpp SOCKET_PROCESS_INTERVAL.</summary>
    public const int SocketProcessIntervalMs = 40;

    /// <summary>Max wait for an internal message (ms). Original: Share/define.h THREADDELAY_MS.</summary>
    public const int ThreadDelayMs = 400;

    /// <summary>If clock drifts more than this many seconds, reset next-tick time. Original: auto-correct logic.</summary>
    public const int TimerOffsetLimitSecs = 10;

    /// <summary>Wait for thread shutdown (ms). Original: CLOSETHREAD_MILLISECS.</summary>
    public const int CloseThreadMs = 30000;

    /// <summary>NPC server system OnTimer period (ms). Original: NpcServer/KernelThread.cpp.</summary>
    public const int NpcSystemOnTimerMs = 500;

    /// <summary>Max monsters spawned per NPC tick (generator budget). Original: NpcKernel/NpcManager.cpp MAXNPC_PER_ONTIMER.</summary>
    public const int MaxNpcPerOnTimer = 20;

    /// <summary>Idle monster scan interval (seconds). Original: NpcKernel/Npc.cpp THINK_SECS.</summary>
    public const int ThinkSecs = 1;

    /// <summary>No player nearby for this many seconds → monster sleeps. Original: DORMANCY_SECS.</summary>
    public const int DormancySecs = 60;

    /// <summary>Map cells per block ("room"). Original: Share/define.h CELLS_PER_BLOCK.</summary>
    public const int CellsPerBlock = 18;

    /// <summary>View distance in cells. Original: Share/define.h CELLS_PER_VIEW.</summary>
    public const int CellsPerView = 18;

    /// <summary>Client heartbeat tick interval (seconds). Server sends heartbeat every TICK_SECS.</summary>
    public const int TickSecs = 10;

    /// <summary>Kick if no client heartbeat for this factor × TickSecs. Original: 2.5 × TICK_SECS.</summary>
    public const double HeartbeatTimeoutFactor = 2.5;

    /// <summary>Slow-message logging threshold (ms). OPUS §9.</summary>
    public const int SlowMessageThresholdMs = 20;
}
