namespace CliSharp.Application.Terminal;

/// <summary>
/// Determines display width of a Unicode character in terminal cells.
/// 0 = zero-width (control), 1 = normal, 2 = wide (CJK, fullwidth).
/// Covers BMP; characters outside BMP are treated as width 1.
/// </summary>
internal static class UnicodeWidth
{
    public static int GetWidth(char c)
    {
        if (c < 0x20 || (c >= 0x7F && c < 0xA0)) return 0;
        if (c < 0x1100) return 1;
        return IsWide(c) ? 2 : 1;
    }

    private static bool IsWide(char c) => c switch
    {
        >= '\u1100' and <= '\u115F' => true,  // Hangul Jamo
        >= '\u2329' and <= '\u232A' => true,  // Angle brackets
        >= '\u2E80' and <= '\u303E' => true,  // CJK Radicals, Kangxi, symbols
        >= '\u3040' and <= '\u33BF' => true,  // Hiragana, Katakana, Bopomofo, CJK compat
        >= '\u3400' and <= '\u4DBF' => true,  // CJK Unified Extension A
        >= '\u4E00' and <= '\uA4CF' => true,  // CJK Unified Ideographs, Yi
        >= '\uAC00' and <= '\uD7AF' => true,  // Hangul Syllables
        >= '\uF900' and <= '\uFAFF' => true,  // CJK Compatibility Ideographs
        >= '\uFE30' and <= '\uFE6F' => true,  // CJK Compatibility Forms
        >= '\uFF01' and <= '\uFF60' => true,  // Fullwidth ASCII/punctuation
        >= '\uFFE0' and <= '\uFFE6' => true,  // Fullwidth currency/symbols
        _ => false
    };
}
