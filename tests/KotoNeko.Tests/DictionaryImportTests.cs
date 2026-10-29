using System.IO.Compression;
using System.Text;
using System.Text.Json;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Ime;
using KotoNeko.Data.Dictionary;
using Xunit;

namespace KotoNeko.Tests;

public class DictionaryImportTests
{
    // A small file in the jmdict-simplified shape, with header values to skip.
    internal const string SampleJson = """
        {
          "version": "3.6.2",
          "languages": ["eng"],
          "commonOnly": false,
          "dictDate": "2026-10-26",
          "dictRevisions": ["1.09"],
          "tags": { "v5u": "Godan verb with 'u' ending", "uk": "usually kana" },
          "words": [
            {
              "id": "1",
              "kanji": [ { "common": true, "text": "会う", "tags": [] }, { "common": false, "text": "遇う", "tags": ["rK"] } ],
              "kana": [ { "common": true, "text": "あう", "tags": [], "appliesToKanji": ["*"] } ],
              "sense": [
                { "partOfSpeech": ["v5u", "vi"], "misc": [], "gloss": [ { "lang": "eng", "text": "to meet" } ] },
                { "partOfSpeech": ["v5u", "vi"], "misc": ["uk"], "gloss": [ { "lang": "eng", "text": "to encounter" } ] }
              ]
            },
            {
              "id": "2",
              "kanji": [ { "common": true, "text": "勉強", "tags": [] } ],
              "kana": [ { "common": true, "text": "べんきょう", "tags": [], "appliesToKanji": ["*"] } ],
              "sense": [ { "partOfSpeech": ["n", "vs", "vt"], "misc": [], "gloss": [ { "lang": "eng", "text": "study" } ] } ]
            },
            {
              "id": "3",
              "kanji": [ { "common": true, "text": "珈琲", "tags": ["ateji"] }, { "common": false, "text": "咖啡", "tags": ["sK"] } ],
              "kana": [ { "common": true, "text": "コーヒー", "tags": [], "appliesToKanji": ["*"] } ],
              "sense": [ { "partOfSpeech": ["n"], "misc": ["uk"], "gloss": [ { "lang": "eng", "text": "coffee" } ] } ]
            },
            {
              "id": "4",
              "kanji": [ { "common": true, "text": "今日", "tags": [] } ],
              "kana": [
                { "common": true, "text": "きょう", "tags": [], "appliesToKanji": ["*"] },
                { "common": false, "text": "こんにち", "tags": [], "appliesToKanji": ["今日"] }
              ],
              "sense": [
                { "partOfSpeech": ["n", "adv"], "misc": [], "gloss": [ { "lang": "eng", "text": "today" }, { "lang": "eng", "text": "this day" } ] },
                { "partOfSpeech": ["n"], "misc": [], "appliesToKana": ["こんにち"], "gloss": [ { "lang": "eng", "text": "these days" }, { "lang": "ger", "text": "heutzutage" } ] }
              ]
            }
          ]
        }
        """;

