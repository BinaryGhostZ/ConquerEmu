using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace ClassicConquer.ServerSockets;
public ref struct PacketWriter : IDisposable
{
    public const int MaxSize = 1024;
    private const int HeaderSize = 4;
    private const int TQ_SEALSIZE = 8;
    private static string seal;
    public static int SealSize => seal != null ? TQ_SEALSIZE : 0;

    public static string SealString
    {
        get => seal;
        set
        {
            if (value.Length != TQ_SEALSIZE)
                throw new ArgumentOutOfRangeException(nameof(value), "Packet seal must be " + TQ_SEALSIZE + " chars long.");
            seal = value;
        }
    }
    
    private byte[] _rentedBuffer;
    private Span<byte> _buffer;

    public int Position { get; private set; }
    public int Size { get; private set; }

    public static long RentedCount = 0;

    public PacketWriter()
    {
        _rentedBuffer = ArrayPool<byte>.Shared.Rent(MaxSize);
        System.Threading.Interlocked.Increment(ref RentedCount);
        _buffer = _rentedBuffer;
        Position = 0;
        Size = 0;
    }

    public PacketWriter(Span<byte> buffer)
    {
        _rentedBuffer = null;
        _buffer = buffer;
        Position = 0;
        Size = 0;
    }

    public void Dispose()
    {
        if (_rentedBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(_rentedBuffer);
            System.Threading.Interlocked.Decrement(ref RentedCount);
            _rentedBuffer = null;
        }
    }

    public readonly int Capacity => _buffer.Length;

    public void InitWriter()
    {
        Position = HeaderSize;
        Size = 0;
    }

    public void Seek(int offset)
    {
        Position = offset;
    }

    public void SeekForward(int amount)
    {
        Position += amount;
    }

    public void SeekBackwards(int amount)
    {
        Position -= amount;
    }

    public void Write(byte value)
    {
        Ensure(1);
        _buffer[Position++] = value;
    }

    public void Write(ushort value)
    {
        Ensure(2);
        BinaryPrimitives.WriteUInt16LittleEndian(
            _buffer[Position..], value);

        Position += 2;
    }

    public void Write(uint value)
    {
        Ensure(4);
        BinaryPrimitives.WriteUInt32LittleEndian(
            _buffer[Position..], value);

        Position += 4;
    }

    public void Write(ulong value)
    {
        Ensure(8);
        BinaryPrimitives.WriteUInt64LittleEndian(
            _buffer[Position..], value);

        Position += 8;
    }

    public void Write(sbyte value) => Write((byte)value);
    public void Write(short value) => Write((ushort)value);
    public void Write(int value) => Write((uint)value);
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

    public void Write(string value, int length)
    {
        Ensure(length);

        if (string.IsNullOrEmpty(value))
        {
            ZeroFill(length);
            return;
        }

        Encoding encoding = Kernel.GameEncoding;//Encoding.GetEncoding(1252);

        Span<byte> destination = _buffer.Slice(Position, length);

        int written = encoding.GetBytes(
            value.AsSpan(),
            destination);

        if (written < length)
            destination[written..].Clear();

        Position += length;
    }
   
    public void Write(params string[] value)
    {
        Write((byte)value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            var str = value[i];
            if (string.IsNullOrEmpty(str))
            {
                Write((byte)0);
                continue;
            }
            int byteCount = /*Encoding.GetEncoding(1252)*/Kernel.GameEncoding.GetByteCount(str);
            Write((byte)byteCount);
            Write(str, byteCount);
        }
    }

    public byte ReadUInt8()
    {
        EnsureRead(1);
        return _buffer[Position++];
    }

    public ushort ReadUInt16()
    {
        EnsureRead(2);

        ushort value =
            BinaryPrimitives.ReadUInt16LittleEndian(
                _buffer[Position..]);

        Position += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        EnsureRead(4);

        uint value =
            BinaryPrimitives.ReadUInt32LittleEndian(
                _buffer[Position..]);

        Position += 4;
        return value;
    }

    public ulong ReadUInt64()
    {
        EnsureRead(8);

        ulong value =
            BinaryPrimitives.ReadUInt64LittleEndian(
                _buffer[Position..]);

        Position += 8;
        return value;
    }

    public sbyte ReadInt8() => (sbyte)ReadUInt8();
    public short ReadInt16() => (short)ReadUInt16();
    public int ReadInt32() => (int)ReadUInt32();
    public long ReadInt64() => (long)ReadUInt64();

    public string ReadCString(int length)
    {
        EnsureRead(length);

        ReadOnlySpan<byte> data =
            _buffer.Slice(Position, length);

        int nullIndex = data.IndexOf((byte)0);

        int stringLength =
            nullIndex >= 0
                ? nullIndex
                : length;

        string result =
            Encoding.GetEncoding(1252)
                .GetString(data[..stringLength]);

        Position += length;

        return result;
    }

    public void ProtoBufferSerialize<T>(T obj)
    {
        using var ms = new MemoryStream();
        ProtoBuf.Serializer.Serialize(ms, obj);
        if (ms.TryGetBuffer(out ArraySegment<byte> buf))
        {
            Ensure(buf.Count);
            buf.AsSpan().CopyTo(_buffer[Position..]);
            Position += buf.Count;
        }
        else
        {
            byte[] array = ms.ToArray();
            Ensure(array.Length);
            array.AsSpan().CopyTo(_buffer[Position..]);
            Position += array.Length;
        }
    }

    public void WriteSeal() => Write(SealString, TQ_SEALSIZE);
    public void Finalize(ushort type)
    {
        if (SealSize > 0)
            WriteSeal();

        Size = Position;

        BinaryPrimitives.WriteUInt16LittleEndian(
            _buffer,
            (ushort)(Size - SealSize));

        BinaryPrimitives.WriteUInt16LittleEndian(
            _buffer[2..],
            type);
    }
    public void Complete()
    {
        Size = Position;
    }
    public void FinalizePacketS(ushort type)
    {
        if (SealSize > 0)
            WriteSeal();

        int size = Position;

        BinaryPrimitives.WriteUInt16LittleEndian(
            _buffer,
            (ushort)(size - SealSize));

        BinaryPrimitives.WriteUInt16LittleEndian(
            _buffer[2..],
            type);

        Size = size;
    }

    public readonly ReadOnlySpan<byte> AsSpan()
        => _buffer[..Size];

    private void Ensure(int count)
    {
        if ((uint)(Position + count) > (uint)_buffer.Length)
            throw new InvalidOperationException(
                $"Packet overflow. Position={Position}, Count={count}, Capacity={_buffer.Length}");
    }

    private void EnsureRead(int count)
    {
        if ((uint)(Position + count) > (uint)Size)
            throw new InvalidOperationException(
                $"Packet read overflow. Position={Position}, Count={count}, Size={Size}");
    }
}
