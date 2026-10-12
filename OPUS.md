# PROJECT BRIEF: Conquer Online classic server, faithful port to .NET 10 x64

You are the lead engineer. Build the **whole server stack** (Account Server -> Game Server -> Npc Server) in **.NET 10 / C# 14**, as a faithful port of the original C++ architecture (2003, still the model behind current servers), using every modern .NET feature that helps. The owner (Tom) is an experienced game-server developer (C#/C++, networking, anti-cheat). Be direct and technical. Do not ask for permission on decisions listed in section 12; just follow the defaults.

## 0. Attach these files to this conversation

1. `my_server.7z`: the ORIGINAL C++ source (reference only, see rule 2.2).

## 1. Mission and non-goals

Mission: a complete, runnable, tested server that behaves like the original: same process/thread model, same message flow, same timers, same game rules, on .NET 10.

Non-goals: new gameplay, new client protocol, a GUI shell (use a console/host + metrics endpoint), billing (stub the fee messages).

## 2. Ground rules

2.1 **Fidelity first, modernization second.** Keep the original *architecture, message flow, timers and rules*. Modernize the *implementation* (pools, channels, spans, DI, source generators). Any deviation from the original behavior must be listed in a `DEVIATIONS.md` with the reason.

2.2 **Do not re-derive the architecture from the source.** Section 3 is a verified digest. Open the original source only to port *specific rules or packet layouts* (item/magic/trade/team/syndicate formulas, structs, table schemas). Cite the original file and function in a comment when you port a rule.

2.3 Everything in section 3 marked **(unverified)** or listed in 3.12 was NOT read. Read the original for those parts before implementing them.

2.4 Never guess a packet layout. If a layout is not in this brief, read it from the original source (`Share/networkdef.h`, `Share/protocol.h`, `MapGroupKernel/Network/*`, `NpcKernel/NetWork/*`, `WorldKernel/NETWORK2/*`) or from Tom's existing code.

## 3. VERIFIED KNOWLEDGE OF THE ORIGINAL SERVER

### 3.1 Processes and threads

```
Account server (separate, source NOT provided: you design it, see 6.1)
        |  TCP (game server connects to it as a CLIENT)
MsgServer.exe (the game server), one process, threads:
  Shell        GUI dialog: prints text, shows stats (port 0)
  Socket       ALL network I/O, encryption, sends (port 1)
  World        login/logout, user list, chat rooms, syndicates (world-level), talks to account server (port 2)
  MapGroup x N each owns a set of maps and ALL players/monsters/items in them (ports 3..3+N-1)
  DB           housekeeping thread (details unverified)
NpcServer.exe (separate process, ONE thread): all monster AI. It CONNECTS to MsgServer as a TCP client.
```

N = `MAPGROUP_SIZE` from `config.ini` `[System]`. Other config: `[InternetPort]` `CURRENT_PORTID`, `PORT_SIZE`, `MASTER_IP`, `MASTER_PORT`, `LOGIN_KEY` (inter-process port set, details unverified); `shell.ini` `[AccountServer]` `SERVERNAME`.

Port ids: `MSGPORT_SHELL=0, MSGPORT_SOCKET=1, MSGPORT_WORLD=2, MSGPORT_MAPGROUP_FIRST=3`.

### 3.2 Inter-thread messaging (the core rule)

Threads share NOTHING. They exchange typed messages through per-thread queues (`MessagePort`): `Send(portTo, packetId, varType, data)`. A thread drains its own queue. This is what you port to `Channel<T>` (one per thread, single reader).

Message ids seen (keep the names, they document intent):

```
SOCKET_*   : CLOSESOCKET, SETPROCESSID, SENDCLIENTMSG, SENDNPCMSG, SETNPCSOCKETID, SETNPCPROCESSID,
             BREAKCONNECT, BREAKALLCONNECT, CHANGECODE
WORLD_*    : SETPROCESSID, TRANSMITMSG, USERNAMEMSG, USERIDMSG, LEVELUP, QUERYFEE, CLOSESOCKET, SENDTIME
MAPGROUP_* : BROADCASTMSG, TRANSMITMSG, REMOTECALL, RPC, SOCKETUSERINFO, SENDOBJINFO, CHANGEMAPGROUP,
             CHANGETEAM, CHANGENPC, DELTRANSNPC, SETMAPSYNID
KERNEL_*   : CLIENTMSG, NPCMSG, CLOSEKERNEL, CREATESYNDICATE, DESTROYSYNDICATE, COMBINESYNDICATE, CHANGESYNDICATE
SHELL_PRINTTEXT
```

