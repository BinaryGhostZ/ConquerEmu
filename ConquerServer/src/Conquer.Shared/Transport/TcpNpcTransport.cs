// Conquer.Shared — TCP-based NPC transport.
// Original: NpcServer/ClientSocket.cpp and ServerSocket/ServerSocket.cpp.
// OPUS §5, §6.3: TCP transport with automatic reconnect.

using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Conquer.Shared.Transport;

/// <summary>
/// TCP client implementation of INpcTransport.
/// Handles connection, framing, and automatic reconnection if disconnected.
/// </summary>
public sealed class TcpNpcTransport : INpcTransport
{
    private readonly string _host;
    private readonly int _port;
    private readonly bool _autoReconnect;
    private readonly TimeSpan _reconnectDelay;
    private Socket? _socket;
    private NetworkStream? _stream;
    private int _disposed;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public bool IsConnected => Volatile.Read(ref _disposed) == 0 && _socket?.Connected == true;

    public TcpNpcTransport(
        string host = "127.0.0.1",
        int port = 9867,
        bool autoReconnect = true,
        TimeSpan? reconnectDelay = null)
    {
        _host = host;
        _port = port;
        _autoReconnect = autoReconnect;
        _reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(1);
    }

    internal TcpNpcTransport(Socket connectedSocket)
    {
        _host = "";
        _port = 0;
        _autoReconnect = false;
        _socket = connectedSocket;
        _stream = new NetworkStream(connectedSocket, ownsSocket: true);
    }

    public async ValueTask ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            return;

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };

        await socket.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: true);
    }

    public async ValueTask SendAsync(NpcPacket packet, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        byte[] encoded = NpcWireProtocol.Encode(packet);
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_stream == null || !IsConnected)
            {
                if (_autoReconnect)
                {
                    await ConnectAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    throw new InvalidOperationException("TCP NPC transport is not connected.");
                }
            }

            await _stream!.WriteAsync(encoded, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async IAsyncEnumerable<NpcPacket> ReceiveAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        byte[] headerBuffer = new byte[NpcWireProtocol.EnvelopeHeaderSize];

        while (Volatile.Read(ref _disposed) == 0 && !ct.IsCancellationRequested)
        {
            if (_stream == null || !IsConnected)
            {
                if (!_autoReconnect)
                    yield break;

                try
                {
                    await Task.Delay(_reconnectDelay, ct).ConfigureAwait(false);
                    await ConnectAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
                catch
                {
                    continue;
                }
            }

            NpcPacket packet;
            try
            {
                // Read 8-byte envelope header
                await ReadExactAsync(_stream!, headerBuffer, ct).ConfigureAwait(false);
                var (msgSize, packetType, npcId) = NpcWireProtocol.ReadEnvelope(headerBuffer);

                int payloadSize = msgSize - NpcWireProtocol.EnvelopeHeaderSize;
                byte[] payload = payloadSize > 0 ? new byte[payloadSize] : [];
                if (payloadSize > 0)
                {
                    await ReadExactAsync(_stream!, payload, ct).ConfigureAwait(false);
                }

                packet = new NpcPacket(npcId, packetType, payload);
            }
            catch (Exception) when (Volatile.Read(ref _disposed) == 0 && _autoReconnect && !ct.IsCancellationRequested)
            {
                CloseSocket();
                continue;
            }

            yield return packet;
        }
    }

    private static async ValueTask ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[totalRead..], ct).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Remote closed NPC connection.");
            totalRead += read;
        }
    }

    private void CloseSocket()
    {
        try { _stream?.Dispose(); } catch { }
        try { _socket?.Dispose(); } catch { }
        _stream = null;
        _socket = null;
    }

    public ValueTask CloseAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            CloseSocket();
            _sendLock.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => CloseAsync();
}

/// <summary>
/// TCP listener for GameServer to accept NPC server connection.
/// Original: ServerSocket.cpp:171 m_idNpcSocket.
/// </summary>
public sealed class TcpNpcListener : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private int _disposed;

    public IPEndPoint LocalEndpoint => (IPEndPoint)_listener.LocalEndpoint;

    public TcpNpcListener(IPAddress ip, int port)
    {
        _listener = new TcpListener(ip, port);
    }

    public void Start() => _listener.Start();

    public async ValueTask<INpcTransport> AcceptAsync(CancellationToken ct = default)
    {
        Socket socket = await _listener.AcceptSocketAsync(ct).ConfigureAwait(false);
        socket.NoDelay = true;
        return new TcpNpcTransport(socket);
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _listener.Stop();
        }
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }
}
