using System.Reflection;
using Conquer.Shared;
using Conquer.Shared.Messages.Packets;
using Xunit;

namespace Conquer.UnitTests;

public class PacketReaderWriterTests
{
    [Fact]
    public void PacketWriter_And_Reader_Roundtrip_Primitives()
    {
        var writer = new PacketWriter();
        try
        {
            writer.Write((byte)42);
            writer.Write((ushort)1234);
            writer.Write((uint)567890);
            writer.Write((ulong)9876543210123456UL);
            writer.Write("Hello Conquer", 16);
            writer.Finalize(PacketType.AccountA);

            var span = writer.AsSpan();
            Assert.True(WireProtocol.IsValidPacket(span));
            Assert.Equal(PacketType.AccountA, WireProtocol.ReadType(span));

            var reader = new PacketReader(span);
            Assert.Equal(42, reader.ReadUInt8());
            Assert.Equal(1234, reader.ReadUInt16());
            Assert.Equal(567890u, reader.ReadUInt32());
            Assert.Equal(9876543210123456UL, reader.ReadUInt64());
            Assert.Equal("Hello Conquer", reader.ReadCString(16));
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public void PacketWriter_And_Reader_Roundtrip_PacketModels()
    {
        // 1. MsgAccount
        var auth = new MsgAccount("Hero123", "SecretPass", "Phoenix");
        var writer = new PacketWriter();
        try
        {
            auth.Encode(ref writer);
            var decodedAuth = MsgAccount.Decode(writer.AsSpan());
            Assert.Equal("Hero123", decodedAuth.AccountName);
            Assert.Equal("SecretPass", decodedAuth.Password);
            Assert.Equal("Phoenix", decodedAuth.ServerName);
        }
        finally
        {
            writer.Dispose();
        }

        // 2. MsgLoginAccount
        var login = new MsgLoginAccount(1001, 8888, 1000, "Hero123");
        writer = new PacketWriter();
        try
        {
            login.Encode(ref writer);
            var decodedLogin = MsgLoginAccount.Decode(writer.AsSpan());
            Assert.Equal(1001u, decodedLogin.AccountId);
            Assert.Equal(8888u, decodedLogin.CheckData);
            Assert.Equal((ushort)1000, decodedLogin.Version);
            Assert.Equal("Hero123", decodedLogin.UserName);
        }
        finally
        {
            writer.Dispose();
        }

        // 3. MsgAuthResponse
        var authRes = new MsgAuthResponse(1001, 8888, 1, "127.0.0.1", 5816);
        writer = new PacketWriter();
        try
        {
            authRes.Encode(ref writer);
            var decodedAuthRes = MsgAuthResponse.Decode(writer.AsSpan());
            Assert.Equal(1001u, decodedAuthRes.AccountId);
            Assert.Equal(8888u, decodedAuthRes.Token);
            Assert.Equal(1u, decodedAuthRes.Status);
            Assert.Equal("127.0.0.1", decodedAuthRes.Ip);
            Assert.Equal((ushort)5816, decodedAuthRes.Port);
        }
        finally
        {
            writer.Dispose();
        }
    }

    private class TestContext
    {
        public bool Handled { get; set; }
        public string User { get; set; } = string.Empty;
    }

    private static class Handlers
    {
        [PacketHandler(PacketType.AccountA)]
        public static void HandleAccount(TestContext ctx, ReadOnlySpan<byte> packet)
        {
            var msg = MsgAccount.Decode(packet);
            ctx.Handled = true;
            ctx.User = msg.AccountName;
        }
    }

    [Fact]
    public void PacketDispatcher_DispatchesAttributeHandler()
    {
        var dispatcher = new PacketDispatcher<TestContext>();
        dispatcher.RegisterFromType(typeof(Handlers));

        var writer = new PacketWriter();
        try
        {
            new MsgAccount("DragonSlayer", "Pass", "Classic").Encode(ref writer);
            var packet = writer.AsSpan();

            var ctx = new TestContext();
            bool result = dispatcher.TryDispatch(ctx, packet);

            Assert.True(result);
            Assert.True(ctx.Handled);
            Assert.Equal("DragonSlayer", ctx.User);
        }
        finally
        {
            writer.Dispose();
        }
    }
}
