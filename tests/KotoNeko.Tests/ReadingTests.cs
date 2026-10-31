using KotoNeko.Core.Ime;
using KotoNeko.Core.Text;
using Xunit;

namespace KotoNeko.Tests;

public class FuriganaTests
{
    [Theory]
    [InlineData("読んだ", "よんだ", "読[よ]んだ")]
    [InlineData("持って来る", "もってくる", "持[も]って来[く]る")]
    [InlineData("新聞", "しんぶん", "新聞[しんぶん]")]
    [InlineData("お茶", "おちゃ", "お茶[ちゃ]")]
    [InlineData("コーヒー", "こーひー", "コーヒー")]
    public void Align_Puts_Each_Reading_Over_Its_Own_Kanji(string text, string reading, string expected)
    {
        Assert.Equal(expected, Furigana.Format(Furigana.Align(text, reading)));
    }

    [Fact]
    public void Align_Falls_Back_To_The_Whole_Word_When_Kana_Do_Not_Match()
    {
        IReadOnlyList<FuriganaPiece> pieces = Furigana.Align("読んだ", "よみます");
        Assert.Equal("{読んだ}[よみます]", Furigana.Format(pieces));
    }

    [Theory]
    [InlineData("毎朝[まいあさ]新聞[しんぶん]を読[よ]みます。")]
    [InlineData("{持って来}[もってく]る")]
    [InlineData("ひらがなだけ")]
    public void Bracket_Format_Round_Trips(string bracketed)
    {
        IReadOnlyList<FuriganaPiece>? pieces = Furigana.Parse(bracketed);
        Assert.NotNull(pieces);
        Assert.Equal(bracketed, Furigana.Format(pieces));
    }

    [Fact]
    public void Parse_Gives_Plain_Text_And_Kana()
    {
        IReadOnlyList<FuriganaPiece> pieces = Furigana.Parse("毎朝[まいあさ]新聞[しんぶん]を読[よ]みます。")!;
        Assert.Equal("毎朝新聞を読みます。", Furigana.PlainText(pieces));
        Assert.Equal("まいあさしんぶんをよみます。", Furigana.KanaText(pieces));
    }

    [Theory]
    [InlineData("新聞[しんぶん")]
    [InlineData("を[を]")]
    [InlineData("新聞]")]
    [InlineData("{持って来[もってく]る")]
    public void Parse_Rejects_Malformed_Brackets(string bracketed)
    {
        Assert.Null(Furigana.Parse(bracketed));
    }

    [Fact]
    public void Readings_Are_Dropped_Over_Words_The_Reader_Knows()
    {
        IReadOnlyList<FuriganaPiece> pieces = Furigana.Parse("新聞[しんぶん]を読[よ]んだ")!;
        // 新聞 occupies 0–1, を is 2, 読んだ is 3–5.
        string? WordAt(int i) => i <= 1 ? "新聞" : i >= 3 ? "読む" : null;

        IReadOnlyList<FuriganaPiece> shown = Furigana.ForReader(pieces, WordAt, w => w == "新聞");

        Assert.Equal("新聞を読[よ]んだ", Furigana.Format(shown));
    }
}

public class SentenceReaderTests
{
    private static DictionaryWord W(string reading, string written, WordType type = WordType.None,
        int rank = 0, int order = 0, bool particle = false, bool vocabulary = false) =>
        new(reading, written, type, rank, order, ParticleLike: particle, FromVocabulary: vocabulary);

    private static readonly DictionaryWord[] Words =
    {
        W("まいあさ", "毎朝"),
        W("しんぶん", "新聞"),
        W("を", "を", particle: true),
        W("は", "は", particle: true),
        W("は", "は", rank: 5, order: 9),   // the kana spelling of 歯
        W("よむ", "読む", WordType.Godan),
        W("くる", "来る", WordType.Kuru),
        W("もってくる", "持って来る", WordType.Kuru),
        W("べんきょうする", "勉強する", WordType.Suru),
        W("たかい", "高い", WordType.IAdjective),
        W("きょう", "今日", order: 1),
        W("こんにち", "今日", rank: 10, order: 1),
        W("こーひー", "コーヒー"),
        W("のむ", "飲む", WordType.Godan),
        W("ほう", "方", order: 2),
        W("かた", "方", order: 3),
        W("かた", "方", vocabulary: true),
    };