### 3.3 Timing constants (exact values from the source)

```
MAPGROUP_ONTIMER_MS      = 500    map group OnTimer period
SOCKETKERNEL_ONTIMER_MS  = 1000   socket kernel OnTimer period
WORLDKERNEL_ONTIMER_MS   = 1000
SOCKET_PROCESS_INTERVAL  = 40     socket polling period (ms)
THREADDELAY_MS           = 400    max wait for an internal message
TIMER_OFFSET_LIMIT       = 10     seconds; if the clock drifts more, the next-tick time is reset (auto-correct)
CLOSETHREAD_MILLISECS    = 30000  wait for thread shutdown
NpcServer SYSTEM_ONTIMER_MS = 500
MAXNPC_PER_ONTIMER       = 20     monsters spawned per NPC tick (generator budget, adaptive step)
THINK_SECS               = 1      idle monster scans surroundings at most once per second
DORMANCY_SECS            = 60     no player nearby for 60 s -> monster sleeps
CELLS_PER_BLOCK          = 18     map is divided in 18x18-cell blocks ("rooms")
CELLS_PER_VIEW           = 18     view distance
```

### 3.4 Socket kernel (the only thread that touches the network)

Loop: drain internal messages -> `OnTimer` every 1 s -> `ProcessSocket()` every 40 ms.

Cases of `ProcessMsg` that matter:
- `SOCKET_SENDCLIENTMSG {idSocket, idPacket, buf, size}`: build the net packet (`UnitePacket`), then `SendPacket`: **copy the buffer, encrypt the copy (stateful per-connection cipher), one `send()` per packet, no batching.** If `send` does not accept all bytes, **close the connection** ("SNDBUF overflow"). There is no growing queue: a client that cannot keep up is disconnected.
- `SOCKET_SENDNPCMSG {idNpc, idPacket, buf, size}`: same, but to the NPC server socket (`m_idNpcSocket`), framed with the NPC id (`UniteNpcPacket`). NPC socket gets bigger buffers (`SOCKET_NPCBUFSIZE`).
- `SOCKET_SETPROCESSID`: records which kernel (World or a MapGroup) owns a socket; incoming client packets are routed there (`KERNEL_CLIENTMSG`).
- `SOCKET_BREAKCONNECT/BREAKALLCONNECT`, `SOCKET_CLOSESOCKET`.

**Important consequence:** MapGroup threads NEVER call `send()` themselves. They post a message to the Socket thread. In .NET this means: no encryption and no syscalls on a MapGroup loop (see 4.3).

### 3.5 World kernel and login (verified flow)

1. Account server authenticates the player and sends the game server `_MSG_LOGIN_A { idAccount, dwCheckData, usVersion, szUserName }` (struct size must equal `unMsgSize`). World stores a pending `CConnectUser`.
2. The client connects to the game server and sends `_MSG_CONNECT { idAccount, dwData, szInfo }`. World matches `(idAccount, check data)`. Whichever of the two arrives first is stored; the second completes the pairing. Wrong check data -> the pending entry is deleted.
3. If the account is already online: `KickOutAccount(idAccount, "login again")` and the new login is refused this round.
4. `UserList()->LoginUser(idAccount, idSocket, info)` returns `LOGIN_OK | LOGIN_NEW | LOGIN_BAN`. BAN = out of balance (message "insufficient balance"), NEW = no character yet (client is told to create one). The character is loaded from the DB.
5. `LoginToMapGroup(player)`: the player is handed to the MapGroup that owns his map; the socket is re-bound (`SOCKET_SETPROCESSID`). Logout runs in reverse (`KERNEL_CLOSEKERNEL` -> World).
6. Handoff rule from the original ReadMe: when a player moves between kernels, **the client must wait for the server's reply before sending more**, otherwise messages can be lost.
7. Other account-server messages: `_MSG_FEE_A { idAccount, ucType }`, `_MSG_QUERYFEE_A { idAccount, dwData, nTime, ucType }` (billing; stub).

### 3.6 MapGroup kernel (verified)

