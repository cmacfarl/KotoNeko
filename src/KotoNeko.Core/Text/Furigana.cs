using System.Text;

namespace KotoNeko.Core.Text;

/// <summary>A run of text, with its reading when furigana should go above it.</summary>
public sealed record FuriganaPiece(string Text, string? Reading);

/// <summary>
/// Furigana helpers: lining a word's reading up with its kanji, and the editable
/// bracket format. In the bracket format a reading follows the kanji it belongs
/// to: 毎朝[まいあさ]新聞[しんぶん]を読[よ]みます. The base is the run of kanji right
/// before "["; a base that mixes kanji and kana goes in braces: {持って来}[もってく]る.
/// </summary>
public static class Furigana
{
    /// <summary>
    /// Split a word into pieces so each reading sits over its own kanji:
    /// 読んだ + よんだ → 読[よ] んだ; 持って来る + もってくる → 持[も] って 来[く] る.
    /// Kana in the word must appear in the reading. If they can't be lined up,
    /// the whole word gets the whole reading.
    /// </summary>
    public static IReadOnlyList<FuriganaPiece> Align(string text, string reading)
    {
        List<(string Run, bool Kanji)> runs = Runs(text);
        if (!runs.Any(r => r.Kanji)) {
            return new[] { new FuriganaPiece(text, null) };
        }

        string key = Kana.ToHiragana(reading);
        List<FuriganaPiece> pieces = new();
        if (Match(runs, 0, key, 0, pieces)) {
            return pieces;
        }
        return new[] { new FuriganaPiece(text, reading) };
    }

    private static bool Match(List<(string Run, bool Kanji)> runs, int r, string reading, int at, List<FuriganaPiece> pieces)
    {
        if (r == runs.Count) {
            return at == reading.Length;
        }

        (string run, bool kanji) = runs[r];
        if (!kanji) {
            string literal = Kana.ToHiragana(run);
            if (at + literal.Length > reading.Length
                || string.CompareOrdinal(reading, at, literal, 0, literal.Length) != 0) {
                return false;
            }
            pieces.Add(new FuriganaPiece(run, null));
            if (Match(runs, r + 1, reading, at + literal.Length, pieces)) {
                return true;
            }
            pieces.RemoveAt(pieces.Count - 1);
            return false;
        }

        // A kanji run reads as at least one kana; try the shortest first.
        for (int end = at + 1; end <= reading.Length; end++) {
            pieces.Add(new FuriganaPiece(run, reading[at..end]));
            if (Match(runs, r + 1, reading, end, pieces)) {
                return true;
            }
            pieces.RemoveAt(pieces.Count - 1);
        }
        return false;
    }

    private static List<(string Run, bool Kanji)> Runs(string text)
    {
        List<(string, bool)> runs = new();
        int start = 0;
        for (int i = 1; i <= text.Length; i++) {
            if (i == text.Length || Kana.IsKanji(text[i]) != Kana.IsKanji(text[start])) {
                runs.Add((text[start..i], Kana.IsKanji(text[start])));
                start = i;
            }
        }
        return runs;
    }

    /// <summary>Write pieces in the bracket format.</summary>
    public static string Format(IEnumerable<FuriganaPiece> pieces)
    {
        StringBuilder sb = new();
        foreach (FuriganaPiece p in pieces) {
            if (p.Reading is null) {
                sb.Append(p.Text);
            } else if (p.Text.All(Kana.IsKanji)) {
                sb.Append(p.Text).Append('[').Append(p.Reading).Append(']');
            } else {
                sb.Append('{').Append(p.Text).Append("}[").Append(p.Reading).Append(']');
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Read the bracket format back into pieces. Null if it is malformed (an
    /// unclosed bracket, or a reading with no kanji or braces before it).
    /// </summary>
    public static IReadOnlyList<FuriganaPiece>? Parse(string bracketed)
    {
        List<FuriganaPiece> pieces = new();
        StringBuilder plain = new();
        int i = 0;
        while (i < bracketed.Length) {
            char c = bracketed[i];
            if (c == '{') {
                int close = bracketed.IndexOf('}', i);
                if (close < 0 || close + 1 >= bracketed.Length || bracketed[close + 1] != '[') {
                    return null;
                }
                int end = bracketed.IndexOf(']', close);
                if (end < 0) {
                    return null;
                }
                Flush();
                pieces.Add(new FuriganaPiece(bracketed[(i + 1)..close], bracketed[(close + 2)..end]));
                i = end + 1;
            } else if (c == '[') {
                int end = bracketed.IndexOf(']', i);
                int baseStart = plain.Length;
                while (baseStart > 0 && Kana.IsKanji(plain[baseStart - 1])) {
                    baseStart--;
                }
                if (end < 0 || baseStart == plain.Length) {
                    return null;
                }
                string baseText = plain.ToString(baseStart, plain.Length - baseStart);
                plain.Length = baseStart;
                Flush();
                pieces.Add(new FuriganaPiece(baseText, bracketed[(i + 1)..end]));
                i = end + 1;
            } else if (c is ']' or '}') {
                return null;
            } else {
                plain.Append(c);
                i++;
            }
        }
        Flush();
        return pieces;

        void Flush() {
            if (plain.Length > 0) {
                pieces.Add(new FuriganaPiece(plain.ToString(), null));
                plain.Clear();
            }
        }
    }

    /// <summary>The text without its readings.</summary>
    public static string PlainText(IEnumerable<FuriganaPiece> pieces) => string.Concat(pieces.Select(p => p.Text));

    /// <summary>The whole text in hiragana: readings in place of the kanji they cover.</summary>
    public static string KanaText(IEnumerable<FuriganaPiece> pieces) =>
        Kana.ToHiragana(string.Concat(pieces.Select(p => p.Reading ?? p.Text)));

    /// <summary>
    /// Keep furigana only where the reader needs it: drop the reading from any
    /// piece that starts inside a word <paramref name="isKnown"/> says the reader
    /// knows. <paramref name="wordAt"/> gives the word (by its dictionary form)
    /// covering a character position, or null.
    /// </summary>
    public static IReadOnlyList<FuriganaPiece> ForReader(
        IReadOnlyList<FuriganaPiece> pieces, Func<int, string?> wordAt, Func<string, bool> isKnown)
    {
        List<FuriganaPiece> result = new(pieces.Count);
        int position = 0;
        foreach (FuriganaPiece p in pieces) {
            bool hide = p.Reading is not null && wordAt(position) is string word && isKnown(word);
            result.Add(hide ? p with { Reading = null } : p);
            position += p.Text.Length;
        }
        return result;
    }
}