    internal static async Task<(JmdictReader Reader, List<JmdictWord> Words)> Read(int bufferSize)
    {
        JmdictReader reader = new(bufferSize);
        List<JmdictWord> words = new();
        await reader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(SampleJson)), words.Add);
        return (reader, words);
    }

    [Theory]
    [InlineData(16)]       // forces a refill and a buffer resize inside nearly every token
    [InlineData(100)]
    [InlineData(1 << 20)]  // the whole file in one buffer
    public async Task Reader_Streams_Every_Word_Whatever_The_Buffer_Size(int bufferSize)
    {
        var (reader, words) = await Read(bufferSize);

        Assert.Equal("3.6.2", reader.Version);
        Assert.Equal("2026-10-26", reader.DictDate);
        Assert.Equal(new[] { "会う", "勉強", "珈琲", "今日" }, words.Select(w => w.Kanji[0].Text));
        Assert.Equal("v5u", words[0].Sense[0].PartOfSpeech[0]);
    }

    [Fact]
    public async Task Reader_Rejects_A_Truncated_File()
    {
        byte[] cut = Encoding.UTF8.GetBytes(SampleJson[..(SampleJson.Length / 2)]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new JmdictReader(64).ReadAsync(new MemoryStream(cut), _ => { }));
    }

    private static async Task<List<DictionaryEntry>> Entries() =>
        JmdictImporter.BuildEntries((await Read(1 << 20)).Words);

    [Fact]
    public async Task Minor_Usually_Kana_Sense_Does_Not_Make_The_Word_Kana()
    {
        List<DictionaryEntry> entries = await Entries();
        DictionaryEntry kanji = entries.Single(e => e.Written == "会う");
        DictionaryEntry kana = entries.Single(e => e.Written == "あう");

        Assert.False(kanji.UsuallyKana);
        Assert.Equal(WordType.Godan, kanji.Type);
        Assert.True(kanji.Rank < kana.Rank);
        Assert.True(entries.Single(e => e.Written == "遇う").Rank > kanji.Rank);
    }

    [Fact]
    public async Task Suru_Nouns_Get_A_Suru_Verb_Entry()
    {
        DictionaryEntry verb = (await Entries()).Single(e => e.Written == "勉強する");
        Assert.Equal("べんきょうする", verb.Reading);
        Assert.Equal(WordType.Suru, verb.Type);
    }

    [Fact]
    public async Task Katakana_Readings_Are_Keyed_In_Hiragana_And_Search_Only_Spellings_Dropped()
    {
        List<DictionaryEntry> entries = await Entries();
        DictionaryEntry coffee = entries.Single(e => e.Written == "コーヒー");

        Assert.Equal("こーひー", coffee.Reading);
        Assert.True(coffee.Rank < entries.Single(e => e.Written == "珈琲").Rank);
        Assert.DoesNotContain(entries, e => e.Written == "咖啡");
    }

    [Fact]
    public async Task Readings_Pair_Only_With_The_Spellings_They_Apply_To()
    {
        List<DictionaryEntry> entries = await Entries();
        Assert.Contains(entries, e => e.Reading == "こんにち" && e.Written == "今日");
        Assert.Contains(entries, e => e.Reading == "きょう" && e.Written == "今日");
    }

    internal static async Task<(List<DictionaryEntry> Entries, Dictionary<int, DictionaryMeaning> Meanings)> Dictionary()
    {
        List<JmdictWord> words = (await Read(1 << 20)).Words;
        return (JmdictImporter.BuildEntries(words), JmdictImporter.BuildMeanings(words).ToDictionary(m => m.Id));
    }

    private static async Task<DictionaryWordMatch> Find(string written)
    {
        var (entries, meanings) = await Dictionary();
        return Assert.Single(DictionaryWords.Match(written, entries, meanings));
    }

    [Fact]
    public async Task Words_Are_Keyed_By_Their_JMdict_Entry_Number()
    {
        var (entries, meanings) = await Dictionary();

        Assert.Equal(1, entries.Single(e => e.Written == "会う").WordId);
        Assert.Equal(2, entries.Single(e => e.Written == "勉強する").WordId);
        Assert.Equal(new[] { 1, 2, 3, 4 }, meanings.Keys.Order());
        // Order still follows the file, for tie-breaks.
        Assert.True(entries.Single(e => e.Written == "会う").Order < entries.Single(e => e.Written == "勉強").Order);
    }

    [Fact]
    public async Task A_Word_Without_A_Numeric_Id_Is_Rejected()
    {
        List<JmdictWord> words = (await Read(1 << 20)).Words;
        words[0].Id = "";
        Assert.Throws<InvalidDataException>(() => JmdictImporter.BuildEntries(words));
    }

    [Fact]
    public async Task Every_Entry_Points_At_Its_Words_Meanings()
    {
        var (entries, meanings) = await Dictionary();

        Assert.All(entries, e => Assert.True(meanings.ContainsKey(e.WordId)));
        Assert.Equal(entries.Single(e => e.Written == "会う").WordId, entries.Single(e => e.Written == "あう").WordId);
        Assert.NotEqual(entries.Single(e => e.Written == "会う").WordId, entries.Single(e => e.Written == "勉強").WordId);
    }

    [Fact]
    public async Task A_Verb_Comes_With_Its_Reading_Class_And_Main_Meaning()
    {
        DictionaryWordMatch word = await Find("会う");

        Assert.Equal("あう", word.Reading);
        Assert.Equal(VerbClass.Godan, word.VerbClass);
        Assert.Equal(new[] { "to meet" }, word.MainMeanings);
        Assert.Equal(2, word.Senses.Count);
    }

    [Fact]
    public async Task Senses_Limited_To_Another_Reading_Are_Left_Out()
    {
        DictionaryWordMatch word = await Find("今日");

        Assert.Equal("きょう", word.Reading);
        Assert.Equal(new[] { "こんにち" }, word.OtherReadings);
        Assert.Equal(new[] { "today", "this day" }, Assert.Single(word.Senses).Glosses);
    }

    [Fact]
    public async Task A_Suru_Verb_Takes_The_Nouns_Meanings()
    {
        DictionaryWordMatch word = await Find("勉強する");

        Assert.Equal("べんきょうする", word.Reading);
        Assert.Equal(VerbClass.Suru, word.VerbClass);
        Assert.Equal(new[] { "study" }, word.MainMeanings);
    }

    [Fact]
    public async Task Kana_Words_Keep_Their_Own_Script_As_The_Reading()
    {
        DictionaryWordMatch word = await Find("コーヒー");
        Assert.Equal("コーヒー", word.Reading);
        Assert.Equal(new[] { "coffee" }, word.MainMeanings);
    }

    [Fact]
    public async Task Only_English_Glosses_Are_Kept()
    {
        var (_, meanings) = await Dictionary();
        Assert.DoesNotContain(meanings.Values, m => m.Senses.Contains("heutzutage"));
    }

    [Fact]
    public void Picks_The_Full_English_Zip_From_A_Release()
    {
        const string release = """
            {
              "tag_name": "3.6.2+20261026175559",
              "assets": [
                { "name": "jmdict-eng-common-3.6.2+20261026175559.json.zip", "browser_download_url": "https://x/common.zip", "size": 1 },
                { "name": "jmdict-eng-3.6.2+20261026175559.json.tgz", "browser_download_url": "https://x/full.tgz", "size": 2 },
                { "name": "jmdict-eng-3.6.2+20261026175559.json.zip", "browser_download_url": "https://x/full.zip", "size": 3 },
                { "name": "jmdict-all-3.6.2+20261026175559.json.zip", "browser_download_url": "https://x/all.zip", "size": 4 }
              ]
            }
            """;
        using JsonDocument doc = JsonDocument.Parse(release);

        DictionaryRelease? picked = JmdictFiles.PickRelease(doc.RootElement);

        Assert.NotNull(picked);
        Assert.Equal("https://x/full.zip", picked.DownloadUrl);
        Assert.Equal("3.6.2+20261026175559", picked.Version);
        Assert.Equal(3, picked.Size);
    }

    [Fact]
    public void Extracts_The_Json_Under_Its_Own_Name()
    {
        string folder = Directory.CreateTempSubdirectory("kotoneko-test-").FullName;
        try {
            string zip = Path.Combine(folder, "dict.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create)) {
                using (StreamWriter w = new(archive.CreateEntry("../../evil/jmdict-eng-3.6.2.json").Open())) {
                    w.Write("{}");
                }
                archive.CreateEntry("README.txt");
            }

            string extracted = JmdictFiles.ExtractJson(zip, folder);

            Assert.Equal(Path.Combine(folder, "jmdict.json"), extracted);
            Assert.Equal("{}", File.ReadAllText(extracted));
        } finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Rejects_A_Zip_Without_Exactly_One_Json()
    {
        string folder = Directory.CreateTempSubdirectory("kotoneko-test-").FullName;
        try {
            string zip = Path.Combine(folder, "dict.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create)) {
                archive.CreateEntry("README.txt");
            }

            Assert.Throws<InvalidDataException>(() => JmdictFiles.ExtractJson(zip, folder));
        } finally {
            Directory.Delete(folder, recursive: true);
        }
    }
}
