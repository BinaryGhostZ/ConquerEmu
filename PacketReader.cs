using System.Text;
using System.Buffers.Binary;

namespace ClassicConquer.ServerSockets
{
    /// <summary>
    /// Zero-allocation packet reader using ReadOnlySpan<byte>.
    /// Used to help decode packet structures using TQ Digital's byte ordering rules.
    /// </summary>
    public ref struct PacketReader
    {
        private readonly ReadOnlySpan<byte> _buffer;
        public int Position { get; private set; }
        
        public int Size => _buffer.Length;

        public PacketReader(ReadOnlySpan<byte> buffer)
        {
            _buffer = buffer;
            Position = 4; // Skip length and type (same as the old ReadUInt32() did)
        }

        public void Seek(int offset)
        {
            Position = offset;
        }

        public void SeekForward(int count)
        {
            Position += count;
        }

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            var slice = _buffer.Slice(Position, count);
            Position += count;
            return slice;
        }

        public byte ReadUInt8() => _buffer[Position++];
        
        public ushort ReadUInt16()
        {
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer[Position..]);
            Position += 2;
            return value;
        }

        public uint ReadUInt32()
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer[Position..]);
            Position += 4;
            return value;
        }

        public ulong ReadUInt64()
        {
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer[Position..]);
            Position += 8;
            return value;
        }

        public short ReadInt16() => (short)ReadUInt16();
        public int ReadInt32() => (int)ReadUInt32();
        public long ReadInt64() => (long)ReadUInt64();

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
        /// Reads a string from the current stream. Replicates BinaryReader's LEB128 prefixed string.
        /// </summary>
        public string ReadString()
        {
            int length = Read7BitEncodedInt();
            return ReadString(length);
        }

        /// <summary>
        /// Reads a string from the current stream with a known fixed length.
        /// </summary>
        public string ReadString(int fixedLength)
        {
            var slice = ReadBytes(fixedLength);
            return /*CodePagesEncodingProvider.Instance.GetEncoding(1252)*/Kernel.GameEncoding.GetString(slice).TrimEnd('\0');
        }

        public string[] ReadStringList()
        {
            var result = new string[ReadUInt8()];
            for (int i = 0; i < result.Length; i++)
                result[i] = ReadCString(ReadUInt8());
            return result;
        }

        public List<string> ReadStrings()
        {
            var strings = new List<string>();
            byte amount = ReadUInt8();
            for (var i = 0; i < amount; i++)
            {
                strings.Add(ReadString());
            }
            return strings;
        }

        public string ReadCString(int size, Encoding encoding = null)
        {
            if (size == 0)
                return string.Empty;

            encoding ??= Kernel.GameEncoding;

            var slice = ReadBytes(size);
            var value = encoding.GetString(slice);

            int nullIdx = value.IndexOf('\0', StringComparison.Ordinal);
            if (nullIdx >= 0)
            {
                return value[..nullIdx];
            }

            return value;
        }

        public unsafe T ReadUnmanaged<T>() where T : unmanaged
        {
            var slice = ReadBytes(sizeof(T));
            fixed (byte* ptr = slice)
            {
                return *(T*)ptr;
            }
        }

        public T ProtoBufferDeserialize<T>(T obj)
        {
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian(_buffer);
            var payload = _buffer.Slice(4, length - 4).ToArray();
            
            using var ms = new MemoryStream(payload);
            return ProtoBuf.Serializer.Deserialize<T>(ms);
        }
        public T ProtoBufferDeserializes<T>(T obj)
        {
            ushort length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(_buffer);
            int payloadLength = length - 4;

            // Borrow an array instead of using .ToArray()
            byte[] rentedBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(payloadLength);
            _buffer.Slice(4, payloadLength).CopyTo(rentedBuffer);

            // Create a MemoryStream strictly over the payload length
            using var ms = new MemoryStream(rentedBuffer, 0, payloadLength);
            T result = ProtoBuf.Serializer.Deserialize<T>(ms);

            // Return the array so no garbage is created!
            System.Buffers.ArrayPool<byte>.Shared.Return(rentedBuffer);

            return result;
        }

    }
}
