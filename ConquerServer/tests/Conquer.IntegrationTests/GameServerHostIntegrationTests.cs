using System.Net;
using System.Net.Sockets;
using Conquer.GameServer;
using Conquer.Net;
using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Messages.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Conquer.IntegrationTests;

public class GameServerHostIntegrationTests
{
    [Fact]
    public async Task GameServerWorker_StartsAndBindsListeners_GracefulShutdown()
    {
        const int testGamePort = 15816;
        const int testIpcPort = 15817;
        var config = new WorldHostConfig
        {
            ServerName = "TestConquerServer",
            ListenPort = testGamePort,
            IpcPort = testIpcPort,
            MapGroupCount = 2
        };

        var meterFactory = new TestMeterFactory();
        var clock = TimeProvider.System;
        var loggerFactory = NullLoggerFactory.Instance;
        var workerLogger = NullLogger<GameServerWorker>.Instance;

        var worker = new GameServerWorker(config, loggerFactory, workerLogger, meterFactory, clock);

        using var cts = new CancellationTokenSource();
        var workerTask = worker.StartAsync(cts.Token);

        // Give the listeners time to bind
        await Task.Delay(200);

        Assert.NotNull(worker.WorldHost);
        Assert.Equal(2, worker.WorldHost.MapGroups.Count);

        // Connect client to GameServer
        using var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await clientSocket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, testGamePort));
        Assert.True(clientSocket.Connected);

        // Send a MsgConnect packet
        var writer = new PacketWriter();
        byte[] connectPacket;
        try
        {
            var msgConnect = new MsgConnect(1001, 55555, "TestPlayer");
            msgConnect.Encode(ref writer);
            connectPacket = writer.AsSpan().ToArray();
        }
        finally
        {
            writer.Dispose();
        }

        await clientSocket.SendAsync(connectPacket);

        // Verify clean shutdown
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        await workerTask;
    }

    [Fact]
    public async Task GameServerWorker_HandlesIpcLogin_AndClientPairing()
    {
        const int testGamePort = 15826;
        const int testIpcPort = 15827;

        var config = new WorldHostConfig
        {
            ServerName = "TestConquerServer",
            ListenPort = testGamePort,
            IpcPort = testIpcPort,
            MapGroupCount = 2
        };

        var meterFactory = new TestMeterFactory();
        var clock = TimeProvider.System;
        var loggerFactory = NullLoggerFactory.Instance;
        var workerLogger = NullLogger<GameServerWorker>.Instance;

        var worker = new GameServerWorker(config, loggerFactory, workerLogger, meterFactory, clock);

        using var cts = new CancellationTokenSource();
        var workerTask = worker.StartAsync(cts.Token);

        await Task.Delay(200);

        // 1. Connect IPC socket and send MsgLoginAccount
        using var ipcSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await ipcSocket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, testIpcPort));

        var writerIpc = new PacketWriter();
        byte[] ipcPacket;
        try
        {
            var loginAccount = new MsgLoginAccount(99999, 123456, 1000, "ConquerHero");
            loginAccount.Encode(ref writerIpc);
            ipcPacket = writerIpc.AsSpan().ToArray();
        }
        finally
        {
            writerIpc.Dispose();
        }

        await ipcSocket.SendAsync(ipcPacket);
        await Task.Delay(100);

        // 2. Connect client socket and send MsgConnect
        using var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await clientSocket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, testGamePort));

        var writerClient = new PacketWriter();
        byte[] clientPacket;
        try
        {
            var msgConnect = new MsgConnect(99999, 123456, "ConquerHero");
            msgConnect.Encode(ref writerClient);
            clientPacket = writerClient.AsSpan().ToArray();
        }
        finally
        {
            writerClient.Dispose();
        }

        await clientSocket.SendAsync(clientPacket);
        await Task.Delay(100);

        // 3. Graceful shutdown
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        await workerTask;
    }
}