**Thread loop:** receive a message (if any) -> `ProcessMsg`; if now >= nextTimer -> `OnTimer` (every 500 ms, with the clock auto-correct above); then wait for a message up to `min(400, 500)` ms. A message handler runs inside try/catch; `DEADLOOP_CHECK(PID, "...")` markers wrap handlers to detect stuck loops. The group logs/asserts if the periodic `ProcessEvent` takes more than 1000 ms.

**Map ownership:** each map row in DB table `cq_map` has `idxserver` and `mapgroup`. A group loads `SELECT * FROM cq_map WHERE (idxserver=-1 || idxserver=<serverId>) && mapgroup=<groupId>`. Maps never move between groups at runtime; players do (`MAPGROUP_CHANGEMAPGROUP`, `ST_CHANGEMAPGROUP`).

**Dynamic object ids never collide across groups:** pets, dynamic NPCs, traps and call-pets use per-group id counters: first id = `(X_FIRST - 1) + groupId`, next id = previous + number of groups.

**`CMapGroup::OnTimer`** calls, in order: `RoleManager.OnTimer`, `MapManager.OnTimer`, `UserManager.OnTimer`, `LeaveWord.OnTimer`, `ProcessEvent` (when its own timer fires), `MercenaryTask.OnTimer`.

**`CUser::OnTimer`** (per player, every 500 ms tick) does, among other things: server heartbeat to the client every `TICK_SECS` and **kick if no client heartbeat for 2.5 x TICK_SECS** (anti speed-hack); syndicate online-time accounting; `ProcessAutoAttack`; XP timer; slow heal of life/mana; stamina regen; magic processing; team-status effects; status expiry; equipment durability; PK-value decay (faster in prison map); mining; team timer.

**Combat model (important):**
- The client sends ONE interact message to start fighting. `CMsgInteract::Process` validates (sender exists, not locked/frozen/fainted, alive, target exists), stands the pose, clears previous target, and for ATTACK/SHOOT on a user calls `SynPosition(x, y)`; **a position mismatch kicks the socket** (anti-cheat).
- The server stores the target (`SetAttackTarget`); then **`CUser::ProcessAutoAttack` repeats the attack from the timer**: `if (battleSystem.IsActived() && m_tFight.ToNextTick(fightRate + fightPause) && no active magic) -> ProcAttack_Hand2Hand()`. So attack rate is paced by weapon speed on the server, NOT by client packet rate.
- `CUser::Attack(target)`: `CBattleSystem::CalcPower` -> shield transfer -> `target.AddAttrib(LIFE, -loss, SYNCHRO)` -> `target.BeAttack(...)` -> crime/PK status (not in dead-island map, only against non-evil targets) -> equipment durability. `CUser::Kill/BeKill`, `CMonster::BeAttack/Kill/BeKill/DropItem` are the death and drop paths.
- A player's death/damage is resolved entirely inside the owning MapGroup, single-threaded, so double-kill races cannot exist.

**Monsters inside the game server (`CMonster`, `AINpc.cpp`):** the game server owns the authoritative monster (life, death, drops, scripted actions, its own `OnTimer`). The NPC server only DECIDES actions.

**NPC messages:** `KERNEL_NPCMSG {idNpc, idPacket, buf, size}` -> `ProcessNpcMsg` -> `CNetMsg::CreateNpcMsg(...)->Process(kernel)`. **The same message classes (`CMsgInteract`, `CMsgAction`, ...) process both player and monster actions**; `RoleManager()->QueryRole(msg, idSender)` resolves a user or a monster. So a monster action passes the same validation as a player action.

**Broadcast (`CRole::BroadcastRoomMsg(msg, bSendSelf)`):** every role keeps an incrementally maintained set of the roles that can see it (`m_setBCRole`, view 18 cells). A broadcast iterates that set and sends to players; `bSendSelf` adds the sender. Messages from users (and call pets/eudemons) are also forwarded to the NPC server so monsters can react; plain monsters do not forward (avoids loops).

**NPC broadcast rule (ReadMe):** to tell all monsters about something, send ONE message to the NPC server with `TARGET_NPC_ID = BROADCAST_NPC_ID`; the NPC server decides which NPCs receive it.

### 3.7 NPC server and monster AI (verified)

