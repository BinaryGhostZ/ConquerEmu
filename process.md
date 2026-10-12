# PROCESS.md — Conquer Online .NET 10 Server Port

> **Purpose (Rule 14.1):** This file is the single source of truth for any agent working on this project.  
> Read this BEFORE writing code. Follow every convention. Update the "Progress" section when you finish work.

---

## 1. Project Overview

Port the original Conquer Online C++ server (2003 architecture) to **.NET 10 / C# 14** as a faithful reproduction.  
Three processes: **Account Server**, **Game Server (MsgServer)**, **NPC Server**.  
Canonical spec: [`OPUS.md`](file:///c:/Users/Administrator/Desktop/SRC/OPUS.md).  
Original C++ source: `my_server/` (extracted from `my_server.7z`).

---

## 2. Solution Layout

```
c:\Users\Administrator\Desktop\SRC\
├── OPUS.md                       # Project brief (read-only)
├── process.md                    # THIS FILE
├── DEVIATIONS.md                 # Required by OPUS §2.1
├── my_server/                    # Original C++ source (read-only reference)
└── ConquerServer/                # The .NET 10 solution root
    ├── ConquerServer.sln
    ├── src/
    │   ├── Conquer.Shared/       # Packet structs, IDs, constants, wire framing, pooled buffers, message contracts
    │   ├── Conquer.Net/          # SecuritySocket (patched), IGameSocket, listener, rate limits
    │   ├── Conquer.Runtime/      # Channel loops, WorldRouter, WorldHost, MapGroup base, timers, metrics
    │   ├── Conquer.Data/         # DB access (async worker), repositories, cq_* mappings
    │   ├── Conquer.AccountServer/# Auth + game server registry + login hand-off
    │   ├── Conquer.GameServer/   # World loop, MapGroup kernel, game rules
    │   ├── Conquer.NpcServer/    # NpcWorld, AI, pathfinding, monster managers
    │   └── Conquer.Host/         # Generic host composition, config, logging, metrics endpoint
    └── tests/
        ├── Conquer.UnitTests/
        ├── Conquer.IntegrationTests/
        └── Conquer.StressTests/
```

---

## 3. Naming & Style Conventions

| Rule | Convention |
|------|-----------|
| **Namespace root** | `Conquer.*` — never `World` as a namespace or type (OPUS §12) |
| **Original names** | Keep in `// Original: CMapGroup::OnTimer` comments for traceability |
| **.NET names** | Idiomatic PascalCase: `MapGroupLoop`, `SocketKernel`, `WorldLoop` |
| **Nullable** | `<Nullable>enable</Nullable>` everywhere |
| **Warnings** | `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` |
| **Logging** | `[LoggerMessage]` source-generated only — no string interpolation in log calls |
| **Metrics** | `System.Diagnostics.Metrics` — `Meter("Conquer.{Component}")` |
| **Buffers** | `ArrayPool<byte>.Shared` — document ownership in XML doc on the type |
| **DB** | Async via dedicated DB worker per group; synchronous only for startup loading (OPUS §12) |
| **File encoding** | UTF-8 with BOM for `.cs`, UTF-8 no BOM for everything else |

---

## 4. Architecture Rules (from OPUS §4.2, §2.1)

1. **Single owner:** Only the owning loop touches game state. No locks in game logic.
2. **Socket thread copies:** Never touches game objects. Strips trailer, copies packet, posts to owning loop.
3. **Transfer protocol:** Old group removes player, flushes output → `router.Enter(dest, ...)` posts `PlayerArrived` FIRST, rebinds socket SECOND. Short-lived forward table for gap packets.
4. **Disconnect:** `router.OnDisconnect` posts `PlayerLeft`; old group forwards if player already moved.
5. **Position:** Changes ONLY through `GameMap.Move`.
6. **Packet validation:** Validate every length before reading; 3 bad packets → kick.
7. **No `await` in handlers.** DB/IO runs elsewhere, results posted back as messages.
8. **Brain/body split:** Brain keeps its own world copy from events, sends commands. Body re-validates everything.
9. **Monster respawn:** One due-queue per group, independent of map activity.
10. **AI hysteresis:** Aggro range < lose range; state-specific think intervals.

---

## 5. Timing Constants (exact, from original source — OPUS §3.3)

```csharp
public static class TimingConstants
{
    public const int MapGroupOnTimerMs     = 500;
    public const int SocketKernelOnTimerMs = 1000;
    public const int WorldKernelOnTimerMs  = 1000;
    public const int SocketProcessIntervalMs = 40;
    public const int ThreadDelayMs         = 400;
    public const int TimerOffsetLimitSecs  = 10;
    public const int CloseThreadMs         = 30000;
    public const int NpcSystemOnTimerMs    = 500;
    public const int MaxNpcPerOnTimer      = 20;
    public const int ThinkSecs             = 1;
    public const int DormancySecs          = 60;
    public const int CellsPerBlock         = 18;
    public const int CellsPerView          = 18;
}
```

---

## 6. Inter-Thread Message IDs (keep original names — OPUS §3.2)

Port these as an enum in `Conquer.Shared/Messages/InternalMessageId.cs`.  
Each message is a typed struct sent through `Channel<T>`.

---

## 7. Delivery Phases (OPUS §11)

| Phase | Scope | Status |
|-------|-------|--------|
| **1** | Shared + Net + Runtime (DI, config, metrics, loops, tests 1-8) | ✅ DONE |
| **2** | Account Server + World login flow + DB layer (test 9) | ✅ DONE |
| **3** | MapGroup core: maps, movement, view/broadcast, transfers, heartbeat | ✅ DONE |
| **4** | Combat: auto-attack, damage, death, drops, PK/crime | ✅ DONE |
| **5** | NPC Server: AI, generators, dormancy, pathfinding, transport | ✅ DONE |
| **6** | Items, equipment, shops/vendors, packages | ✅ DONE |
| **7** | Magic, status effects | ⬜ |
| **8** | Team, trade/booth, chat rooms, syndicates, GameAction engine | ⬜ |
| **9** | Hardening: soak test, tuning, docs, DEVIATIONS.md | ⬜ |

---

## 8. Current Progress Log

### Phase 1 — Completed
**Agent:** Antigravity  
**Date:** 2026-10-10  
**Work done:**
- Built `Conquer.Shared` (constants, message IDs, packet structs, wire framing)
- Built `Conquer.Net` (`SecuritySocket`, `IGameSocket`, `ICrypto`)
- Built `Conquer.Runtime` (`GameLoop`, `WorldRouter`, `GameConnection`, `MapGroupLoop`, `WorldLoop`, `WorldHost`)
- Wrote and passed Phase 1 tests (`SecuritySocketTests` checking loopback, stack overflow, malformed packets)

### Phase 2 — Completed
**Agent:** Antigravity  
**Date:** 2026-10-10  
**Work done:**
- Built `Conquer.Data` with Dapper and MySqlConnector for `cq_account` mapping.
- Implemented `MsgAccount`, `MsgLoginAccount`, `MsgConnect`, and Auth response packets in `Conquer.Shared`.
- Created and configured `Conquer.AccountServer` worker service to process auth and notify MsgServer via IPC.
- Implemented `WorldLoop` login pairing mechanism matching internal IPC auth with external client connections.
- Added comprehensive integration test (`LoginFlowTests`) covering the full login handshake. All tests passing.

### Packet Architecture Refactoring — Completed
**Agent:** Antigravity  
**Date:** 2026-10-10  
**Work done:**
- Replaced unsafe pointer structs and manual byte indexing with zero-allocation, safe `PacketReader` and `PacketWriter` abstractions (`ref struct`).
- Created `ConquerEncoding.GameEncoding` (Windows-1252) with `CodePagesEncodingProvider`.
- Added `[PacketHandler]` attribute and `PacketDispatcher<TContext>` for attribute-based packet routing.
- Converted `MsgAccount`, `MsgLoginAccount`, `MsgConnect`, and `MsgAuthResponse` into safe record structs with `Decode` and `Encode` methods.
- Refactored `AccountServerWorker`, `WorldLoop`, and `LoginFlowTests`.
- Disabled unsafe blocks across the solution (`<AllowUnsafeBlocks>false</AllowUnsafeBlocks>`).
- Added `PacketReaderWriterTests` covering round-trips and dispatching. All unit and integration tests passing.

### Phase 3 — Completed
**Agent:** Antigravity  
**Date:** 2026-10-11  
**Work done:**
- Ported direction constants and 8-way offset delta tables from `MapGroupKernel/I_Role.h` (`_DELTA_X`, `_DELTA_Y`) and Chebyshev distance from `GameMap.h`.
- Created safe zero-allocation packet models: `MsgWalk` (1005), `MsgAction` (1010, jump/disappear/change dir), `MsgTick` (1012), and `MsgPlayer` (1014) in `Conquer.Shared`.
- Created `MapModel` and `IMapRepository` / `MapRepository` in `Conquer.Data` mapping `cq_map`.
- Built 18x18 spatial grid partitioning: `CellMap` for collision/boundaries and `GameBlock` for spatial hashing and dormancy.
- Implemented `GameMap`: 9-block spatial observer tracking (`GetBlocksIn9Grid`), `Enter`, `Leave`, `Broadcast`, and atomic movement `Move(IWorldEntity, newX, newY)` computing enter/leave/common block differentials.
- Implemented `IWorldEntity` and `Player` in `Conquer.Runtime` tracking socket connection, level, life, look, and heartbeat timestamps.
- Implemented `MapGroupLoop`: single-owner execution of map entity movement, `MsgWalk` delta verification, `MsgAction` jump range check (`< 18`), `MsgTick` heartbeat, and `OnTimer` 500ms heartbeat dispatch (10s) and silent client kick (25s).
- Added comprehensive test suites: `MapSystemTests` (10 unit tests) and `MapGroupIntegrationTests` (2 integration tests). All 18 unit/integration tests passing.

### Phase 4 — Completed
**Agent:** Antigravity  
**Date:** 2026-10-11  
**Work done:**
- Created combat constants in `CombatConstants.cs` matching original values: `PkValueOneKill = 20`, `PkValueRedName = 21`, `PkValueBlackName = 101`, `CrimeKeepSecs = 30`, `DefaultAttackSpeedMs = 1000`, `DefaultMeleeRange = 1`, and `DieMode` enum.
- Created zero-allocation safe packet models in `CombatPackets.cs`: `MsgInteract` (1022) with `InteractType`, and `MsgMapItem` (1101) with `MapItemAction`.
- Extended `IWorldEntity` and `Player` with combat properties: `MinAtk`, `MaxAtk`, `Defense`, `AttackRange`, `AttackSpeedMs`, `TargetId`, `LastAttackTime`, `PkPoints`, `CrimeExpireTime`, `IsEvil`, and `IsAlive`.
- Created authoritative `Monster` entity in `Conquer.Runtime` with life, combat stats, dormancy, and silver drop configuration.
- Ported `BattleEngine` from `BattleSystem.cpp` (`CBattleSystem::CalcPower` / `CalcAttackPower` / level adjustments / minimum damage clamping).
- Implemented combat processing in `MapGroupLoop`:
  - `MsgInteract` (Attack/Shoot): position synchronization anti-cheat verification, target acquisition, crime check on intent.
  - Authoritative damage application and observer broadcast of attack, kill, and die packets.
  - Server-driven auto-attack repeating loop in `OnTimer` (500ms tick) paced by server attack speed.
  - Single-owner thread death resolution completely eliminating double-kill race conditions.
  - Monster death silver drop broadcast (`MsgMapItem`) and cleanup from map.
  - Murder PK addition (`PkValueOneKill = 20`) when killing innocent players.
  - Crime expiration handling in `OnTimer`.
- Added test suites: `CombatSystemTests` (9 unit tests) and `CombatIntegrationTests` (5 integration tests). Total tests across solution: 32/32 passing with 0 warnings.

### Phase 5 — Completed
**Agent:** Antigravity  
**Date:** 2026-10-11  
**Work done:**
- Ported C++ NPC wire protocol matching 8-byte header `[msgSize:2 LE][packetId:2 LE][npcId:4 LE][body...]` (`PROTOCOL_NPCMSG_HEADSIZE = 8` from `Share/protocol.h`).
- Created `MsgAiNpcInfo` (20001, 34 bytes) with `AiNpcAction` (`CreateNew = 1`, `Delete = 2`) for spawner synchronization.
- Created transport layer abstractions and implementations:
  - `INpcTransport` interface (`SendAsync`, `ReceiveAsync`, `CloseAsync`, `IsConnected`).
  - `ChannelNpcTransport` with `CreatePair()` for high-performance in-memory channel communication.
  - `TcpNpcTransport` and `TcpNpcListener` for TCP communication with automatic reconnect.
- Created `Conquer.NpcServer`:
  - `NpcMode` enum (Dormancy, Idle, Walk, Goto, Talk, Follow, Escape, Forward, Attack).
  - `NpcTypeData` template archetype attributes.
  - `NpcUser` player observer model built from server events.
  - `NpcBlock` 18x18-cell dormancy grid partitions.
  - `NpcWorldMap` 9-grid observer queries, dormancy sleep/wake tracking.
  - `NpcGenerator` spawner with `MaxNpc`, `MaxPerGen`, `RestSecs`, spatial boundaries, and budget-limited `Generator()` loop.
  - `NpcBrain` state machine: 10-analysis cycle per tick, `ProcessEscape`, `ProcessAttack`, `ProcessForward`, `ProcessIdle`, `DetectPath` (8-way pathfinding with direct step tie-breaking), and dormancy management.
  - `NpcWorld` simulation engine with C++ `CutTrail` adaptive step pacing for spawners (`MAXNPC_PER_ONTIMER = 20`), awake-only monster ticks, and incoming packet routing.
  - `NpcServerWorker` hosted background service.
- Integrated NPC server communications into `MapGroupLoop`:
  - Dispatches player enter, leave, move, jump, and death events over `INpcTransport`.
  - Authoritatively processes `KernelNpcMsg` packets (`HandleNpcWalk`, `HandleNpcInteract`, `HandleNpcSpawn`), strictly validating distance and line-of-sight/passability.
- Added comprehensive test suites:
  - `NpcSystemTests` (10 unit tests): wire framing, `MsgAiNpcInfo`, generator budget, 9-block dormancy, state machine transitions, paced attacks, dormancy timeouts, 8-way pathfinding, and `ChannelNpcTransport`.
  - `NpcIntegrationTests` (5 integration tests): TCP framing roundtrip on loopback, generator spawn budget with C++ adaptive pacing, dormancy lifecycle, MapGroup command validation & cheat rejection, and end-to-end combat/AI loop over channel transport.
- Total solution tests: 49/49 passing with 0 warnings and 0 errors.

### Phase 6 — Completed
**Agent:** Antigravity  
**Date:** 2026-10-11  
**Work done:**
- Created packet models in `Conquer.Shared/Messages/Packets/ItemPackets.cs`:
  - `ItemPosition` enum (Equipment slots 1..11: Helmet, Necklace, Armor, WeaponR, WeaponL, RingR, RingL, Shoes, Mount, Sprite, Mantle; Packages 50..53; Storage 201..204; Ground 254).
  - `ItemAction` enum (`Buy = 1`, `Sell = 2`, `Drop = 3`, `Use = 4`, `Equip = 5`, `UnEquip = 6`, `DropMoney = 12`, `DropEquipment = 18`, etc.).
  - `ItemInfoAction` enum (`AddItem = 1`, `Trade = 2`, `Update = 3`, `OtherPlayerEquipment = 4`, `Auction = 5`).
  - `MsgItem` (PacketType 1009, 20 bytes exact C++ binary layout `[len:2][type:2][id:4][dwData:4][usAction:2][pad:2][idTarget:4]`) with helpers and `Decode`/`Encode`.
  - `MsgItemInfo` (PacketType 1008, 40+ bytes exact C++ binary layout `[len:2][type:2][id:4][dwType:4][usAmount:2][usAmountLimit:2][ucAction:1][ucIdent:1][ucPosition:1][ucGem1:1][ucGem2:1][ucMagic1:1][ucMagic2:1][ucMagic3:1][nData:4][dwWarGhostExp:4][dwGemAtkType:4][dwAvailableTime:4][strings...]`).
- Created database entities and repositories in `Conquer.Data/ItemRepository.cs`:
  - `ItemModel` mapping `cq_item`.
  - `ItemTypeModel` mapping `cq_itemtype`.
  - `ShopGoodsModel` mapping `cq_goods`.
  - `IItemRepository` & `ItemRepository` (CRUD for `cq_item`).
  - `IItemTypeRepository` & `ItemTypeRepository` (item archetype templates).
  - `IShopRepository` & `ShopRepository` (NPC shop inventory).
- Created runtime systems in `Conquer.Runtime`:
  - `Item.cs`: In-memory item model, item sort/subtype/quality classification, `ResolveEquipPosition`, and `GetSellPrice()` formula ported from `Item.cpp:261` (1/3 base, durability scale, quality bonus, arrows unsellable).
  - `InventoryPackage.cs`: 40-slot backpack (`MaxCapacity = 40`), `TryAdd`, `Remove`, `GetItem`, `IsFull`.
  - `EquipmentManager.cs`: 11 equipment slots, requirement validation (level, profession, sex), item swap back to package, stat aggregation (`BonusMinAtk`, `BonusMaxAtk`, `BonusDefense`, `BonusLife`, `AttackRange`, `AttackSpeedMs`).
  - `ShopManager.cs`: Goods registry per shop NPC, `BuyItem` (verifies shop sells, checks price, bag capacity, deducts money, adds item), `SellItem` (checks bag, calculates sell price, credits money, removes item).
  - `Player.cs`: Integrated `Package`, `Equipment`, `Money`, base attributes, and `RecalculateStats()`.
  - `GroundItem.cs`: Ground item / money drop entity.
  - `GameBlock.cs` & `GameMap.cs`: Spatial ground item tracking, `AddGroundItem`, `RemoveGroundItem`, `GetGroundItem`.
  - `MapGroupLoop.cs`: Added `ShopManager`, `ItemTypeLookup`, `NextItemId()`, and message routing for `PacketType.Item` (Buy, Sell, Drop, DropMoney, Use/Equip, UnEquip) and `PacketType.MapItem` (Pick). Monster death drops registered as ground items.
- Added comprehensive test suites:
  - `ItemSystemTests` (9 unit tests): `MsgItem` and `MsgItemInfo` roundtrip framing, exact 40-item backpack capacity, equipment slot resolution, level/prof/sex requirements check, equipment stat aggregation and unequip, `Item.cpp:261` resale formula, and `ShopManager` buy/sell transactions.
  - `ItemIntegrationTests` (6 integration tests): End-to-end buying from NPC shop with silver deduction and `MsgItemInfo`, weapon equipping with stat recalculation and observer appearance broadcast, armor unequipping with stat reversion, selling items for silver, dropping and picking up items between players, and picking up monster death silver drops.
- Solution status: 64/64 tests passing (45 unit tests + 19 integration tests), 0 warnings, 0 errors.

### Phase 7 — Completed
**Agent:** Antigravity  
**Date:** 2026-10-12  
**Work done:**
- Created packet models in `Conquer.Shared/Messages/Packets`:
  - `MagicPackets.cs`:
    - `EffectRole` struct (`RoleId`, `Data`).
    - `MsgMagicEffect` (PacketType 1105, 20 bytes base header + 8 bytes per target `[len:2][type:2][idUser:4][targetId/posX,posY:4][usType:2][usLevel:2][ucDir:1][ucEffectNum:1][pad:2]` followed by `EffectRole[]`), with `CreateSingle`, `CreateArea`, `Decode`, `Encode`.
    - `MsgMagicInfo` (PacketType 1103, 16 bytes exact C++ layout `[len:2][type:2][idOwner:4][dwExp:4][usType:2][usLevel:2]`) with `Create`, `Decode`, `Encode`.
    - `MsgFlushExp` (PacketType 1104, 12 bytes exact C++ layout `[len:2][type:2][dwExp:4][usType:2][ucAction:1][pad:1]`) with `CreateMagicExp`, `Decode`, `Encode`.
  - `UserAttribPackets.cs`:
    - `UserAttributeType` enum (`Life = 0`, `MaxLife = 1`, `Mana = 2`, `MaxMana = 3`, `Money = 4`, `Exp = 5`, `Pk = 6`, `Profession = 7`, `Level = 13`, `KeepEffect = 26` (low 32 bits), `Xp = 28`, `KeepEffect2 = 36` (high 32 bits)).
    - `MsgUserAttrib` (PacketType 1017, 12 bytes base header + 8 bytes per attribute `[len:2][type:2][idUser:4][dwAttributeNum:4]` followed by `[ucAttributeType:4][dwAttributeData:4]`) with helpers `CreateLife`, `CreateMana`, `CreateXp`, `CreateStatus` (splits 64-bit status into low and high 32-bit attributes), `Decode`, `Encode`.
- Created database entities and repositories in `Conquer.Data/MagicRepository.cs`:
  - `MagicModel` mapping `cq_magic` table (`Id`, `OwnerId`, `Type`, `Level`, `Exp`, `Unlearn`, `OldLevel`).
  - `MagicTypeModel` mapping `cq_magictype` template archetype table (`Id`, `Type`, `Sort`, `Name`, `Crime`, `Ground`, `Multi`, `Target`, `Level`, `UseMp`, `Power`, `Intone`, `Percent`, `Step`, `Range`, `Distance`, `Status`, `StatusChance`, `NeedProfMask`, `NeedExp`, `NeedLevel`, `UseXp`, `WeaponSubtype`, `Delay`).
  - `IMagicRepository` & `MagicRepository` (CRUD for `cq_magic`).
  - `IMagicTypeRepository` & `MagicTypeRepository` (template registry for `cq_magictype`, synchronous in-memory `Get(type, level)` and async queries).
- Created runtime systems in `Conquer.Runtime`:
  - `StatusManager.cs`:
    - `KeepEffectFlags` 64-bit flags bitmask matching `KEEPEFFECT_*` (`Normal`, `TeamLeader`, `Die`, `Ghost`, `Crime`, `Red`, `DeepRed`, `Poison`, `Hidden`, `Freeze`, `SuperSoldier`, `Defense1`, `Attack`, `AtkSpeed`, `MagicDamage`, `MagicDefense`, `Reflect`, `ReflectMagic`, `Slowdown1`, `XpFull`, `Fly`, `Cyclone`).
    - `StatusEffectType` enum matching `STATUS_*` from `I_Role.h`.
    - `StatusEffect` tracking power, duration, expiration, and periodic tick intervals.
    - `StatusManager` class with `AttachStatus`, `DetachStatus`, `HasStatus`, `GetStatus`, `OnTimer` (handles periodic ticks e.g. poison and expires timed effects).
  - `MagicManager.cs`:
    - `MagicSort` enum (`Attack = 1`, `Recruit = 2`, `Cross = 3`, `Fan = 4`, `Bomb = 5`, `AttachStatus = 6`, `DetachStatus = 7`, `Square = 8`, `Line = 14`, etc.).
    - `LearnedMagic`: manages spell level, exp progression, and level-ups.
    - `MagicManager`: spellbook registry, per-spell cooldown tracking, and XP accumulation / consumption (up to 100 with `XpFull` trigger).
  - `IWorldEntity.cs`: Added `Mana`, `MaxMana`, `MagicDef`, and `StatusManager Statuses`.
  - `Player.cs`: Integrated `Mana`, `MaxMana`, `MagicDef`, `Magics`, `Statuses`, and updated `RecalculateStats()` to factor in active buffs (`Superman`, `Cyclone`, `Shield`, `SuperAtk`, `AtkSpeed`, `MagicDefense`) and status flags in `CreateSpawnPacket()`.
  - `Monster.cs`: Integrated `Mana`, `MaxMana`, `MagicDef`, `Statuses`, and updated `CreateSpawnPacket()`.
  - `BattleEngine.cs`: Added `CalculateMagicDamage(attacker, target, magicPower)` formula with level delta adjustments and status multipliers (`Superman` 10x vs monsters / 3x vs players, `Cyclone` 2x, `Shield`/`Defense1` 30% damage reduction). Physical `CalculateDamage` updated with Flying target immunity to melee (`AttackRange <= 1`).
  - `MapGroupLoop.cs`:
    - Extended `HandleInteract` with `InteractType.MagicAttack` and `InteractType.AbortMagic`.
    - Implemented `ProcessMagicAttackRequest` checking alive state, learned spell, cooldown, mana, and XP requirements.
    - Implemented `ExecuteMagicSingleAttack` (`MagicSort.Attack`) with range/line-of-sight check, direction facing, crime detection, damage calculation, HP deduction, and `MsgMagicEffect` broadcast.
    - Implemented `ExecuteMagicRecruit` (`MagicSort.Recruit`) for healing target HP, synchronizing life, and broadcasting `MsgMagicEffect`.
    - Implemented `ExecuteMagicAttachStatus` (`MagicSort.AttachStatus`) for applying buffs (`Superman`, `Cyclone`, `Shield`, `Fly`), updating `KeepEffectFlags`, recalculating stats, and broadcasting status.
    - Implemented `ExecuteMagicArea` (`MagicSort.Cross`, `Fan`, `Bomb`, `Square`, `Line`) resolving all monster/player targets within radius, applying damage, and broadcasting multi-target `MsgMagicEffect`.
    - Implemented `AwardSpellExp` awarding spell exp and sending `MsgFlushExp`/`MsgMagicInfo` on level-up.
    - Updated `OnTimer`: periodic poison ticks, status expiration, and timer-driven XP accumulation (+1 XP every 3s up to 100, triggering `XpFull`).
- Added comprehensive test suites:
  - `MagicSystemTests` (8 unit tests): `MsgMagicEffect` single and area encoding/decoding, `MsgMagicInfo` and `MsgFlushExp` framing, `MsgUserAttrib` life/mana/xp/status roundtrip, `StatusManager` bitmask updates, `StatusManager.OnTimer` expiration and poison ticks, `MagicManager` learning/cooldown/xp progression, `BattleEngine.CalculateMagicDamage` multipliers/shield reduction, and flying target melee immunity.
  - `MagicIntegrationTests` (5 integration tests): single-target Thunder attack with MP deduction, monster damage, and `MsgMagicEffect` broadcast; Cure spell healing and MP deduction; Shield buff attaching `Defense1` and recalculating defense; Superman XP skill activation consuming full 100 XP and attaching `SuperSoldier`; and AoE FireCircle bomb damaging multiple monsters in radius.
- Solution status: 79/79 tests passing (55 unit tests + 24 integration tests), 0 warnings, 0 errors across all 11 projects under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

### Phase 8 — Completed
**Agent:** Antigravity  
**Date:** 2026-10-12  
**Work done:**
- Created packet models in `Conquer.Shared/Messages/Packets/SocialPackets.cs`:
  - `ChatChannel` enum (`Normal = 2000`, `Private/Whisper = 2001`, `Action = 2002`, `Team = 2003`, `Syndicate = 2004`, `System = 2005`, `Broadcast = 2008`, `Talk = 2011`, etc.).
  - `MsgTalk` (PacketType 1004, 16-byte base header `[len:2][type:2][dwWordsColor:4][usChannel:2][usStyle:2][dwTime:4]` + 4 packed length-prefixed strings: `Speaker`, `Hearer`, `Emotion`, `Words`).
  - `TeamAction` enum (`Create = 0`, `ApplyJoin = 1`, `LeaveTeam = 2`, `AgreeJoin = 3`, `Invite = 4`, `AgreeInvite = 5`, `Dismiss = 6`, `KickOut = 7`, `CloseTeam = 8`, `OpenTeam = 9`).
  - `MsgTeam` (PacketType 1023, 12 bytes exact C++ binary layout `[len:2][type:2][unAction:2][pad:2][idPlayer:4]`).
  - `TeamMemberAction` enum (`AddMember = 0`, `DelMember = 1`).
  - `TeamMemberRecord` record (`Name`, `Id`, `LookFace`, `MaxHp`, `Hp` — 28 bytes per member).
  - `MsgTeamMember` (PacketType 1026, 8-byte base header `[len:2][type:2][ucAction:1][ucAmount:1][pad:2]` + up to 5 member records).
  - `TradeAction` enum (`Apply = 1`, `Quit = 2`, `Open = 3`, `Success = 4`, `False = 5`, `AddItem = 6`, `AddMoney = 7`, `MoneyAll = 10`, `Ok = 11`, `AddItemFail = 12`).
  - `MsgTrade` (PacketType 1056, 12 bytes exact C++ binary layout `[len:2][type:2][data:4][usAction:2][pad:2]`).
  - `SyndicateAction` enum (`ApplyJoin = 1`, `InviteJoin = 2`, `Leave = 3`, `KickOutMember = 4`, `QuerySynName = 6`, `DonateMoney = 11`, `QuerySynAttr = 12`, `SetSyn = 14`, `Demise = 116`).
  - `MsgSyndicate` (PacketType 1107, 16 bytes base header `[len:2][type:2][usAction:2][pad:2][idTarget:4][idFealty:4]` + optional string).
  - `MsgSynInfo` (PacketType 1114, 56 bytes exact layout `[len:2][type:2][idSyn:4][idFealty:4][dwFund:4][dwPopulation:4][ucRank:1][ucSaint:1][pad:2][szName:16][szLeader:16]`).
  - `FriendAction` enum (`Apply = 10`, `Accept = 11`, `Online = 12`, `Offline = 13`, `Break = 14`, `GetInfo = 15`).
  - `MsgFriend` (PacketType 1019, 32 bytes exact layout `[len:2][type:2][idFriend:4][dwLookFace:4][ucAction:1][ucOnline:1][ucLevel:1][pad:1][szName:16]`).
- Created database entities and repositories in `Conquer.Data`:
  - `SyndicateRepository.cs`: `SyndicateModel` (`cq_syndicate`), `SyndicateAttrModel` (`cq_synattr`), `ISyndicateRepository` & `SyndicateRepository` (Dapper async CRUD).
  - `FriendRepository.cs`: `FriendModel` (`cq_friend`), `IFriendRepository` & `FriendRepository` (Dapper async CRUD).
- Created runtime systems in `Conquer.Runtime`:
  - `Team.cs`: `Team` and `TeamManager` (max 5 members, leader controls, close/open team toggles, exp sharing within 32-cell `ExpShareRange` with active member level scaling).
  - `TradeSession.cs`: `TradeSession` and `TradeManager` (secure 2-phase commit state machine, max 10 items, staged silver, distance validation <= 18 cells, atomic commit swap, abort rollback).
  - `Syndicate.cs`: `Syndicate` and `SyndicateManager` (hierarchy `Member`, `TeamLeader`, `Manager`, `HonoraryManager`, `DeputyLeader`, `Leader`, silver donations, allies, enemies, join/leave/kick).
  - `FriendManager.cs`: In-memory friend relationships, bilateral tracking, online/offline status event notifications.
  - `PacketReader.cs`: Added framing guard ensuring declared packet length trims wire trailer without allocating or overflowing string parsers.
  - `MapGroupLoop.cs`: Integrated `TeamManager`, `TradeManager`, `SyndicateManager`, and `FriendManager`. Added packet handlers and message dispatch for `Talk` (Normal local view broadcast, Whisper direct echo, Team broadcast, Syndicate broadcast), `Team` (Create, ApplyJoin, Leave, Kick, Dismiss), `Trade` (Apply handshake, AddItem, AddMoney, Ok 2-phase commit, Quit abort), `Syndicate` (ApplyJoin, DonateMoney, Leave, Kick, Query), and `Friend` (Apply/Accept, Break, GetInfo). Hooked friend online/offline notifications into `AddPlayer`/`RemovePlayer`, and hooked team exp sharing into monster death in `ExecuteDeath`.
- Added comprehensive test suites:
  - `SocialSystemTests` (12 unit tests): `MsgTalk`, `MsgTeam`, `MsgTeamMember`, `MsgTrade`, `MsgSyndicate`, `MsgSynInfo`, `MsgFriend` binary roundtrips with trailer wire framing; `Team` 5-member capacity and leader removal; `TradeSession` 2-phase atomic swap; `TradeSession` abort rollback; `Syndicate` donations and rank hierarchy; `FriendManager` bidirectional relationships and online notifications.
  - `SocialIntegrationTests` (4 integration tests): Local talk broadcast (within 18 cells) and direct whisper routing with echo; Team formation, invite acceptance, and nearby monster kill exp sharing; End-to-end 2-phase trade atomic exchange (item for silver); Guild creation and silver donation with updated `MsgSynInfo` broadcast.
- Solution status: 95/95 tests passing (67 unit tests + 28 integration tests), 0 warnings, 0 errors across all 11 projects under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

### Phase 9 — Next Up
**Where to continue:**
- Phase 9: NPC scripting, mining, quests, packaging, stress testing, and final polish.
- Lua or C# scripting for NPCs (`cq_npc`, `cq_dynanpc`), task system (`cq_task`), mining system, server launch composition, client compatibility validation, stress tests under simulated concurrent load.



---

## 9. Rules for Any Agent

1. **Read `OPUS.md` sections 2-4 and 12 before any implementation decision.**
2. **Never guess a packet layout.** Read from the original source or ask Tom.
3. **Cite original file and function** in a comment when porting a rule: `// Original: MapGroupKernel/User.cpp CUser::OnTimer`
4. **Log deviations** in `DEVIATIONS.md` with reason.
5. **Keep this file updated** — update the Progress Log when you stop working.
6. **Run `dotnet build` and `dotnet test` before declaring anything done.**
7. **No `new byte[]` on hot paths** — use `ArrayPool`, `stackalloc`, `Span<byte>`.
8. **Every handler wrapped in try/catch** — a bad packet must not stop a loop.
9. **Measure: log SLOW > 20ms, track Backlog/Busy%/MaxHandleMs per loop.**
10. **Follow the phase order.** Don't skip ahead. Each phase ends green and committed.
