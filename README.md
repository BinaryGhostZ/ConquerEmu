# ConquerEmu — Conquer Online .NET 10 Server Port

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![C# 14](https://img.shields.io/badge/C%23-14.0-239120?logo=csharp)](https://docs.microsoft.com/dotnet/csharp/)
[![Build & Test](https://img.shields.io/badge/Tests-95%20Passing-brightgreen)]()
[![Zero Warnings](https://img.shields.io/badge/Warnings-0%20(TreatAsErrors)-success)]()

A faithful, modern, high-performance re-implementation of the classic **2003 Conquer Online** C++ game server in **.NET 10 / C# 14**.

---

## Architecture & Design Principles

ConquerEmu preserves the core 2003 distributed server design while adopting modern, safe, high-throughput .NET 10 paradigms:

1. **Three Process Topology:**
   - **Account Server:** Authentication, server registry, and one-time login hand-off tokens.
   - **Game Server (MsgServer):** World loop, MapGroup kernel loops, authoritative combat, inventory, items, magic, social systems, and game rules.
   - **NPC Server:** Independent AI simulation, 18x18-cell dormancy grid, generator spawners, and 8-way pathfinding state machines.

2. **Single-Owner Concurrency:**
   - Each `MapGroupLoop` thread owns its designated maps, players, monsters, and items.
   - **No locks** in game logic execution. Inter-thread communication is handled via lock-free channels and thread-safe queues.

3. **Zero-Allocation Hot Paths:**
   - Zero heap allocation during packet parsing and serialization using `PacketReader` and `PacketWriter` (`ref struct` over `ArrayPool<byte>.Shared` spans).
   - Strict `<AllowUnsafeBlocks>false</AllowUnsafeBlocks>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

4. **Brain / Body Split (NPC Simulation):**
   - The NPC server maintains its own localized world view synchronized through network events.
   - Decision-making occurs in `NpcBrain`, while the Game Server authoritatively validates movement boundaries, line of sight, and combat ranges.

---

## Solution Layout

```
SRC/
├── OPUS.md                       # Canonical architecture specification
├── process.md                    # Detailed progress log and implementation tracker
├── DEVIATIONS.md                 # Documented architectural differences from C++
├── my_server/                    # Original 2003 C++ source reference
└── ConquerServer/                # .NET 10 solution root
    ├── ConquerServer.slnx        # Modern solution file
    ├── src/
    │   ├── Conquer.Shared/       # Packet models, wire framing, constants, Direction, spans
    │   ├── Conquer.Net/          # SecuritySocket, Blowfish/Cast5 crypto, rate limiters
    │   ├── Conquer.Runtime/      # MapGroupLoop, GameMap, spatial partitioning, Combat, Magic, Social
    │   ├── Conquer.Data/         # Entity repositories, cq_* data mappings
    │   ├── Conquer.AccountServer/# Account auth, session tokens, server registry
    │   ├── Conquer.GameServer/   # World loop, player lifecycle, map dispatch
    │   ├── Conquer.NpcServer/    # NpcBrain AI, NpcGenerator, pathfinding, dormancy
    │   └── Conquer.Host/         # Generic host composition, DI, metrics
    └── tests/
        ├── Conquer.UnitTests/    # 67 isolated unit tests
        ├── Conquer.IntegrationTests/# 28 end-to-end integration tests
        └── Conquer.StressTests/  # Load & stress benchmarks
```

---

## Implementation Roadmap

| Phase | Description | Status |
|:-----:|-------------|:------:|
| **1** | Foundation, 11-project solution, Directory.Build.props |  Completed |
| **2** | NetWork layer, SecuritySocket, Blowfish/Cast5, rate limiting |  Completed |
| **3** | AccountServer, authentication, login hand-off tokens |  Completed |
| **4** | MapGroup kernel, 18x18 GameBlock spatial partitioning, movement |  Completed |
| **5** | NpcServer, AI state machines, spawners, dual TCP/Channel transport |  Completed |
| **6** | Item system, 40-slot inventory, 11 equipment slots, NPC shops, drops |  Completed |
| **7** | Magic & status system, spellbook, 64-bit KeepEffect, Superman/Cyclone XP skills |  Completed |
| **8** | Guilds, teams, chat, friends, trading, relationships |  Completed |
| **9** | End-to-end integration, stress testing, polish | 🟡 Next Up |

---

## Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Building
Build the entire solution with zero warnings:
```powershell
dotnet build ConquerServer/ConquerServer.slnx
```

### Running Tests
Execute the complete test suite (95 unit and integration tests):
```powershell
dotnet test ConquerServer/ConquerServer.slnx
```

---

## Documentation
- [`OPUS.md`](OPUS.md) — Architectural design, packet layouts, and concurrency rules.
- [`process.md`](process.md) — Detailed implementation progress, completed phases, and conventions.
- [`DEVIATIONS.md`](DEVIATIONS.md) — Documented improvements over the original 2003 C++ codebase.
