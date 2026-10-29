using KotoNeko.Core.Domain;
using KotoNeko.Data.Dictionary;
using KotoNeko.Data.Vocab;
using Xunit;

namespace KotoNeko.Tests;

public class KnownWordsTests
{
    private static Vocabulary Word(bool asleep, SrsStage? stage, bool doNotUse = false) => new() {
        Japanese = "会う",
        Reading = "あう",
        IsAsleep = asleep,
        DoNotUse = doNotUse,
        Srs = stage is null ? null : new SrsItem { Stage = stage.Value },
    };

    [Theory]
    [InlineData(true, null, false, SrsStage.Guru1, true)]                 // asleep, never studied: a known word
    [InlineData(true, null, true, SrsStage.Guru1, false)]                 // ...unless marked do not use
    [InlineData(false, null, false, SrsStage.Guru1, false)]               // awaiting its lesson
    [InlineData(false, SrsStage.Apprentice4, false, SrsStage.Guru1, false)]
    [InlineData(false, SrsStage.Guru1, false, SrsStage.Guru1, true)]
    [InlineData(false, SrsStage.Guru2, false, SrsStage.Master, false)]
    [InlineData(false, SrsStage.Burned, false, SrsStage.Master, true)]
    [InlineData(true, SrsStage.Guru1, false, SrsStage.Guru1, true)]       // studied, then put to sleep
    [InlineData(true, SrsStage.Apprentice1, false, SrsStage.Guru1, false)]
    [InlineData(false, SrsStage.Master, true, SrsStage.Guru1, false)]
    public void The_Pool_Rule(bool asleep, SrsStage? stage, bool doNotUse, SrsStage threshold, bool inPool)
    {
        Vocabulary word = Word(asleep, stage, doNotUse);
        Assert.Equal(inPool, KnownWords.IsInPool(word, threshold));
        Assert.Equal(inPool, new[] { word }.AsQueryable().InPool(threshold).Any());
    }

    [Fact]
    public async Task A_Dictionary_Verb_Becomes_An_Asleep_Word_With_Conjugations()
    {
        var (entries, meanings) = await DictionaryImportTests.Dictionary();
        DictionaryWordMatch match = Assert.Single(DictionaryWords.Match("勉強する", entries, meanings));

        Vocabulary word = KnownWords.FromDictionary(match, DateTime.UtcNow);

        Assert.True(word.IsAsleep);
        Assert.False(word.DoNotUse);
        Assert.Equal("べんきょうする", word.Reading);
        Assert.Equal(VerbClass.Suru, word.VerbClass);
        Assert.Equal(14, word.Conjugations.Count);
        Assert.Equal("study", word.PrimaryMeaning);
        Assert.True(KnownWords.IsInPool(word, SrsStage.Guru1));
    }
}

public class JlptWordListTests
{
    private const string Header = "jmdict_seq,kana,kanji,waller_definition";

    [Fact]
    public void Parses_Quoted_Commas_Kana_Words_A_Bom_And_Crlf()
    {
        string csv = "﻿" + Header + "\r\n1,あう,会う,to meet\r\n3,コーヒー,,coffee\r\n9,あく,開く,\"to open, to become \"\"open\"\"\"\r\n\r\n";

        List<JlptListWord> words = JlptWordLists.Parse(csv, JlptLevel.N5);

        Assert.Equal(3, words.Count);
        Assert.Equal("会う", words[0].Written);
        Assert.Equal("コーヒー", words[1].Written);
        Assert.Equal("to open, to become \"open\"", words[2].Definition);
        Assert.All(words, w => Assert.Equal(JlptLevel.N5, w.Level));
    }

    [Fact]
    public void Rejects_A_File_That_Is_Not_A_List()
    {
        Assert.Throws<InvalidDataException>(() => JlptWordLists.Parse("<html>Not found</html>", JlptLevel.N4));
        Assert.Throws<InvalidDataException>(() => JlptWordLists.Parse(Header + "\nabc,あう,会う,to meet\n", JlptLevel.N4));
    }

    private static async Task<JlptSeedPlan> Plan(IEnumerable<JlptListWord> words, params ExistingWord[] existing)
    {
        var (entries, meanings) = await DictionaryImportTests.Dictionary();
        return JlptWordLists.Plan(words, existing, entries, meanings, DateTime.UtcNow);
    }

    [Fact]
    public async Task A_List_Word_Comes_From_The_Dictionary_Asleep()
    {
        JlptSeedPlan plan = await Plan(new[] { new JlptListWord(1, "あう", "会う", "to meet (list)", JlptLevel.N5) });

        Vocabulary word = Assert.Single(plan.New);
        Assert.Equal("会う", word.Japanese);
        Assert.Equal("あう", word.Reading);
        Assert.Equal(VerbClass.Godan, word.VerbClass);
        Assert.Equal(14, word.Conjugations.Count);
        Assert.Equal("to meet", word.PrimaryMeaning);   // JMdict's meaning, not the list's
        Assert.True(word.IsAsleep);
        Assert.False(word.DoNotUse);
        Assert.Equal(JlptLevel.N5, word.JlptLevel);
        Assert.False(word.HasWordAudio);
    }

