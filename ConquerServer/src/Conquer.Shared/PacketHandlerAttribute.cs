namespace Conquer.Shared;

/// <summary>
/// Marks a static or instance method as a packet handler for a specific PacketType.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class PacketHandlerAttribute : Attribute
{
    public PacketType Type { get; }

    public PacketHandlerAttribute(PacketType type)
    {
        Type = type;
    }

    public PacketHandlerAttribute(ushort type)
    {
        Type = (PacketType)type;
    }
}
