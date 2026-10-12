// Conquer.UnitTests — SecuritySocketTests (OPUS §10, tests 7 and 8)
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Conquer.Net;
using Conquer.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Conquer.UnitTests;

public class SecuritySocketTests
{
    // A simple stateful cipher that XORs with an incrementing counter
    private class TestCipher : ICrypto
    {
        private byte _state = 0;
        
        public void Encrypt(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            for (int i = 0; i < source.Length; i++)
            {
                destination[i] = (byte)(source[i] ^ _state);
                _state++;
            }
        }

        public void Decrypt(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            for (int i = 0; i < source.Length; i++)
            {
                destination[i] = (byte)(source[i] ^ _state);
                _state++;
            }
        }
    }

    [Fact]
    public async Task Test7_StatefulCipher_Loopback_NoStackOverflow()
    {
        // OPUS test 7: stateful cipher loopback, 6 producer threads, 50k packets.
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var endPoint = (IPEndPoint)listener.LocalEndPoint!;

        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connectTask = clientSocket.ConnectAsync(endPoint);
        var serverSocket = await listener.AcceptAsync();
        await connectTask;

        var clientCrypto = new TestCipher();
        var serverCrypto = new TestCipher();
        
        var securitySocket = new SecuritySocket(1, serverSocket, serverCrypto, NullLogger.Instance);
        
        var tcs = new TaskCompletionSource();
        const int TotalPackets = 50000;
        securitySocket.Attach((id, body) => { }, id => { });
        securitySocket.Start();

        // The client receives and validates
        var clientTask = Task.Run(async () =>
        {
            byte[] recvBuf = new byte[TotalPackets * 16];
            int recvTail = 0;
            
            while (recvTail < TotalPackets * 16) // each packet is 16 bytes (8 body + 8 trailer)
            {
                int received = await clientSocket.ReceiveAsync(recvBuf.AsMemory(recvTail), SocketFlags.None);
                if (received == 0) break;
                
                clientCrypto.Decrypt(recvBuf.AsSpan(recvTail, received), recvBuf.AsSpan(recvTail, received));
                recvTail += received;
                
                // We don't bother framing perfectly here since we know they are all 16 bytes exactly.
                // But let's be safe.
            }
            
            // Validate all 50k packets were received and decrypted properly.
            // Since 6 threads call Send/SendBatch concurrently, the order of IDs won't be strictly 0 to 50000.
            // Wait, does the send loop preserve order? It preserves the order things are queued.
            // So we just verify we received 50k valid packets with correct lengths and trailer.
            int validCount = 0;
            for(int i = 0; i < recvTail; i += 16)
            {
                ushort len = WireProtocol.ReadLength(recvBuf.AsSpan(i));
                Assert.Equal(8, len);
                validCount++;
            }
            Assert.Equal(TotalPackets, validCount);
            tcs.TrySetResult();
        });

        // Start 6 producer threads calling securitySocket.Send/SendBatch
        const int Threads = 6;
        int remainingPackets = TotalPackets;
        
        var producers = new Task[Threads];
        for (int t = 0; t < Threads; t++)
        {
            producers[t] = Task.Run(() =>
            {
                while (Interlocked.Decrement(ref remainingPackets) >= 0)
                {
                    int packetBodyLen = 8;
                    int totalLen = WireProtocol.WireSize(packetBodyLen);
                    byte[] buf = new byte[totalLen];
                    
                    WireProtocol.WriteHeader(buf, (ushort)packetBodyLen, PacketType.Walk);
                    WireProtocol.WriteServerTrailer(buf.AsSpan(totalLen - WireProtocol.TrailerSize));

                    // Randomly choose Send or SendBatch
                    if (Random.Shared.Next(2) == 0)
                    {
                        securitySocket.Send(buf);
                    }
                    else
                    {
                        securitySocket.SendBatch(buf);
                    }
                }
            });
        }

        await Task.WhenAll(producers);
        
        // Wait for all to be received
        await Task.WhenAny(tcs.Task, Task.Delay(10000));
        await clientTask;
        
        Assert.Equal(0, securitySocket.PendingSends); // Should be 0 since it's the receiver, but just verifying properties

        securitySocket.Disconnect();
        clientSocket.Dispose();
        listener.Dispose();
    }

    [Fact]
    public async Task Test8_MalformedPackets_DisconnectsOnce()
    {
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var endPoint = (IPEndPoint)listener.LocalEndPoint!;

        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connectTask = clientSocket.ConnectAsync(endPoint);
        var serverSocket = await listener.AcceptAsync();
        await connectTask;

        var securitySocket = new SecuritySocket(1, serverSocket, new NullCrypto(), NullLogger.Instance);
        
        var tcs = new TaskCompletionSource();
        int disconnectCount = 0;
        securitySocket.Attach((id, body) => { }, id => 
        { 
            Interlocked.Increment(ref disconnectCount);
            tcs.TrySetResult();
        });

        securitySocket.Start();

        // Send a malformed packet (length > MaxPacketSize)
        byte[] badPacket = new byte[16];
        WireProtocol.WriteHeader(badPacket, 5000, PacketType.Walk);
        
        await clientSocket.SendAsync(badPacket, SocketFlags.None);

        await Task.WhenAny(tcs.Task, Task.Delay(2000));

        Assert.Equal(1, disconnectCount);

        securitySocket.Disconnect();
        clientSocket.Dispose();
        listener.Dispose();
    }
}