    [Fact]
    public async Task N3_Words_Start_As_Do_Not_Use()
    {
        JlptSeedPlan plan = await Plan(new[] {
            new JlptListWord(1, "あう", "会う", "to meet", JlptLevel.N4),
            new JlptListWord(2, "べんきょう", "勉強", "study", JlptLevel.N3),
        });

        Assert.False(plan.New.Single(w => w.Japanese == "会う").DoNotUse);
        Assert.True(plan.New.Single(w => w.Japanese == "勉強").DoNotUse);
    }

    [Fact]
    public async Task A_Word_On_Two_Lists_Takes_The_Easier_Level()
    {
        JlptSeedPlan plan = await Plan(new[] {
            new JlptListWord(1, "あう", "会う", "to meet", JlptLevel.N3),
            new JlptListWord(1, "あう", "会う", "to meet", JlptLevel.N5),
        });

        Vocabulary word = Assert.Single(plan.New);
        Assert.Equal(JlptLevel.N5, word.JlptLevel);
        Assert.False(word.DoNotUse);
    }

    [Fact]
    public async Task Rows_Sharing_An_Entry_And_Spelling_Become_One_Word_With_Alternate_Readings()
    {
        JlptSeedPlan plan = await Plan(new[] {
            new JlptListWord(4, "きょう", "今日", "today", JlptLevel.N5),
            new JlptListWord(4, "こんにち", "今日", "these days", JlptLevel.N4),
        });

        Vocabulary word = Assert.Single(plan.New);
        Assert.Equal("きょう", word.Reading);
        Assert.Equal("こんにち", word.AlternateReadings);
        Assert.Equal(JlptLevel.N5, word.JlptLevel);
    }

    [Fact]
    public async Task A_Kana_Word_Keeps_Its_Own_Script()
    {
        Vocabulary word = Assert.Single((await Plan(new[] { new JlptListWord(3, "コーヒー", "", "coffee", JlptLevel.N5) })).New);
        Assert.Equal("コーヒー", word.Japanese);
        Assert.Equal("コーヒー", word.Reading);
        Assert.Equal("coffee", word.PrimaryMeaning);
    }

    [Fact]
    public async Task Existing_Words_Are_Skipped_But_Get_An_Empty_Or_Harder_Level_Filled_In()
    {
        JlptSeedPlan plan = await Plan(
            new[] {
                new JlptListWord(1, "あう", "会う", "to meet", JlptLevel.N5),
                new JlptListWord(3, "コーヒー", "", "coffee", JlptLevel.N4),
                new JlptListWord(2, "べんきょう", "勉強", "study", JlptLevel.N4),
            },
            new ExistingWord(10, "会う", "あう", null),
            new ExistingWord(11, "コーヒー", "コーヒー", JlptLevel.N5),
            new ExistingWord(12, "勉強", "ベンキョウ", JlptLevel.N3));

        Assert.Empty(plan.New);
        Assert.Equal(3, plan.AlreadyThere);
        Assert.Equal(JlptLevel.N5, plan.LevelUpdates[10]);
        Assert.False(plan.LevelUpdates.ContainsKey(11));   // already easier
        Assert.Equal(JlptLevel.N4, plan.LevelUpdates[12]); // readings compare as hiragana
    }

    [Fact]
    public async Task A_Spelling_The_Entry_Lacks_Becomes_Its_Usual_Spelling()
    {
        // As the lists write 明い for JMdict's 明るい.
        Vocabulary word = Assert.Single((await Plan(new[] { new JlptListWord(1, "あう", "遭う", "to meet", JlptLevel.N5) })).New);
        Assert.Equal("会う", word.Japanese);
        Assert.Equal(VerbClass.Godan, word.VerbClass);
    }

    [Fact]
    public async Task A_Renumbered_Entry_Is_Found_By_Spelling_And_Reading()
    {
        JlptSeedPlan plan = await Plan(new[] { new JlptListWord(77, "べんきょう", "勉強", "(list's own)", JlptLevel.N5) });

        Vocabulary word = Assert.Single(plan.New);
        Assert.Equal(0, plan.NotInDictionary);
        Assert.Equal("study", word.PrimaryMeaning);
    }

    [Fact]
    public async Task A_Respelled_Word_Already_Here_Is_Recognised()
    {
        JlptSeedPlan plan = await Plan(new[] { new JlptListWord(1, "あう", "遭う", "to meet", JlptLevel.N5) },
            new ExistingWord(10, "会う", "あう", null));

        Assert.Empty(plan.New);
        Assert.Equal(JlptLevel.N5, plan.LevelUpdates[10]);
    }

    [Fact]
    public async Task A_Word_Missing_From_The_Dictionary_Uses_The_Lists_Definition()
    {
        JlptSeedPlan plan = await Plan(new[] { new JlptListWord(999, "ふしぎ", "不思議", "mystery, wonder", JlptLevel.N4) });

        Vocabulary word = Assert.Single(plan.New);
        Assert.Equal(1, plan.NotInDictionary);
        Assert.Equal("ふしぎ", word.Reading);
        Assert.Equal(new[] { "mystery", "wonder" }, word.Meanings.OrderBy(m => m.SortOrder).Select(m => m.Text));
        Assert.Equal(VerbClass.None, word.VerbClass);
    }
}
