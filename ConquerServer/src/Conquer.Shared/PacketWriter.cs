using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Conquer.Shared;

/// <summary>
/// Zero-allocation packet writer using rented byte buffers or caller spans.
/// Writes packets according to Conquer Online's wire framing [len:2][type:2][body...][TQServer:8].
/// </summary>
public ref struct PacketWriter : IDisposable
{
    public const int DefaultCapacity = 1024;
    private const int HeaderSize = WireProtocol.MinPacketSize; // 4 bytes

    private byte[]? _rentedBuffer;
    private Span<byte> _buffer;

    public int Position { get; private set; }
    public int Size { get; private set; }
    public int Capacity => _buffer.Length;

    public PacketWriter() : this(DefaultCapacity)
    {
    }

    public PacketWriter(int capacity)
    {
        _rentedBuffer = ArrayPool<byte>.Shared.Rent(capacity);
        _buffer = _rentedBuffer;
        Position = HeaderSize;
        Size = 0;
    }

    public PacketWriter(Span<byte> buffer)
    {
        _rentedBuffer = null;
        _buffer = buffer;
        Position = HeaderSize;
        Size = 0;
    }

    public void Dispose()
    {
        if (_rentedBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(_rentedBuffer, clearArray: false);
            _rentedBuffer = null;
        }
    }

    public void InitWriter()
    {
        Position = HeaderSize;
        Size = 0;
    }

    public void Seek(int offset)
    {
        if ((uint)offset > (uint)_buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Seek offset out of range");
        Position = offset;
    }

    public void SeekForward(int amount)
    {
        Ensure(amount);
        Position += amount;
    }

    public void SeekBackwards(int amount)
    {
        if (Position - amount < HeaderSize)
            throw new InvalidOperationException("Cannot seek backwards past packet header");
        Position -= amount;
    }

    public void Write(byte value)
    {
        Ensure(1);
        _buffer[Position++] = value;
    }

    public void Write(sbyte value) => Write((byte)value);

    public void Write(ushort value)
    {
        Ensure(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[Position..], value);
        Position += 2;
    }

    public void Write(short value) => Write((ushort)value);

    public void Write(uint value)
    {
        Ensure(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer[Position..], value);
        Position += 4;
    }

    public void Write(int value) => Write((uint)value);

    public void Write(ulong value)
    {
        Ensure(8);
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer[Position..], value);
        Position += 8;
    }

    public void Write(long value) => Write((ulong)value);

    public void Write(ReadOnlySpan<byte> data)
    {
        Ensure(data.Length);
        data.CopyTo(_buffer[Position..]);
        Position += data.Length;
    }

    public void ZeroFill(int amount)
    {
        Ensure(amount);
        _buffer.Slice(Position, amount).Clear();
        Position += amount;
    }

    /// <summary>
    /// Writes a fixed-length string, zero-padding any remaining space.
    /// </summary>
    public void Write(string? value, int length, Encoding? encoding = null)
    {
        Ensure(length);
        if (string.IsNullOrEmpty(value))
        {
            ZeroFill(length);
            return;
        }

        encoding ??= ConquerEncoding.GameEncoding;
        Span<byte> destination = _buffer.Slice(Position, length);
        int written = encoding.GetBytes(value.AsSpan(), destination);

        if (written < length)
        {
            destination[written..].Clear();
        }

        Position += length;
    }

    /// <summary>
    /// Writes an array of length-prefixed strings.
    /// </summary>
    public void Write(params string[] values)
    {
        Write((byte)values.Length);
        var encoding = ConquerEncoding.GameEncoding;
        foreach (var str in values)
        {
            if (string.IsNullOrEmpty(str))
            {
                Write((byte)0);
                continue;
            }
            int byteCount = encoding.GetByteCount(str);
            Write((byte)byteCount);
            Write(str, byteCount, encoding);
        }
    }

    public void WriteSeal()
    {
        Ensure(WireProtocol.TrailerSize);
        WireProtocol.WriteServerTrailer(_buffer.Slice(Position, WireProtocol.TrailerSize));
        Position += WireProtocol.TrailerSize;
    }

    /// <summary>
    /// Finalizes the packet by writing the trailer, total length (excluding trailer), and packet type.
    /// </summary>
    public void Finalize(PacketType type) => Finalize((ushort)type);

    public void Finalize(ushort type)
    {
        WriteSeal();
        Size = Position;

        // OPUS §3.9: len EXCLUDES the trailer
        ushort packetLengthExcludingTrailer = (ushort)(Size - WireProtocol.TrailerSize);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer, packetLengthExcludingTrailer);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[WireProtocol.TypeOffset..], type);
    }

    public readonly ReadOnlySpan<byte> AsSpan() => _buffer[..Size];

    private void Ensure(int count)
    {
        if ((uint)(Position + count) > (uint)_buffer.Length)
            throw new InvalidOperationException(
                $"Packet overflow. Position={Position}, Count={count}, Capacity={_buffer.Length}");
    }
}