    private sealed class Lookup : IDictionaryLookup
    {
        private readonly DictionaryWord[] _words;
        public Lookup(DictionaryWord[] words) { _words = words; }

        public Task<IReadOnlyList<DictionaryWord>> LookupAsync(IReadOnlyCollection<string> readings) =>
            Task.FromResult<IReadOnlyList<DictionaryWord>>(_words.Where(w => readings.Contains(w.Reading)).ToList());

        public Task<IReadOnlyList<DictionaryWord>> LookupWrittenAsync(IReadOnlyCollection<string> spellings) =>
            Task.FromResult<IReadOnlyList<DictionaryWord>>(_words.Where(w => spellings.Contains(w.Written)).ToList());
    }

    private static async Task<string> Furiganas(string text, DictionaryWord[]? words = null)
    {
        SentenceReader reader = new(new Lookup(words ?? Words));
        return Furigana.Format(SentenceReader.ToFurigana(await reader.ReadAsync(text)));
    }

    [Theory]
    [InlineData("毎朝新聞を読みます。", "毎朝[まいあさ]新聞[しんぶん]を読[よ]みます。")]
    [InlineData("新聞を読んだ", "新聞[しんぶん]を読[よ]んだ")]
    [InlineData("来た", "来[き]た")]
    [InlineData("来ない", "来[こ]ない")]
    [InlineData("持って来た", "持[も]って来[き]た")]
    [InlineData("勉強した", "勉強[べんきょう]した")]
    [InlineData("高かった", "高[たか]かった")]
    [InlineData("今日", "今日[きょう]")]
    [InlineData("コーヒーを飲んだ", "コーヒーを飲[の]んだ")]
    public async Task Readings_Come_From_The_Dictionary(string text, string expected)
    {
        Assert.Equal(expected, await Furiganas(text));
    }

    [Theory]
    [InlineData("新聞を読んじゃった", "新聞[しんぶん]を読[よ]んじゃった", "読む")]
    [InlineData("新聞を読んどく", "新聞[しんぶん]を読[よ]んどく", "読む")]
    [InlineData("新聞を読んでる", "新聞[しんぶん]を読[よ]んでる", "読む")]
    [InlineData("コーヒーを飲まなきゃ", "コーヒーを飲[の]まなきゃ", "飲む")]
    public async Task Casual_Contractions_Read_As_Their_Verb(string text, string expected, string lemma)
    {
        Assert.Equal(expected, await Furiganas(text));
        SentenceReader reader = new(new Lookup(Words));
        Assert.Contains(lemma, (await reader.ReadAsync(text)).Select(w => w.Lemma));
    }

    [Fact]
    public async Task Unknown_Kanji_Get_No_Reading()
    {
        Assert.Equal("新聞[しんぶん]を龘む", await Furiganas("新聞を龘む"));
    }

    [Fact]
    public async Task The_Users_Own_Vocabulary_Reading_Wins()
    {
        Assert.Equal("方[かた]", await Furiganas("方"));
    }

    [Fact]
    public async Task Words_Carry_Their_Dictionary_Form_And_Particles_Are_Marked()
    {
        SentenceReader reader = new(new Lookup(Words));
        IReadOnlyList<SentenceWord> words = await reader.ReadAsync("新聞は読んだ");

        Assert.Equal(new[] { "新聞", "は", "読む" }, words.Select(w => w.Lemma));
        Assert.True(words[1].IsParticle);
        Assert.Equal(new[] { 0, 2, 3 }, words.Select(w => w.Start));
    }
}

public class DictationComparerTests
{
    [Fact]
    public void Exact_Kana_Scores_Full_Marks_Ignoring_Punctuation_And_Script()
    {
        DictationComparison c = DictationComparer.Compare("まいあさ、しんぶんを よみます。", "マイアサしんぶんをよみます");
        Assert.Equal(1.0, c.Score);
        Assert.Equal(0, c.Errors);
        Assert.Equal(DiffKind.Same, Assert.Single(c.Parts).Kind);
    }

