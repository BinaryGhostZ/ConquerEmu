using System.Reflection;

namespace Conquer.Shared;

/// <summary>
/// Delegate for handling incoming packets.
/// Zero-copy read-only view of the packet buffer.
/// </summary>
public delegate void PacketHandlerDelegate<in TContext>(TContext context, ReadOnlySpan<byte> packet);

/// <summary>
/// Dispatches packets by PacketType to registered handlers.
/// Can discover handlers via [PacketHandler] attributes or manual registration.
/// </summary>
public class PacketDispatcher<TContext>
{
    private readonly Dictionary<PacketType, PacketHandlerDelegate<TContext>> _handlers = new();

    public void Register(PacketType type, PacketHandlerDelegate<TContext> handler)
    {
        _handlers[type] = handler;
    }

    public void RegisterFromAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            RegisterFromType(type);
        }
    }

    public void RegisterFromType(Type type)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        foreach (var method in methods)
        {
            var attrs = method.GetCustomAttributes<PacketHandlerAttribute>();
            foreach (var attr in attrs)
            {
                try
                {
                    var del = (PacketHandlerDelegate<TContext>)Delegate.CreateDelegate(
                        typeof(PacketHandlerDelegate<TContext>), method);
                    _handlers[attr.Type] = del;
                }
                catch (ArgumentException)
                {
                    // Skip methods that do not match the expected delegate signature
                }
            }
        }
    }

    public bool TryDispatch(TContext context, ReadOnlySpan<byte> packet)
    {
        if (packet.Length < WireProtocol.MinPacketSize) return false;

        var type = WireProtocol.ReadType(packet);
        if (_handlers.TryGetValue(type, out var handler))
        {
            handler(context, packet);
            return true;
        }

        return false;
    }
}
