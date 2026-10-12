// Conquer.Net — Cryptography interface.
// OPUS §3.9: Handshake handles DH key exchange, then a stateful stream cipher via ICrypto.
// "Never assume the cipher is stateless."

namespace Conquer.Net;

/// <summary>
/// Stateful stream cipher for client-server communication.
/// </summary>
public interface ICrypto
{
    /// <summary>
    /// Encrypt a packet in-place, or from source to destination.
    /// Since the cipher is stateful, it must be called EXACTLY once per packet, in order.
    /// </summary>
    void Encrypt(ReadOnlySpan<byte> source, Span<byte> destination);

    /// <summary>
    /// Decrypt a packet in-place, or from source to destination.
    /// Must be called EXACTLY once per received packet, in order.
    /// </summary>
    void Decrypt(ReadOnlySpan<byte> source, Span<byte> destination);
}

/// <summary>
/// Null crypto for internal NPC transport and testing.
/// </summary>
public sealed class NullCrypto : ICrypto
{
    public void Encrypt(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source != destination)
        {
            source.CopyTo(destination);
        }
    }

    public void Decrypt(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source != destination)
        {
            source.CopyTo(destination);
        }
    }
}
