using System.Net;
using System.Net.Sockets;
using Conquer.AccountServer;
using Conquer.Data;
using Conquer.Shared;
using Conquer.Shared.Messages.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Conquer.IntegrationTests;

public class LoginFlowTests
{
    private class MockAccountRepo : IAccountRepository
    {
        public Task<AccountModel?> GetAccountByNameAsync(string name)
        {
            if (name == "testuser")
            {
                return Task.FromResult<AccountModel?>(new AccountModel
                {
                    Id = 12345,
                    Name = "testuser",
                    Password = "password123"
                });
            }
            return Task.FromResult<AccountModel?>(null);
        }

        public Task UpdateNetStateAsync(uint id, int netState) => Task.CompletedTask;
    }

    [Fact]
    public async Task CompleteLoginFlow_AuthenticatesAndPairsSuccessfully()
    {
        // 1. Mock IPC listener for MsgServer on port 5817
        var ipcListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        ipcListener.Bind(new IPEndPoint(IPAddress.Loopback, 5817));
        ipcListener.Listen(1);

        // 2. Start AccountServer on port 9866
        using var cts = new CancellationTokenSource();
        var accountWorker = new AccountServerWorker(
            NullLogger<AccountServerWorker>.Instance,
            new MockAccountRepo(),
            9866);
        
        _ = accountWorker.StartAsync(cts.Token);

        // 3. Connect as Client to AccountServer
        using var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await clientSocket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 9866));

        // 4. Send MsgAccount
        byte[] authBuffer = CreateAuthPacket("testuser", "password123", "ConquerServer");
        await clientSocket.SendAsync(authBuffer);

        // 5. AccountServer sends IPC MsgLoginAccount to MsgServer on 5817
        using var ipcClient = await ipcListener.AcceptAsync();
        byte[] ipcBuffer = new byte[128];
        int ipcReceived = await ipcClient.ReceiveAsync(ipcBuffer);
        Assert.True(ipcReceived > 0);

        // Parse IPC packet with safe decoder
        var ipcPacket = MsgLoginAccount.Decode(ipcBuffer.AsSpan(0, ipcReceived));
        Assert.Equal(12345u, ipcPacket.AccountId);
        Assert.Equal("testuser", ipcPacket.UserName);

        // 6. Client receives MsgAuthResponse from AccountServer
        byte[] responseBuffer = new byte[128];
        int received = await clientSocket.ReceiveAsync(responseBuffer);
        Assert.True(received > 0);

        var authRes = MsgAuthResponse.Decode(responseBuffer.AsSpan(0, received));
        Assert.Equal(12345u, authRes.AccountId);
        Assert.Equal(ipcPacket.CheckData, authRes.Token); // Tokens must match!

        // 7. Cleanup
        await accountWorker.StopAsync(CancellationToken.None);
        ipcListener.Dispose();
    }

    private static byte[] CreateAuthPacket(string user, string pass, string server)
    {
        var writer = new PacketWriter();
        try
        {
            var authPacket = new MsgAccount(user, pass, server);
            authPacket.Encode(ref writer);
            return writer.AsSpan().ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }
}
