using KotoNeko.Core.Conjugation;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Ime;
using KotoNeko.Core.Text;
using Xunit;

namespace KotoNeko.Tests;

public class DeinflectorTests
{
    public static TheoryData<string, VerbClass> Verbs => new()
    {
        { "たべる", VerbClass.Ichidan },
        { "みる", VerbClass.Ichidan },
        { "かう", VerbClass.Godan },
        { "かく", VerbClass.Godan },
        { "およぐ", VerbClass.Godan },
        { "はなす", VerbClass.Godan },
        { "まつ", VerbClass.Godan },
        { "しぬ", VerbClass.Godan },
        { "あそぶ", VerbClass.Godan },
        { "よむ", VerbClass.Godan },
        { "かえる", VerbClass.Godan },
        { "いく", VerbClass.Godan },
        { "する", VerbClass.Suru },
        { "べんきょうする", VerbClass.Suru },
        { "くる", VerbClass.Kuru },
    };

    /// <summary>Every form the conjugation engine produces must lead back to its verb.</summary>
    [Theory]
    [MemberData(nameof(Verbs))]
    public void Every_Engine_Form_Deinflects_To_Its_Dictionary_Form(string dict, VerbClass cls)
    {
        WordType expected = cls switch
        {
            VerbClass.Ichidan => WordType.Ichidan,
            VerbClass.Godan => WordType.Godan,
            VerbClass.Suru => WordType.Suru,
            _ => WordType.Kuru,
        };

        foreach (ConjugationResult form in ConjugationEngine.Generate(dict, cls))
        {
            IReadOnlyList<Deinflection> results = Deinflector.Deinflect(form.Kana);
            Assert.True(
                results.Any(r => r.DictionaryForm == dict && (r.Types & expected) != 0),
                $"{form.Kana} ({form.Form} {form.Polarity}) did not deinflect to {dict}");
        }
    }

    [Theory]
    [InlineData("たべます", "たべる", WordType.Ichidan)]
    [InlineData("たべませんでした", "たべる", WordType.Ichidan)]
    [InlineData("たべたい", "たべる", WordType.Ichidan)]
    [InlineData("たべたくなかった", "たべる", WordType.Ichidan)]
    [InlineData("たべよう", "たべる", WordType.Ichidan)]
    [InlineData("たべれば", "たべる", WordType.Ichidan)]
    [InlineData("たべたら", "たべる", WordType.Ichidan)]
    [InlineData("たべている", "たべる", WordType.Ichidan)]
    [InlineData("たべてた", "たべる", WordType.Ichidan)]
    [InlineData("よみました", "よむ", WordType.Godan)]
    [InlineData("よもう", "よむ", WordType.Godan)]
    [InlineData("よんでいる", "よむ", WordType.Godan)]
    [InlineData("かければ", "かく", WordType.Godan)]
    [InlineData("きました", "くる", WordType.Kuru)]
    [InlineData("しなければ", "する", WordType.Suru)]
    [InlineData("たかかった", "たかい", WordType.IAdjective)]
    [InlineData("たかくない", "たかい", WordType.IAdjective)]
    [InlineData("たかくて", "たかい", WordType.IAdjective)]
    public void Forms_Outside_The_Engine_Deinflect(string inflected, string dict, WordType type)
    {
        Assert.Contains(Deinflector.Deinflect(inflected), r => r.DictionaryForm == dict && (r.Types & type) != 0);
    }

    [Theory]
    [InlineData("食べる", "たべる", "たべなかった", "食べなかった")]
    [InlineData("読む", "よむ", "よんだ", "読んだ")]
    [InlineData("行く", "いく", "いった", "行った")]
    [InlineData("勉強する", "べんきょうする", "べんきょうした", "勉強した")]
    [InlineData("高い", "たかい", "たかかった", "高かった")]
    [InlineData("来る", "くる", "きた", "来た")]
    [InlineData("来る", "くる", "こない", "来ない")]
    [InlineData("来る", "くる", "くれば", "来れば")]
    [InlineData("持って来る", "もってくる", "もってきた", "持って来た")]
    public void Reinflect_Applies_The_Change_To_The_Written_Form(string written, string dict, string inflected, string expected)
    {
        Assert.Equal(expected, Deinflector.Reinflect(written, dict, inflected));
    }
}

public class KanaKanjiConverterTests
{
    private static DictionaryWord W(string reading, string written, WordType type = WordType.None,
        int rank = 0, int order = 0, bool usuallyKana = false, bool particle = false) =>
        new(reading, written, type, rank, order, usuallyKana, particle);

