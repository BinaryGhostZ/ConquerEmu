// Conquer.Runtime — WorldLoop: the World kernel loop.
// Original: MsgServer/WorldThread.cpp + WorldKernel/WorldKernel.cpp
// OPUS §3.5: login pairing, user list, chat rooms, syndicate world data.

using System.Diagnostics.Metrics;
using Conquer.Shared;
using Conquer.Shared.Messages;
using Conquer.Shared.Messages.Packets;
using Microsoft.Extensions.Logging;

namespace Conquer.Runtime;

/// <summary>
/// The World kernel loop. Handles login pairing, user list management, chat rooms,
/// and world-level syndicate data.
/// <para>
/// <b>Original:</b> WorldKernel/WorldKernel.cpp CWorldKernel::ProcessMsg / OnTimer.
/// </para>
/// <para>
/// <b>Login flow (OPUS §3.5):</b>
/// 1. Account server sends _MSG_LOGIN_A → World stores a pending CConnectUser.
/// 2. Client sends _MSG_CONNECT → World matches (idAccount, checkData).
/// 3. Either can arrive first — the second completes the pairing.
/// 4. If already online → kick old, refuse new this round.
/// </para>
/// </summary>
public class WorldLoop : GameLoop
{
    protected override int TimerIntervalMs => TimingConstants.WorldKernelOnTimerMs;

    private readonly Dictionary<uint, PendingLogin> _pendingLogins = new();

    private class PendingLogin
    {
        public uint AccountId;
        public uint Token;
        public uint ClientToken;
        public string AccountName = string.Empty;
        public bool AuthReceived;
        public uint SocketId;
        public long ExpirationTicks;
    }

    public WorldLoop(
        ILogger<WorldLoop> logger,
        IMeterFactory meterFactory,
        TimeProvider clock)
        : base("World", Conquer.Shared.PortId.World, logger, meterFactory, clock)
    {
    }

    protected override void ProcessMessage(in InternalMessage message)
    {
        // Phase 2 stub — will be filled when Account Server + login flow is built
        switch (message.Id)
        {
            case InternalMessageId.KernelClientMsg:
                ProcessClientMsg((uint)message.SourceId, message.PayloadSpan);
                break;

            case InternalMessageId.KernelCloseKernel:
                // Socket closed during login
                break;

            case InternalMessageId.WorldSetProcessId:
                // A MapGroup is telling World about a socket binding change
                break;

            case InternalMessageId.WorldCloseSocket:
                // Request to close a socket
                break;

            default:
                Logger.LogDebug("[World] Unhandled message {MsgId}", message.Id);
                break;
        }
    }

    private void ProcessClientMsg(uint socketId, ReadOnlySpan<byte> data)
    {
        var type = WireProtocol.ReadType(data);

        if (type == PacketType.LoginAccount) // Internal from AccountServer
        {
            var msg = MsgLoginAccount.Decode(data);
            HandleAuthReceived(msg.AccountId, msg.CheckData, msg.UserName);
        }
        else if (type == PacketType.Connect) // External from Client
        {
            var msg = MsgConnect.Decode(data);
            HandleClientConnect(socketId, msg.AccountId, msg.Data);
        }
    }

    private void HandleAuthReceived(uint accountId, uint token, string accountName)
    {
        if (!_pendingLogins.TryGetValue(accountId, out var login))
        {
            login = new PendingLogin { AccountId = accountId };
            _pendingLogins[accountId] = login;
        }

        login.Token = token;
        login.AccountName = accountName;
        login.AuthReceived = true;
        login.ExpirationTicks = Clock.GetUtcNow().Ticks + TimeSpan.FromMinutes(1).Ticks;

        Logger.LogInformation("[World] Auth received for {Account} (ID: {Id})", accountName, accountId);
        
        TryCompleteLogin(login);
    }

    private void HandleClientConnect(uint socketId, uint accountId, uint token)
    {
        if (!_pendingLogins.TryGetValue(accountId, out var login))
        {
            login = new PendingLogin { AccountId = accountId };
            _pendingLogins[accountId] = login;
        }

        login.ClientToken = token;
        login.SocketId = socketId;
        login.ExpirationTicks = Clock.GetUtcNow().Ticks + TimeSpan.FromMinutes(1).Ticks;

        Logger.LogInformation("[World] Client connected for Account ID {Id} on Socket {SocketId}", accountId, socketId);
        
        TryCompleteLogin(login);
    }

    private void TryCompleteLogin(PendingLogin login)
    {
        if (!login.AuthReceived || login.SocketId == 0)
        {
            return; // Waiting for the other half
        }

        if (login.Token != login.ClientToken)
        {
            Logger.LogWarning("[World] Token mismatch for {Id}. Expected {Expected}, got {Got}", login.AccountId, login.Token, login.ClientToken);
            _pendingLogins.Remove(login.AccountId);
            // In a real implementation we would tell the router to close the socket
            return;
        }

        Logger.LogInformation("[World] Login pairing successful for {Account} (ID: {Id}) on Socket {SocketId}", login.AccountName, login.AccountId, login.SocketId);
        
        _pendingLogins.Remove(login.AccountId);

        // Phase 2/3: 
        // 1. Fetch character from DB
        // 2. If valid, router.Rebind(socketId, mapGroupId)
        // 3. Send MsgUserInfo, MsgTalk "Welcome"
    }

    protected override void OnTimer()
    {
        long now = Clock.GetUtcNow().Ticks;
        var expired = new List<uint>();
        
        foreach (var kvp in _pendingLogins)
        {
            if (now > kvp.Value.ExpirationTicks)
            {
                expired.Add(kvp.Key);
            }
        }
        
        foreach (var id in expired)
        {
            _pendingLogins.Remove(id);
            Logger.LogWarning("[World] Pending login for Account ID {Id} timed out.", id);
        }
    }

    protected override void OnStart()
    {
        Logger.LogInformation("[World] Started");
    }

    protected override void OnStop()
    {
        Logger.LogInformation("[World] Stopped");
    }
}

