using System.Text;

namespace KotoNeko.Core.Text;

/// <summary>
/// Converts romaji into hiragana, WaniKani-style. Designed for live conversion as
/// the user types: any trailing romaji that could still become a valid kana (e.g.
/// a lone "sh") is left untouched rather than mangled.
/// </summary>
public static class RomajiConverter
{
    private const string Vowels = "aeiou";

    // Longest-first lookup of romaji token -> hiragana.
    private static readonly Dictionary<string, string> Map = BuildMap();
    private static readonly int MaxKeyLength = Map.Keys.Max(k => k.Length);

    /// <summary>
    /// Convert a romaji string to hiragana. Already-kana characters pass through
    /// unchanged. Trailing partial romaji is preserved so live typing works.
    /// </summary>
    public static string ToHiragana(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        string lower = input.ToLowerInvariant();
        StringBuilder result = new();
        int i = 0;

        while (i < lower.Length)
        {
            char c = lower[i];

            // Pass through anything that is not a latin letter (kana, spaces, etc.).
            if (!IsLatinLetter(c))
            {
                result.Append(input[i]);
                i++;
                continue;
            }

            // Handle syllabic ん.
            if (c == 'n' && TryConsumeN(lower, i, out int nConsumed))
            {
                result.Append('ん');
                i += nConsumed;
                continue;
            }

            // Handle sokuon (small っ) from a doubled consonant, e.g. "kk" -> っk.
            if (c != 'n' && !IsVowel(c) && i + 1 < lower.Length && lower[i + 1] == c)
            {
                result.Append('っ');
                i++;
                continue;
            }

            // Longest token match.
            if (TryMatchToken(lower, i, out string kana, out int consumed))
            {
                result.Append(kana);
                i += consumed;
                continue;
            }

            // If the remaining tail is a prefix of some token, the user may still be
            // typing it: leave it (and everything after) as romaji.
            if (IsPrefixOfSomeKey(lower, i))
            {
                result.Append(input.Substring(i));
                break;
            }

            // Nothing matched: emit the original character verbatim.
            result.Append(input[i]);
            i++;
        }

        return result.ToString();
    }

    private static bool TryMatchToken(string s, int start, out string kana, out int consumed)
    {
        int max = Math.Min(MaxKeyLength, s.Length - start);
        for (int len = max; len >= 1; len--)
        {
            string candidate = s.Substring(start, len);
            if (Map.TryGetValue(candidate, out string? value))
            {
                kana = value;
                consumed = len;
                return true;
            }
        }

        kana = string.Empty;
        consumed = 0;
        return false;
    }

    private static bool TryConsumeN(string s, int i, out int consumed)
    {
        // 'n' is syllabic ん when followed by a consonant (not y), another 'n',
        // an apostrophe, or the end of input. Before a vowel or 'y' it is part of
        // na/ni/.../nya, so let token matching handle it.
        char next = i + 1 < s.Length ? s[i + 1] : '\0';

        if (next == '\0')
        {
            // Trailing lone 'n': treat as ん (user finished the word).
            consumed = 1;
            return true;
        }

        if (next == '\'')
        {
            consumed = 2;
            return true;
        }

        if (next == 'n')
        {
            // "nna"/"nni" -> ん + な/に (consume one n); bare "nn" -> single ん.
            char after = i + 2 < s.Length ? s[i + 2] : '\0';
            bool afterFormsSyllable = IsVowel(after) || after == 'y';
            consumed = afterFormsSyllable ? 1 : 2;
            return true;
        }

        if (IsLatinLetter(next) && !IsVowel(next) && next != 'y')
        {
            consumed = 1;
            return true;
        }

        consumed = 0;
        return false;
    }