- One thread. `KernelThread::OnProcess`: if the socket to the game server is closed, **reconnect** (`Connect to game server again`); read every available packet (`GetPacket/SplitNpcPacket/ClearPacket`) and call `Kernel.ProcessMsg(idPacket, msg, size, idNpc)`; every 500 ms call `Kernel.OnTimer`.
- Object model (NpcServer ReadMe): global `CNpcWorld` -> `CMapManager` -> `CMap` { `CMapItem`, `CGameMap` (map data synced with the client), refs to `CNpc` and `CUser` }, `CNpcManager` -> `CNpc`, `CUserManager` -> `CUser`. The NPC server keeps **its own copy** of users and maps, fed by messages.
- `CNpcManager::OnTimer`: (1) spawn monsters from generators, at most `MAXNPC_PER_ONTIMER` per tick with an adaptive step; (2) loop over all NPCs and call `OnTimer()` on each one **except dormant monsters**; (3) agent generation on its own delay.
- **Dormancy:** `CGameBlock` has `m_bDormancy` (initially true). `CGameMap::SetDormancy(x,y)` returns false if any of the **9 blocks around** has a user, else marks the block dormant. `CallWakeMonster(x,y)` clears dormancy on the 9 blocks and wakes the monsters in them (called when a player enters/moves there). A monster re-checks dormancy every `DORMANCY_SECS`.
- `CNpc::OnTimer`: if dormant -> return; if dead/locked/inactive -> return; then up to **10 AI analyses per tick** over `m_nActMode`: `MODE_ESCAPE, MODE_ATTACK, MODE_FORWARD, MODE_IDLE` (+ `MODE_DORMANCY`). Each `ProcessX` returns true when it consumed the tick.
- `ProcessIdle`: guards/killers behave differently; if a path exists, `PathMove`; otherwise wait for the action timer (`m_tAction`, interval `max(ATKSPEED*3/1000, THINK_SECS)` seconds); then `FindNewTarget` (only if `IsActiveEnable`); target in attack range -> `MODE_ATTACK`; target seen but no path -> 80% stay idle else `MODE_ESCAPE`; else `MODE_FORWARD`.
- Movement is paced per monster: `m_tMove.ToNextTick(MOVESPEED or RUNSPEED)`; **at most one path step per 500 ms tick**. Path found by `FindPath` (algorithm unverified; read `NpcKernel/Npc.cpp`, `RoleMove.cpp`).
- Monster type data used: `NPCTYPEDATA_ATKSPEED, MOVESPEED, RUNSPEED, LIFE, MANA`.
- Monster -> game server messages are normal client-style packets, e.g. `CMsgAction`, `CMsgInteract` (walk/attack/die).

### 3.8 Database

Direct, **synchronous** MySQL calls (`mysql_query`) made from the MapGroup/World threads, one connection per kernel (`MyDB`, `MyDBMT`). Tables referenced so far: `cq_map` (and the many `cq_*` tables behind users, items, magic, monsters, generators, syndicates).

### 3.9 Wire protocol (client side)

