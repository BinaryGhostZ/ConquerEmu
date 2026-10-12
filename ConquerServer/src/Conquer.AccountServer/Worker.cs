// Conquer.AccountServer — Background service listening for client logins.

using System.Net;
using System.Net.Sockets;
using Conquer.Data;
using Conquer.Net;
using Conquer.Shared;
using Conquer.Shared.Messages.Packets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Conquer.AccountServer;

public class AccountServerWorker : BackgroundService
{
    private readonly ILogger<AccountServerWorker> _logger;
    private readonly IAccountRepository _repository;
    private readonly int _port;

    public AccountServerWorker(ILogger<AccountServerWorker> logger, IAccountRepository repository, int port = 9866)
    {
        _logger = logger;
        _repository = repository;
        _port = port;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Any, _port));
        listener.Listen(100);
        
        _logger.LogInformation("AccountServer listening on port {Port}", _port);

        while (!stoppingToken.IsCancellationRequested)
        {
            var socket = await listener.AcceptAsync(stoppingToken);
            _ = HandleClientAsync(socket, stoppingToken);
        }
    }

    private async Task HandleClientAsync(Socket socket, CancellationToken ct)
    {
        try
        {
            var crypto = new PlaintextCrypto();
            using var secSocket = new SecuritySocket(1, socket, crypto, _logger);
            
            var tcs = new TaskCompletionSource();
            
            secSocket.Attach((id, body) =>
            {
                try
                {
                    var type = WireProtocol.ReadType(body);
                    if (type == PacketType.AccountA)
                    {
                        var auth = MsgAccount.Decode(body);
                        _ = ProcessAuthAsync(secSocket, auth.AccountName, auth.Password);
                    }
                    else
                    {
                        _logger.LogWarning("Unexpected packet type {Type} on AccountServer", type);
                        secSocket.Disconnect();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing auth");
                    secSocket.Disconnect();
                }
            }, id => tcs.TrySetResult());
            
            secSocket.Start();
            await tcs.Task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Client socket error");
        }
    }

    private async Task ProcessAuthAsync(SecuritySocket socket, string account, string password)
    {
        _logger.LogInformation("Auth request for user {Account}", account);

        var dbUser = await _repository.GetAccountByNameAsync(account);
        if (dbUser == null || dbUser.Password != password)
        {
            socket.Disconnect();
            return;
        }

        // Generate token
        uint token = (uint)Random.Shared.Next();
        
        // Send MsgLogin_A to MsgServer (Internal IPC)
        await NotifyMsgServerAsync(dbUser.Id, token, account);

        // Send AuthResponse to client
        SendAuthResponse(socket, dbUser.Id, token, "127.0.0.1", 5816);
    }

    private async Task NotifyMsgServerAsync(uint accountId, uint token, string accountName)
    {
        try
        {
            using var ipcSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await ipcSocket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 5817));
            
            var writer = new PacketWriter();
            byte[] payload;
            try
            {
                var loginMsg = new MsgLoginAccount(accountId, token, 1000, accountName);
                loginMsg.Encode(ref writer);
                payload = writer.AsSpan().ToArray();
            }
            finally
            {
                writer.Dispose();
            }
            
            await ipcSocket.SendAsync(payload, SocketFlags.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify MsgServer");
        }
    }

    private static void SendAuthResponse(SecuritySocket socket, uint accountId, uint token, string ip, ushort port)
    {
        var writer = new PacketWriter();
        try
        {
            var response = new MsgAuthResponse(accountId, token, 1, ip, port);
            response.Encode(ref writer);
            socket.Send(writer.AsSpan());
        }
        finally
        {
            writer.Dispose();
        }
            
        // Disconnect client cleanly after handoff
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            socket.Disconnect();
        });
    }
}