    private static readonly DictionaryWord[] Words =
    {
        W("わたし", "私"),
        W("は", "は", particle: true),
        W("は", "歯", order: 1),
        W("は", "葉", order: 2),
        W("はし", "橋", order: 3),
        W("はし", "箸", order: 4),
        W("しんぶん", "新聞"),
        W("ぶん", "分", order: 5),
        W("を", "を", particle: true),
        W("よむ", "読む", WordType.Godan),
        W("こうえん", "公園", order: 6),
        W("こうえん", "講演", order: 7),
        W("こうえん", "公演", rank: 10, order: 8),
        W("こーひー", "コーヒー", usuallyKana: true),
        W("こーひー", "珈琲", rank: 13, usuallyKana: true),
        W("たべる", "食べる", WordType.Ichidan),
        W("べんきょうする", "勉強する", WordType.Suru),
        W("べんきょう", "勉強"),
        W("する", "する", WordType.Suru, usuallyKana: true),
        W("ある", "ある", WordType.Godan, usuallyKana: true),
        W("ある", "有る", WordType.Godan, rank: 3, usuallyKana: true),
    };

    private sealed class FakeLookup : IDictionaryLookup
    {
        private readonly IReadOnlyList<DictionaryWord> _words;

        public FakeLookup(IReadOnlyList<DictionaryWord>? words = null)
        {
            _words = words ?? Words;
        }

        public int Calls { get; private set; }

        public Task<IReadOnlyList<DictionaryWord>> LookupAsync(IReadOnlyCollection<string> readings)
        {
            Calls++;
            HashSet<string> wanted = new(readings, StringComparer.Ordinal);
            IReadOnlyList<DictionaryWord> found = _words.Where(w => wanted.Contains(w.Reading)).ToList();
            return Task.FromResult(found);
        }

        public Task<IReadOnlyList<DictionaryWord>> LookupWrittenAsync(IReadOnlyCollection<string> spellings)
        {
            HashSet<string> wanted = new(spellings, StringComparer.Ordinal);
            IReadOnlyList<DictionaryWord> found = _words.Where(w => wanted.Contains(w.Written)).ToList();
            return Task.FromResult(found);
        }
    }

    /// <summary>The fake dictionary, with some words marked as picked by the user before.</summary>
    private static FakeLookup WithChoices(params (string Written, DateTime At)[] chosen) =>
        new(Words.Select(w => chosen.FirstOrDefault(c => c.Written == w.Written) is { Written: not null } c
            ? w with { LastChosen = c.At }
            : w).ToList());

    private static readonly DateTime Earlier = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Past_Choices_Come_First_Most_Recent_First()
    {
        KanaKanjiConverter converter = new(WithChoices(("公演", Earlier), ("講演", Later)));
        IReadOnlyList<string> candidates = await converter.CandidatesAsync("こうえん");

        Assert.Equal(new[] { "講演", "公演", "公園" }, candidates.Take(3));
    }

    [Fact]
    public async Task A_Past_Choice_Applies_To_Every_Form_Of_The_Word()
    {
        DictionaryWord[] words =
        {
            W("よむ", "詠む", WordType.Godan),
            W("よむ", "読む", WordType.Godan, order: 1) with { LastChosen = Later },
        };
        KanaKanjiConverter converter = new(new FakeLookup(words));

        Assert.Equal("読みます", (await converter.CandidatesAsync("よみます"))[0]);
        Assert.Equal("読んだ", (await converter.CandidatesAsync("よんだ"))[0]);
    }

    [Fact]
    public async Task Past_Choices_Are_Ignored_When_Asked()
    {
        KanaKanjiConverter converter = new(WithChoices(("公演", Later)));
        IReadOnlyList<string> candidates = await converter.CandidatesAsync("こうえん", usePastChoices: false);

        Assert.Equal("公園", candidates[0]);
    }

    [Fact]
    public async Task A_Past_Choice_Overrides_The_Kana_Default_For_A_Particle()
    {
        KanaKanjiConverter converter = new(WithChoices(("歯", Later)));
        Assert.Equal("歯", (await converter.CandidatesAsync("は"))[0]);
    }

    [Theory]
    [InlineData("よんだ", "読んだ", "よむ", "読む")]
    [InlineData("べんきょうした", "勉強した", "べんきょうする", "勉強する")]
    [InlineData("こうえん", "コウエン", "こうえん", "コウエン")]
    public async Task Choices_Are_Remembered_As_The_Dictionary_Word(string kana, string written, string reading, string word)
    {
        KanaKanjiConverter converter = new(new FakeLookup());
        Assert.Equal((reading, word), await converter.ResolveChoiceAsync(kana, written));
    }

    private static async Task<List<(string Reading, string First)>> Convert(string kana)
    {
        KanaKanjiConverter converter = new(new FakeLookup());
        IReadOnlyList<ConversionSegment> segments = await converter.ConvertAsync(kana);
        return segments.Select(s => (s.Reading, s.Candidates[0])).ToList();
    }

    [Fact]
    public async Task Splits_A_Sentence_Where_Greedy_Matching_Would_Fail()
    {
        // Greedy longest-match would take はし (橋) and strand ん.
        var result = await Convert("わたしはしんぶんをよんだ");

        Assert.Equal(new[] { "私", "は", "新聞", "を", "読んだ" }, result.Select(r => r.First));
        Assert.Equal("わたしはしんぶんをよんだ", string.Concat(result.Select(r => r.Reading)));
    }