- Packet header `[len:2][type:2]` little endian; packet id is the `ushort` at offset 2.
- Client -> server packets end with an 8-byte trailer; server -> client packets also carry an 8-byte trailer (Tom's code names the constant `TQ_CLIENT_SIGNATURE`; the server trailer is `"TQServer"`, confirm in his code). `len` EXCLUDES the trailer.
- Handshake: Diffie-Hellman key exchange handled inside `SecuritySocket` (`HandleDhKey`), then a stateful stream cipher via `ICrypto.Encrypt/Decrypt(ReadOnlySpan<byte>, Span<byte>)`. **Encryption is per packet, in order, one `Encrypt` call per packet.** Never assume the cipher is stateless.
- Rate limits in `SecuritySocket`: `ServerSocket.RecvPerSec / SendPerSec` per 100 ms window.

### 3.10 Server <-> NPC server framing

NPC packets carry the NPC id: `UniteNpcPacket(buf, max, idPacket, body, size, idNpc)` / `SplitNpcPacket(...)`. Same transport for events (game server -> NPC server) and actions (NPC server -> game server).

### 3.11 Account server contract (inferred from the game server's side)

Game server connects to it as a client with `LOGIN_KEY`. For each authenticated player it sends the game server `_MSG_LOGIN_A`; the player is told the game server IP/port and the check data; the player then sends `_MSG_CONNECT` to the game server (3.5).

### 3.12 NOT read (read the original before implementing)

Magic system details (`Magic*.cpp`), items and equipment (`User_Item.cpp`, `Item*.cpp`), `CBattleSystem` formulas, trade/booth/package, team, syndicate (large), `GameAction.cpp` (263 KB, the event/script action engine), `AiModule/` (rule-based AI helper), `MapData/` (map file format), `InternetPort`/`messageport` internals, `DBThread` internals, original `EncryptServer.h` (not needed if Tom's `ICrypto` is used), `FindPath` algorithm, the full `CMsg*` layouts, the `cq_*` schemas.

## 4. VALIDATED .NET DESIGN (reference code attached)

### 4.1 Mapping original -> .NET

| Original | .NET |
|---|---|
| MessagePort per thread | `Channel<T>` per loop, `SingleReader = true`, `AllowSynchronousContinuations = false` |
| MapGroup thread (+ OnTimer 500 ms) | `MapGroup`: one logical loop reading its Channel; a timer only POSTS a `Tick` message (coalesced); handlers never run in parallel |
| Socket thread | per-connection `SecuritySocket` (Tom's) + `WorldRouter` (socket id -> owning group) |
| World thread | `World` loop (Channel) |
| NpcServer thread | `AiWorker` (in-process) or a separate process with the SAME message contract |
| Dormancy by 9 blocks | brain-side grid, cells of 32, 3x3 cells, state `Idle` = not in the schedule queue |
| cq_map.mapgroup | `WorldHost`: dedicated groups + balanced shared groups, `MapDirectory` (lock-free reads) |
| DEADLOOP_CHECK | measure every message; log `SLOW` over a threshold |
| BroadcastRoomMsg | `GameMap.Broadcast` over the 3x3 cells (or the per-role observer set, see 12) |

### 4.2 Rules the reference code enforces (keep them)

1. **Single owner:** only the owning loop touches game state. No locks/atomics in game logic. Other threads send messages.
2. The socket thread only strips the trailer, **copies** the packet (receive buffer is reused), posts it. It never touches game objects.
3. **Transfer protocol:** old group removes the player and flushes his output, then `router.Enter(dest, ...)` posts `PlayerArrived` FIRST and rebinds the socket SECOND; the old group keeps a short-lived forward table so packets that arrive in the gap are forwarded, not dropped. Tested: 200,000 attacks while a player hopped groups, zero lost.
4. Disconnect: `router.OnDisconnect` posts `PlayerLeft`; if the player already moved, the old group forwards `PlayerLeft`.
5. Entity position changes ONLY through `GameMap.Move` (keeps grid cells in sync).
6. Validate every packet length before reading; count bad packets, kick after 3 (once, `Kicked` flag).
7. Never `await` inside a handler. DB and other I/O run elsewhere and POST the result back.
8. **Brain/body split:** the brain keeps its own world copy built only from events (`EntityEntered/Moved/Left`, `MapDropped`) and sends commands (`MonsterMove`, `MonsterAttack`). The body RE-VALIDATES every command (monster alive, target alive, in range, step <= 1 cell) because the brain's view is stale by design. The brain updates a monster's position only from the body's `EntityMoved`.
9. Monster respawn: one due-queue per group, independent of whether the map is active; a dropped (dynamic) map silently discards pending respawns; the brain is told with `EntityEntered` when a monster returns.
10. Hysteresis in AI: aggro range < lose range; each state (Idle/Chase/Attack) has its own think interval.

### 4.3 Tom's `SecuritySocket`: facts and the fixes already applied (tested for real)

The original class was compiled and run on loopback (with stubs for his `Time32/ICrypto/ReceiveBuffer/ServerSocket/World`) against a stateful cipher. Findings and fixes in `SecuritySocket.patched.cs`:

- **Stack overflow (reproduced):** `BeginSend` can complete synchronously and call `SendCallback` inside itself, so `SendAsync -> BeginSend -> SendCallback -> SendAsync` nests once per queued item. A 50,000-packet backlog crashed the original process. **Fix:** when `result.CompletedSynchronously`, hop with `Task.Run(socket.SendAsync)`.
- **`AllowSynchronousContinuations` on the send channel was `true`:** a writer's `TryWrite` ran encryption and the syscall inline on the writer's thread (the map group). `Send` cost the caller 3.4 us. **Fix:** `false` -> 0.47 us.
- **`Channel.Reader.Count` throws `NotSupportedException` on a SingleReader unbounded channel.** `PendingSends` is counted by hand (`Interlocked` increment before `TryWrite`, decrement after the read).
- `_startSend` was check-then-set; replaced by `Interlocked.CompareExchange` (could NOT be reproduced in 400 rounds on a 1-core box, kept as a theoretical fix).
- `SendBatch(ReadOnlySpan<byte> wholePackets)` added: queue item `SendItem(byte[] Buf, int Len)` (`Len == 0` = one packet, length from its header + 8; `Len > 0` = a batch). The send loop encrypts **each packet of a batch with its own `Encrypt` call, in order**, so cipher state advances exactly as before. Batches are capped at 60,000 bytes (`_sendLen` is a `ushort`) and split on packet boundaries; broken framing disconnects.
- `Send` copies the packet into its own pooled buffer and encrypts the COPY, so one broadcast packet can safely be shared by all observers.
- `IGameSocket` (Send, SendBatch, Disconnect, PendingSends) is the only thing the world needs from a socket. `GameConnection.Attach(...)` wires `Player.Send/SendBatch/Kick`, strips the 8-byte trailer, and kicks a client whose `PendingSends` exceeds a limit (default 10,000; the original also disconnects slow clients).
- Remaining caveats in his class: `Disconnect` invokes its callback while holding a lock (safe only because the world callback just posts a message); `_firstSend` special case writes `_sendLen` from the caller's thread (batches clear it); `ArrayPool.Return(buf, true)` clears every buffer (consider `false` for packet buffers).

### 4.4 Measured numbers (1 core sandbox, shared by load generator; treat as lower bounds, not guarantees)

- One group, one hot map, broadcast per attack: ~141k attacks/s at 50 observers, ~34k at 200, ~6k at 1000 (cost of copy+enqueue only).
- AI + group, 2000 players moving (~6,700 moves/s) + 5000 monsters: AI brain 3-7% of a core, group 15-20%, zero backlog, worst message 2-5 ms. In crowded "hunting grounds" (100 players and 250 monsters per spot) the load is dominated by FAN-OUT: ~2.25M packets/s at 100 ms monster pacing. **The original paces monsters at 500 ms, ~5x less**, and sends one `send()` per packet from a single socket thread, so it never carried that load either. The optional outbox (4.5) turned 2.25M packets/s into ~40k real sends/s.
- NOT measured: pathfinding cost, real network, 1000 players in one view, GC pauses under real load, the user's hardware.

### 4.5 Optional outbox (implemented, default OFF to match the original)

`Player.Queue/FlushOut`: an isolated packet leaves immediately; during a burst packets collect per player and leave in ONE `SendBatch` at the next group tick (<= 50 ms). `Player.FlushWindowMs = 0` disables batching (every packet goes out at once, like the original). Keep it, keep it OFF by default, expose it in config.

## 5. TARGET SOLUTION LAYOUT

```
ConquerServer.sln
  src/
    Conquer.Shared/        packet structs, ids, constants, Wire framing, pooled buffers, message contracts
    Conquer.Net/           SecuritySocket (patched), IGameSocket, listener, rate limits
    Conquer.Runtime/       Channel loops, WorldRouter, WorldHost, MapGroup base, timers, metrics
    Conquer.Data/          DB access (EF Core 10 or Dapper), repositories, cq_* mappings, migrations
    Conquer.AccountServer/ auth + game server registry + login hand-off
    Conquer.GameServer/    World loop, MapGroup kernel, game rules (combat, items, magic, team, syndicate, scripts)
    Conquer.NpcServer/     NpcWorld, managers, AI, pathfinding, client of the game server
    Conquer.Host/          generic host composition, config, logging, metrics endpoint
  tests/
    Conquer.UnitTests, Conquer.IntegrationTests (loopback), Conquer.StressTests
```

NpcServer transport: **abstraction `INpcTransport`** with two implementations: TCP (default, same as the original: NpcServer is a separate process that connects to the game server and reconnects automatically) and in-process Channels (tests and single-box deployments). Same message contract in both.

## 6. COMPONENT REQUIREMENTS

### 6.1 Account Server
Accepts game-server connections (shared `LOGIN_KEY`, registry of servers by name). Authenticates players, issues `idAccount` + check data, sends `_MSG_LOGIN_A` to the chosen game server, tells the client IP/port + data. Handles `_MSG_FEE_A/_MSG_QUERYFEE_A` as stubs. Password hashing: modern (Argon2id or PBKDF2), never plain. Rate-limit and lock out brute force. Reuse Tom's existing client-facing login protocol and crypto exactly (take it from his socket code).

### 6.2 Game Server
- **Socket layer:** `SecuritySocket` per connection; `WorldRouter` (socket id -> loop). Listener with backlog and accept loop; per-IP connection cap.
- **World loop:** login pairing (3.5), duplicate-login kick, user list, chat rooms, world-level syndicate data, account server client with reconnect.
- **MapGroup loops:** N from config; maps assigned from `cq_map.mapgroup` (+ `idxserver`); dedicated groups from config for busy maps (guild war, event maps); the rest balanced by weight; per-group id counters (3.6); transfers (4.2.3); all game rules.
- **Game rules:** port from the original source, one subsystem at a time (see phases). Server-driven auto-attack timer (3.6), position sync check, client heartbeat check.
- **Monsters:** authoritative objects in the group; accept `MonsterMove/MonsterAttack/...` commands with full re-validation; drops, death, respawn queue.

### 6.3 Npc Server
Loop (500 ms tick) over awake monsters only; generator spawn budget `MAXNPC_PER_ONTIMER`; dormancy by 9 blocks of 18 cells (use the original's numbers in the port; the reference code uses 32-cell grid, align it to 18 for fidelity or keep 32 and document); modes ESCAPE/ATTACK/FORWARD/IDLE; per-monster action/move timers from monster type data; path finding ported from the original; world copy built from events; reconnect to the game server; commands are the same packet types the original sends.

## 7. MEMORY AND PERFORMANCE RULES ("memory rent")

Tom's requirement: **all packet and temporary buffers are rented from pools and returned**; then he will tune it.

1. `ArrayPool<byte>.Shared.Rent/Return` (or `MemoryPool<byte>.Shared.Rent` with `IMemoryOwner<byte>` where lifetime crosses async boundaries). Define ONE ownership rule per buffer type and write it in the type's XML doc: *who rents, who returns, who may keep it*.
2. Receive path: the socket's receive buffer is reused, so the router copies into a rented buffer and **transfers ownership** to the target loop, which returns it in `finally` after handling (the reference code does this).
3. Send path: `SecuritySocket.Send/SendBatch` copy into their own rented buffer; callers keep theirs.
4. Parse with `ReadOnlySpan<byte>` / `BinaryPrimitives`; build packets with a `ref struct` writer over a rented span. No `new byte[]` per packet on hot paths (the reference code allocates in a few placeholder spots: replace them).
5. Pool `List<>` scratch objects, use `CollectionsMarshal`, `FrozenDictionary` for immutable tables (vendor shops, type data), `stackalloc` for small packets.
6. `Return(buf, clearArray: false)` for packet buffers.
7. Measure allocations: GC count per second under the stress test must stay near zero in steady state.

## 8. .NET 10 / C# 14 TOOLING TO USE

Generic Host + DI, `BackgroundService`, `TimeProvider` (testable clocks; the 500 ms ticks must be fake-clock testable), `System.Threading.Lock`, `Channel<T>`, primary constructors, collection expressions, C# 14 extension members, `[LoggerMessage]` source-generated logging, `System.Diagnostics.Metrics` (+ OpenTelemetry export optional), `FrozenDictionary/FrozenSet`, `PeriodicTimer`, `ArrayPool/MemoryPool`, `BinaryPrimitives`, `Span/Memory`, Native AOT-friendly code where cheap (not required), nullable enabled, warnings as errors, analyzers on.

## 9. OBSERVABILITY AND SAFETY (port of the original's safety net)

- Per message and per tick: measure; log `SLOW <type> <ms>` over a threshold (default 20 ms); keep `MaxHandleMs`, `Busy%`, `Backlog` per loop, expose via metrics. **Backlog per loop is the primary health metric.**
- try/catch around EVERY message handler (a bad packet must not stop a loop), exception logged with message type and entity id.
- Watchdog equivalent of `DEADLOOP_CHECK`: a monitor that flags a loop whose current message has run longer than N seconds.
- Kick policy: malformed packets (3 strikes), position desync, missing client heartbeat (2.5 x TICK_SECS), send queue over the limit.
- Graceful shutdown: stop accepting, drain, save players, close loops (original waits up to 30 s).
- Clock auto-correct as in the original (reset next tick if drift > 10 s).

## 10. TESTING REQUIREMENTS (acceptance criteria)

Reuse and extend the harness in the reference code (all of these passed there):
1. Double-kill: 64 sockets x 50 attacks on one monster -> exactly one death, exact hit count.
2. Transfer under fire: 200,000 attacks while the player moves between groups -> zero lost, order preserved.
3. Leave/disconnect removes the player everywhere; map becomes inactive; respawn still happens in an idle map.
4. Dormancy: 1000+ monsters with nobody near -> zero AI work; waking by proximity; hysteresis (keeps chasing past aggro until lose range).
5. Brain commands rejected when stale (target dead, step > 1).
6. Dynamic maps: created on the lightest group, destroyed while a respawn is pending -> no errors.
7. Socket: stateful-cipher loopback test with 6 producer threads mixing `Send` and `SendBatch` -> order and cipher intact; 50,000-packet backlog does not overflow the stack; malformed batch closes the socket; `PendingSends` returns to 0 and never throws.
8. Malformed packets: kicked once, no log spam.
9. Login flow: both arrival orders of `_MSG_LOGIN_A`/`_MSG_CONNECT`, wrong check data, duplicate login kick, BAN/NEW results.
10. Stress/soak: 2,000 simulated players + 5,000 monsters for 10 minutes: no backlog growth, steady memory, near-zero GC, worst message < 20 ms. Report numbers honestly (single machine, say what it is).

Every claim of "works" in your final report must name the test that proves it.

## 11. DELIVERY PLAN (phases, each ends green and committed)

1. Shared + Net + Runtime (adopt reference code, rename, DI, config, metrics, tests 1-8 above).
2. Account Server + World login flow + DB layer (test 9).
3. MapGroup kernel core: maps from `cq_map`, movement, view/broadcast, transfers, heartbeat, position sync.
4. Combat: auto-attack loop, damage, death, drops, PK/crime rules (read the original `User.cpp`, `CBattleSystem`).
5. Npc Server: AI port, generators, dormancy, pathfinding, transport (TCP + in-process), reconnect.
6. Items, equipment, shops/vendors, packages.
7. Magic, status effects.
8. Team, trade/booth, chat rooms, syndicates (+ `GameAction` script engine).
9. Hardening: soak test, tuning of pooling, docs, `DEVIATIONS.md`.

At the end of each phase give: what was ported (original file/function), tests added, numbers, known gaps.

## 12. DEFAULTS FOR OPEN DECISIONS (follow unless Tom says otherwise)

- DB: **async via a dedicated DB worker per group** (requests as messages, results posted back), NOT blocking the loop. This is a deliberate deviation from the original's synchronous `mysql_query`; log it in `DEVIATIONS.md`. Keep a synchronous path only for startup loading.
- Outbox batching: implemented, OFF by default (`FlushWindowMs = 0`).
- Monster pacing: 500 ms tick, per-monster timers from type data (not the 100 ms of the reference benchmark).
- Dormancy grid: align the brain grid to the original's 18-cell blocks.
- Broadcast: start with the grid query used in the reference code; if profiling shows it hot, switch to the original's per-role observer set (`m_setBCRole`) maintained on movement.
- NpcServer: separate process over TCP by default; in-process transport for tests.
- Number of MapGroups: `max(2, cores - 2)` unless config says otherwise; dedicated groups for the busy maps listed in config.
- Naming: keep the original names in comments for traceability; use idiomatic .NET names in code.
- Namespace root `Conquer.*`; do NOT use a namespace or type called `World` (Tom's socket code already has a `World` class).

## 13. HOW TO REPORT

Short, factual, no marketing. List deviations. State what you did not verify. When you port a rule, name the original file and function. If something in this brief contradicts the original source, trust the source and say so.

## 14. Rules
  1- make a proccess.md files so any agent can understand and work on it where you start and where you stop so any agent should follow your rules and same clean code and same workflow so they will not make any mistake
  2- if you want to plan a better flow or make some changes in the plan make sure you follow the 10 rules above and make sure it is better than the original one and also check with me with the reason and why and how it will improve our code/logic 