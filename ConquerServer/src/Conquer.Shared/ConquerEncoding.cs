using System.Text;

namespace Conquer.Shared;

/// <summary>
/// Provides shared encoding for Conquer Online strings (Windows-1252 / ASCII).
/// </summary>
public static class ConquerEncoding
{
    private static readonly Encoding s_gameEncoding;

    static ConquerEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        s_gameEncoding = Encoding.GetEncoding(1252);
    }

    /// <summary>
    /// Default encoding for game text and packet strings (Windows-1252).
    /// </summary>
    public static Encoding GameEncoding => s_gameEncoding;
}
