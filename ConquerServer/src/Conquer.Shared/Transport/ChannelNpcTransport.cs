// Conquer.Shared — In-process channel-based NPC transport.
// OPUS §5, §6.3: In-process Channels for tests and single-box deployments.

using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Conquer.Shared.Transport;

/// <summary>
/// In-process NPC transport implementation powered by System.Threading.Channels.
/// Provides zero-copy, deterministic messaging without network I/O.
/// </summary>
public sealed class ChannelNpcTransport : INpcTransport
{
    private readonly Channel<NpcPacket> _incoming;
    private readonly Channel<NpcPacket> _outgoing;
    private int _disposed;

    public bool IsConnected => Volatile.Read(ref _disposed) == 0;

    public ChannelNpcTransport(Channel<NpcPacket> incoming, Channel<NpcPacket> outgoing)
    {
        _incoming = incoming;
        _outgoing = outgoing;
    }

    /// <summary>
    /// Creates a bidirectional pair of connected transports for server and client.
    /// </summary>
    public static (ChannelNpcTransport ServerTransport, ChannelNpcTransport ClientTransport) CreatePair(int capacity = 0)
    {
        Channel<NpcPacket> ch1 = capacity > 0
            ? Channel.CreateBounded<NpcPacket>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            })
            : Channel.CreateUnbounded<NpcPacket>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

        Channel<NpcPacket> ch2 = capacity > 0
            ? Channel.CreateBounded<NpcPacket>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            })
            : Channel.CreateUnbounded<NpcPacket>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

        var server = new ChannelNpcTransport(incoming: ch1, outgoing: ch2);
        var client = new ChannelNpcTransport(incoming: ch2, outgoing: ch1);
        return (server, client);
    }

    public async ValueTask SendAsync(NpcPacket packet, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _outgoing.Writer.WriteAsync(packet, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<NpcPacket> ReceiveAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        while (await _incoming.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_incoming.Reader.TryRead(out var packet))
            {
                yield return packet;
            }
        }
    }

    public ValueTask CloseAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _outgoing.Writer.TryComplete();
            _incoming.Writer.TryComplete();
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => CloseAsync();
}