    [Fact]
    public async Task Particles_Default_To_Kana_But_Offer_Kanji()
    {
        KanaKanjiConverter converter = new(new FakeLookup());
        IReadOnlyList<string> candidates = await converter.CandidatesAsync("は");

        Assert.Equal("は", candidates[0]);
        Assert.Contains("歯", candidates);
        Assert.Contains("ハ", candidates);
    }

    [Fact]
    public async Task Homophones_Are_All_Offered_Common_First()
    {
        KanaKanjiConverter converter = new(new FakeLookup());
        IReadOnlyList<string> candidates = await converter.CandidatesAsync("こうえん");

        Assert.Equal(new[] { "公園", "講演", "公演", "こうえん", "コウエン" }, candidates);
    }

    [Fact]
    public async Task Long_Vowel_Kana_Finds_Katakana_Words_And_Prefers_Them()
    {
        KanaKanjiConverter converter = new(new FakeLookup());
        IReadOnlyList<ConversionSegment> result = await converter.ConvertAsync("こーひー");

        Assert.Equal(new[] { "コーヒー", "珈琲", "こーひー" }, Assert.Single(result).Candidates);
    }

    [Fact]
    public async Task Suru_Nouns_Inflect_As_One_Word()
    {
        var result = await Convert("べんきょうした");
        Assert.Equal("勉強した", Assert.Single(result).First);
    }

    [Fact]
    public async Task Usually_Kana_Words_Default_To_Kana()
    {
        var result = await Convert("あった");
        Assert.Equal("あった", Assert.Single(result).First);
    }

    [Fact]
    public async Task Unknown_Kana_Still_Converts_To_Itself()
    {
        var result = await Convert("ぬぽ");
        Assert.Equal(new[] { "ぬ", "ぽ" }, result.Select(r => r.First));
    }

    [Fact]
    public async Task One_Lookup_Covers_The_Whole_Conversion()
    {
        FakeLookup lookup = new();
        await new KanaKanjiConverter(lookup).ConvertAsync("わたしはしんぶんをよんだ");
        Assert.Equal(1, lookup.Calls);
    }

    [Fact]
    public void Kana_Conversions_Round_Trip()
    {
        Assert.Equal("こーひー", Kana.ToHiragana("コーヒー"));
        Assert.Equal("コーヒー", Kana.ToKatakana("こーひー"));
        Assert.Equal("新聞を", Kana.ToHiragana("新聞ヲ"));
    }
}

public class ConverterSplitTests
{
    private sealed class Lookup(DictionaryWord[] words) : IDictionaryLookup
    {
        public Task<IReadOnlyList<DictionaryWord>> LookupAsync(IReadOnlyCollection<string> readings) =>
            Task.FromResult<IReadOnlyList<DictionaryWord>>(words.Where(w => readings.Contains(w.Reading)).ToList());

        public Task<IReadOnlyList<DictionaryWord>> LookupWrittenAsync(IReadOnlyCollection<string> spellings) =>
            Task.FromResult<IReadOnlyList<DictionaryWord>>(words.Where(w => spellings.Contains(w.Written)).ToList());
    }

    [Fact]
    public async Task A_Particle_After_A_Word_Beats_Swallowing_It()
    {
        // ともだちとこうえん: 友達|と|公園, not 友達|とこう (渡航, or 解こう from 解く)|えん.
        DictionaryWord[] words =
        {
            new("ともだち", "友達", WordType.None, 0, 0),
            new("と", "と", WordType.None, 0, 1, ParticleLike: true),
            new("こうえん", "公園", WordType.None, 0, 2),
            new("とこう", "渡航", WordType.None, 0, 3),
            new("とく", "解く", WordType.Godan, 0, 4),
            new("えん", "円", WordType.None, 0, 5),
        };
        KanaKanjiConverter converter = new(new Lookup(words));

        IReadOnlyList<ConversionSegment> result = await converter.ConvertAsync("ともだちとこうえん");

        Assert.Equal(new[] { "友達", "と", "公園" }, result.Select(s => s.Candidates[0]));
    }

    [Fact]
    public async Task A_Phrase_Does_Not_Start_With_A_Particle()
    {
        // にほんの: にほん|の, not に|ほんの.
        DictionaryWord[] words =
        {
            new("にほん", "日本", WordType.None, 0, 0),
            new("に", "に", WordType.None, 0, 1, ParticleLike: true),
            new("ほんの", "ほんの", WordType.None, 0, 2),
            new("の", "の", WordType.None, 0, 3, ParticleLike: true),
        };
        KanaKanjiConverter converter = new(new Lookup(words));

        IReadOnlyList<ConversionSegment> result = await converter.ConvertAsync("にほんの");

        Assert.Equal(new[] { "日本", "の" }, result.Select(s => s.Candidates[0]));
    }
}
