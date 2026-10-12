// Conquer.Shared — Login and connection packet models.
// Uses PacketReader and PacketWriter for zero-allocation, safe serialization.

namespace Conquer.Shared.Messages.Packets;

/// <summary>
/// Sent from AccountServer to MsgServer via IPC to announce a valid login.
/// Original: CMsgLogin_A
/// Type: 1002 (LoginAccount)
/// </summary>
public readonly record struct MsgLoginAccount(uint AccountId, uint CheckData, ushort Version, string UserName)
{
    public static MsgLoginAccount Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint accountId = reader.ReadUInt32();
        uint checkData = reader.ReadUInt32();
        ushort version = reader.ReadUInt16();
        string userName = reader.ReadCString(16);
        return new MsgLoginAccount(accountId, checkData, version, userName);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(AccountId);
        writer.Write(CheckData);
        writer.Write(Version);
        writer.Write(UserName, 16);
        writer.Finalize(PacketType.LoginAccount);
    }
}

/// <summary>
/// Sent from Client to MsgServer to connect using an auth token.
/// Original: CMsgConnect
/// Type: 1052 (Connect)
/// </summary>
public readonly record struct MsgConnect(uint AccountId, uint Data, string Info)
{
    public static MsgConnect Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint accountId = reader.ReadUInt32();
        uint data = reader.ReadUInt32();
        string info = reader.ReadCString(16);
        return new MsgConnect(accountId, data, info);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(AccountId);
        writer.Write(Data);
        writer.Write(Info, 16);
        writer.Finalize(PacketType.Connect);
    }
}

/// <summary>
/// Sent from Client to AccountServer to authenticate.
/// Original: CMsgAccount_A
/// Type: 1051 (AccountA)
/// </summary>
public readonly record struct MsgAccount(string AccountName, string Password, string ServerName)
{
    public static MsgAccount Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        string accountName = reader.ReadCString(16);
        string password = reader.ReadCString(16);
        string serverName = reader.ReadCString(16);
        return new MsgAccount(accountName, password, serverName);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(AccountName, 16);
        writer.Write(Password, 16);
        writer.Write(ServerName, 16);
        writer.Finalize(PacketType.AccountA);
    }
}

/// <summary>
/// Sent from AccountServer to Client with token and GameServer endpoint.
/// Type: 1055
/// </summary>
public readonly record struct MsgAuthResponse(uint AccountId, uint Token, uint Status, string Ip, ushort Port)
{
    public static MsgAuthResponse Decode(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet);
        uint accountId = reader.ReadUInt32();
        uint token = reader.ReadUInt32();
        uint status = reader.ReadUInt32();
        string ip = reader.ReadCString(16);
        ushort port = reader.ReadUInt16();
        return new MsgAuthResponse(accountId, token, status, ip, port);
    }

    public void Encode(ref PacketWriter writer)
    {
        writer.Write(AccountId);
        writer.Write(Token);
        writer.Write(Status);
        writer.Write(Ip, 16);
        writer.Write(Port);
        writer.Finalize((PacketType)1055);
    }
}