    [Fact]
    public void Missing_Wrong_And_Extra_Kana_Are_Shown()
    {
        // Expected しんぶんをよんだ; typed しぶんをよんだっ: missing ん, extra っ.
        DictationComparison c = DictationComparer.Compare("しんぶんをよんだ", "しぶんをよんだっ");

        Assert.Equal(2, c.Errors);
        Assert.Contains(c.Parts, p => p.Kind == DiffKind.Missing && p.Expected == "ん");
        Assert.Contains(c.Parts, p => p.Kind == DiffKind.Extra && p.Typed == "っ");
        Assert.Equal(1 - 2.0 / 8, c.Score, 3);
    }

    [Fact]
    public void Wrong_Kana_Is_Marked_Wrong()
    {
        DictationComparison c = DictationComparer.Compare("こうえん", "こうへん");
        Assert.Contains(c.Parts, p => p.Kind == DiffKind.Wrong && p.Expected == "え" && p.Typed == "へ");
    }

    [Fact]
    public void Particles_Typed_As_They_Sound_Are_Accepted_With_A_Note()
    {
        // わたしは (は at kana position 3 is a particle) typed as わたしわ.
        DictationComparison c = DictationComparer.Compare("わたしは", "わたしわ", new HashSet<int> { 3 });
        Assert.Equal(1.0, c.Score);
        Assert.Single(c.ParticleNotes);

        // The same は that is not a particle (はな) must still be heard as は.
        Assert.Equal(1, DictationComparer.Compare("はな", "わな").Errors);
    }

    [Fact]
    public void Speech_Maps_Particle_Positions_Into_The_Kana()
    {
        IReadOnlyList<FuriganaPiece> pieces = Furigana.Parse("私[わたし]は、学校[がっこう]へ")!;
        // In the text 私は、学校へ: は is at 1, へ at 5.
        var (kana, particles) = DictationComparer.Speech(pieces, new HashSet<int> { 1, 5 });

        Assert.Equal("わたしはがっこうへ", kana);
        Assert.Equal(new HashSet<int> { 3, 8 }, particles);
    }

    [Fact]
    public void Writing_Notes_Flag_Different_Kanji_And_Kana_Spellings()
    {
        var expected = new[] { ("新聞", "しんぶん"), ("を", "を"), ("会う", "あう"), ("友達", "ともだち") };
        var typed = new[] { ("しんぶん", "しんぶん"), ("を", "を"), ("合う", "あう"), ("友達", "ともだち") };

        IReadOnlyList<KanjiNote> notes = DictationComparer.CompareWriting(expected, typed);

        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.Kind == KanjiNoteKind.WrittenInKana && n.Expected == "新聞");
        Assert.Contains(notes, n => n.Kind == KanjiNoteKind.DifferentKanji && n.Typed == "合う");
    }
}

public class SentenceReaderKanaTextTests
{
    [Fact]
    public async Task Kana_Spellings_Of_Common_Words_Are_Not_Treated_As_Rare()
    {
        DictionaryWord[] words =
        {
            new("にほん", "日本", WordType.None, 0, 0),
            new("にほん", "にほん", WordType.None, 5, 1),   // kana spelling of a common kanji word
            new("に", "に", WordType.None, 0, 2, ParticleLike: true),
            new("ほんの", "ほんの", WordType.None, 0, 3),
            new("の", "の", WordType.None, 0, 4, ParticleLike: true),
        };
        SentenceReader reader = new(new Lookup(words));

        IReadOnlyList<SentenceWord> result = await reader.ReadAsync("にほんの");

        Assert.Equal(new[] { "にほん", "の" }, result.Select(w => w.Text));
    }

    private sealed class Lookup(DictionaryWord[] words) : IDictionaryLookup
    {
        public Task<IReadOnlyList<DictionaryWord>> LookupAsync(IReadOnlyCollection<string> readings) =>
            Task.FromResult<IReadOnlyList<DictionaryWord>>(words.Where(w => readings.Contains(w.Reading)).ToList());

        public Task<IReadOnlyList<DictionaryWord>> LookupWrittenAsync(IReadOnlyCollection<string> spellings) =>
            Task.FromResult<IReadOnlyList<DictionaryWord>>(words.Where(w => spellings.Contains(w.Written)).ToList());
    }
}
