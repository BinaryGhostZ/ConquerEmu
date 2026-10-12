# DEVIATIONS.md — Intentional Differences from the Original C++ Server

> Required by OPUS §2.1. Every deviation from the original behavior is listed here with its reason.

---

## Approved Deviations (OPUS §12 defaults)

| # | Area | Original | .NET Port | Reason |
|---|------|----------|-----------|--------|
| D1 | DB access | Synchronous `mysql_query` on MapGroup/World threads | Async via dedicated DB worker per group; requests as messages, results posted back | Blocking DB calls on a game loop can stall thousands of players. The worker preserves the single-owner rule. Synchronous path kept for startup loading only. |
| D2 | Shell / GUI | Win32 MFC dialog (`CMsgServerDlg`) | Console host + metrics endpoint | Original GUI served as a debug display; replaced by structured logging + OpenTelemetry-compatible metrics. |
| D3 | Packet Serialization | Raw struct casts with pointer math (`(MsgAccount*)pBuf`) | Safe `PacketReader` / `PacketWriter` (`ref struct`) over spans | OPUS §12.4: Zero allocation over rented spans, eliminates buffer overrun hazards, GC pinning issues, and permits 100% safe C# code (`AllowUnsafeBlocks=false`). |

| D4 | NPC Transport | TCP socket only (`CClientSocket`) | Dual transport: `TcpNpcTransport` and `ChannelNpcTransport` via `INpcTransport` | Allows zero-socket high-performance in-process channel pair when running GameServer and NpcServer in the same process, and enables fast deterministic unit testing. |

---

## Deviations Found During Implementation

| # | Area | Original file/function | Original behavior | .NET behavior | Reason |
|---|------|----------------------|-------------------|---------------|--------|
| D5 | Packet Framing | `MsgSynInfo.h` / `CMsgSynInfo` | Comment indicated 52-byte header struct | Wire layout contains 56 bytes: header (4) + attributes (20) + two 16-byte strings (32) = 56 bytes (+8 trailer = 64) | Accurate sizing prevents buffer underflow and bounds mismatches. |
| D6 | Packet Trailer Trimming | `PacketReader.cs` | Unsafe C++ cast raw buffer leaving trailer in memory | `PacketReader` trims buffer to declared `WireProtocol.ReadLength` when full wire packet is provided | Prevents string parser from misinterpreting "TQServer" trailer as length-prefixed string list. |
| D7 | Single-Owner Social Concurrency | `Team.cpp`, `SquareDeal.cpp`, `Syndicate.cpp` | Multi-threaded synchronization with OS mutexes/critical sections | Single-owner isolation: all state updates occur strictly within `MapGroupLoop` | Preserves OPUS §4.1/§4.2 lock-free guarantee across teams, trade, guilds, and friends. |
| D8 | Entity Spatial & Hashing Lookups | `CUserList`, `CRoleList` | Linear linked-list traversals | O(1) keyed `Dictionary<uint, IWorldEntity>` and spatial `GameBlock` hashing | High-throughput entity queries maintaining predictable latency under 2,000+ concurrent players. |
