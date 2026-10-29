using System.Text;

namespace KotoNeko.Core.Text;

/// <summary>Small helpers for telling scripts apart and converting between kana.</summary>
public static class Kana
{
    private const char HiraganaFirst = 'ぁ';
    private const char HiraganaLast = 'ゖ';
    private const char KatakanaFirst = 'ァ';
    private const char KatakanaLast = 'ヶ';
    private const int KatakanaOffset = KatakanaFirst - HiraganaFirst;

    /// <summary>The prolonged sound mark ー, used in both scripts.</summary>
    public const char LongVowelMark = 'ー';

    public static bool IsHiragana(char c) => c >= HiraganaFirst && c <= HiraganaLast;

    public static bool IsKatakana(char c) => c >= KatakanaFirst && c <= KatakanaLast;

    /// <summary>True for hiragana, katakana and ー.</summary>
    public static bool IsKana(char c) => IsHiragana(c) || IsKatakana(c) || c == LongVowelMark;

    public static bool IsKanji(char c) =>
        (c >= '一' && c <= '鿿')      // CJK Unified Ideographs
        || (c >= '㐀' && c <= '䶿')    // Extension A
        || c == '々';                  // iteration mark

    /// <summary>True when the text is non-empty and every character is kana.</summary>
    public static bool IsAllKana(string text) => text.Length > 0 && text.All(IsKana);

    /// <summary>Katakana → hiragana; everything else (including ー) is unchanged.</summary>
    public static string ToHiragana(string text)
    {
        StringBuilder sb = new(text.Length);
        foreach (char c in text)
        {
            sb.Append(IsKatakana(c) && c - KatakanaOffset <= HiraganaLast ? (char)(c - KatakanaOffset) : c);
        }
        return sb.ToString();
    }

    /// <summary>Hiragana → katakana; everything else is unchanged.</summary>
    public static string ToKatakana(string text)
    {
        StringBuilder sb = new(text.Length);
        foreach (char c in text)
        {
            sb.Append(IsHiragana(c) ? (char)(c + KatakanaOffset) : c);
        }
        return sb.ToString();
    }
}