    private static bool IsPrefixOfSomeKey(string s, int start)
    {
        string tail = s.Substring(start);
        foreach (string key in Map.Keys)
        {
            if (key.Length > tail.Length && key.StartsWith(tail, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLatinLetter(char c) => c is >= 'a' and <= 'z';

    private static bool IsVowel(char c) => Vowels.IndexOf(c) >= 0;

    private static Dictionary<string, string> BuildMap()
    {
        Dictionary<string, string> m = new(StringComparer.Ordinal)
        {
            // Vowels
            ["a"] = "あ", ["i"] = "い", ["u"] = "う", ["e"] = "え", ["o"] = "お",
            // K
            ["ka"] = "か", ["ki"] = "き", ["ku"] = "く", ["ke"] = "け", ["ko"] = "こ",
            ["kya"] = "きゃ", ["kyu"] = "きゅ", ["kyo"] = "きょ",
            // G
            ["ga"] = "が", ["gi"] = "ぎ", ["gu"] = "ぐ", ["ge"] = "げ", ["go"] = "ご",
            ["gya"] = "ぎゃ", ["gyu"] = "ぎゅ", ["gyo"] = "ぎょ",
            // S
            ["sa"] = "さ", ["shi"] = "し", ["si"] = "し", ["su"] = "す", ["se"] = "せ", ["so"] = "そ",
            ["sha"] = "しゃ", ["shu"] = "しゅ", ["sho"] = "しょ",
            ["sya"] = "しゃ", ["syu"] = "しゅ", ["syo"] = "しょ",
            // Z / J
            ["za"] = "ざ", ["ji"] = "じ", ["zi"] = "じ", ["zu"] = "ず", ["ze"] = "ぜ", ["zo"] = "ぞ",
            ["ja"] = "じゃ", ["ju"] = "じゅ", ["jo"] = "じょ",
            ["jya"] = "じゃ", ["jyu"] = "じゅ", ["jyo"] = "じょ",
            // T
            ["ta"] = "た", ["chi"] = "ち", ["ti"] = "ち", ["tsu"] = "つ", ["tu"] = "つ", ["te"] = "て", ["to"] = "と",
            ["cha"] = "ちゃ", ["chu"] = "ちゅ", ["cho"] = "ちょ",
            ["tya"] = "ちゃ", ["tyu"] = "ちゅ", ["tyo"] = "ちょ",
            // D
            ["da"] = "だ", ["di"] = "ぢ", ["du"] = "づ", ["de"] = "で", ["do"] = "ど",
            ["dya"] = "ぢゃ", ["dyu"] = "ぢゅ", ["dyo"] = "ぢょ",
            // N
            ["na"] = "な", ["ni"] = "に", ["nu"] = "ぬ", ["ne"] = "ね", ["no"] = "の",
            ["nya"] = "にゃ", ["nyu"] = "にゅ", ["nyo"] = "にょ",
            // H / F
            ["ha"] = "は", ["hi"] = "ひ", ["fu"] = "ふ", ["hu"] = "ふ", ["he"] = "へ", ["ho"] = "ほ",
            ["hya"] = "ひゃ", ["hyu"] = "ひゅ", ["hyo"] = "ひょ",
            ["fa"] = "ふぁ", ["fi"] = "ふぃ", ["fe"] = "ふぇ", ["fo"] = "ふぉ",
            // B
            ["ba"] = "ば", ["bi"] = "び", ["bu"] = "ぶ", ["be"] = "べ", ["bo"] = "ぼ",
            ["bya"] = "びゃ", ["byu"] = "びゅ", ["byo"] = "びょ",
            // P
            ["pa"] = "ぱ", ["pi"] = "ぴ", ["pu"] = "ぷ", ["pe"] = "ぺ", ["po"] = "ぽ",
            ["pya"] = "ぴゃ", ["pyu"] = "ぴゅ", ["pyo"] = "ぴょ",
            // M
            ["ma"] = "ま", ["mi"] = "み", ["mu"] = "む", ["me"] = "め", ["mo"] = "も",
            ["mya"] = "みゃ", ["myu"] = "みゅ", ["myo"] = "みょ",
            // Y
            ["ya"] = "や", ["yu"] = "ゆ", ["yo"] = "よ",
            // R
            ["ra"] = "ら", ["ri"] = "り", ["ru"] = "る", ["re"] = "れ", ["ro"] = "ろ",
            ["rya"] = "りゃ", ["ryu"] = "りゅ", ["ryo"] = "りょ",
            // W
            ["wa"] = "わ", ["wo"] = "を", ["wi"] = "うぃ", ["we"] = "うぇ",
            // V
            ["va"] = "ゔぁ", ["vi"] = "ゔぃ", ["vu"] = "ゔ", ["ve"] = "ゔぇ", ["vo"] = "ゔぉ",
            // Small kana via x/l prefix
            ["xa"] = "ぁ", ["xi"] = "ぃ", ["xu"] = "ぅ", ["xe"] = "ぇ", ["xo"] = "ぉ",
            ["la"] = "ぁ", ["li"] = "ぃ", ["lu"] = "ぅ", ["le"] = "ぇ", ["lo"] = "ぉ",
            ["xya"] = "ゃ", ["xyu"] = "ゅ", ["xyo"] = "ょ",
            ["xtsu"] = "っ", ["xtu"] = "っ", ["ltu"] = "っ",
            ["-"] = "ー",
        };

        return m;
    }
}
