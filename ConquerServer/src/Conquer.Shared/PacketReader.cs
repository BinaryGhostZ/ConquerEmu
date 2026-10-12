using System.Buffers.Binary;
using System.Text;

namespace Conquer.Shared;

/// <summary>
/// Zero-allocation packet reader using ReadOnlySpan&lt;byte&gt;.
/// Decodes packet payloads using Conquer Online's little-endian byte ordering rules.
/// </summary>
public ref struct PacketReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    
    public int Position { get; private set; }
    public int Size => _buffer.Length;
    public int Remaining => _buffer.Length - Position;

    public PacketReader(ReadOnlySpan<byte> buffer, int startPosition = 4)
    {
        _buffer = buffer;
        Position = startPosition; // Default 4: skip [len:2][type:2] header
    }

    public void Seek(int offset)
    {
        if ((uint)offset > (uint)_buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Seek offset out of range");
        Position = offset;
    }

    public void SeekForward(int count)
    {
        EnsureRead(count);
        Position += count;
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        EnsureRead(count);
        var slice = _buffer.Slice(Position, count);
        Position += count;
        return slice;
    }

    public byte ReadUInt8()
    {
        EnsureRead(1);
        return _buffer[Position++];
    }

    public sbyte ReadInt8() => (sbyte)ReadUInt8();

    public ushort ReadUInt16()
    {
        EnsureRead(2);
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer[Position..]);
        Position += 2;
        return value;
    }

    public short ReadInt16() => (short)ReadUInt16();

    public uint ReadUInt32()
    {
        EnsureRead(4);
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer[Position..]);
        Position += 4;
        return value;
    }

    public int ReadInt32() => (int)ReadUInt32();

    public ulong ReadUInt64()
    {
        EnsureRead(8);
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer[Position..]);
        Position += 8;
        return value;
    }

    public long ReadInt64() => (long)ReadUInt64();

    /// <summary>
    /// Reads a fixed-length string, trimming any null terminator characters.
    /// </summary>
    public string ReadCString(int size, Encoding? encoding = null)
    {
        if (size <= 0) return string.Empty;
        EnsureRead(size);

        encoding ??= ConquerEncoding.GameEncoding;
        var slice = _buffer.Slice(Position, size);
        Position += size;

        int nullIdx = slice.IndexOf((byte)0);
        if (nullIdx >= 0)
        {
            slice = slice[..nullIdx];
        }

        return encoding.GetString(slice);
    }

    public string ReadString(int fixedLength) => ReadCString(fixedLength);

    private int Read7BitEncodedInt()
    {
        int count = 0;
        int shift = 0;
        byte b;
        do
        {
            if (shift == 5 * 7) throw new FormatException("Format_Bad7BitInt32");
            b = ReadUInt8();
            count |= (b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return count;
    }

    /// <summary>
    /// Reads a 7-bit encoded length-prefixed string.
    /// </summary>
    public string ReadString()
    {
        int length = Read7BitEncodedInt();
        return ReadCString(length);
    }

    /// <summary>
    /// Reads a list of length-prefixed strings.
    /// </summary>
    public List<string> ReadStrings()
    {
        var strings = new List<string>();
        byte amount = ReadUInt8();
        for (int i = 0; i < amount; i++)
        {
            strings.Add(ReadString());
        }
        return strings;
    }

    public string[] ReadStringList()
    {
        byte count = ReadUInt8();
        var result = new string[count];
        for (int i = 0; i < count; i++)
        {
            byte strLen = ReadUInt8();
            result[i] = ReadCString(strLen);
        }
        return result;
    }

    private void EnsureRead(int count)
    {
        if ((uint)(Position + count) > (uint)_buffer.Length)
            throw new InvalidOperationException(
                $"Packet read overflow. Position={Position}, Count={count}, Size={_buffer.Length}");
    }
}
