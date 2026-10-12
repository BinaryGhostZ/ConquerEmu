// Conquer.Net — Plaintext crypto for dev/testing or early protocols.

namespace Conquer.Net;

public sealed class PlaintextCrypto : ICrypto
{
    public void Encrypt(ReadOnlySpan<byte> plaintext, Span<byte> ciphertext)
    {
        plaintext.CopyTo(ciphertext);
    }

    public void Decrypt(ReadOnlySpan<byte> ciphertext, Span<byte> plaintext)
    {
        ciphertext.CopyTo(plaintext);
    }
}
