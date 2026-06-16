using KotoNeko.Core.Text;
using Xunit;

namespace KotoNeko.Tests;

public class RomajiConverterTests
{
    [Theory]
    [InlineData("konnichiwa", "こんにちわ")]
    [InlineData("shinbun", "しんぶん")]
    [InlineData("sensei", "せんせい")]
    [InlineData("gakkou", "がっこう")]
    [InlineData("kitte", "きって")]
    [InlineData("nihon", "にほん")]
    [InlineData("tabemasu", "たべます")]
    [InlineData("kyou", "きょう")]
    [InlineData("chotto", "ちょっと")]
    [InlineData("jisho", "じしょ")]
    [InlineData("fudon", "ふどん")]
    [InlineData("wo", "を")]
    public void Converts_Complete_Words(string romaji, string expected)
    {
        Assert.Equal(expected, RomajiConverter.ToHiragana(romaji));
    }

    [Fact]
    public void Trailing_N_Becomes_Final_Nn()
    {
        Assert.Equal("ほん", RomajiConverter.ToHiragana("hon"));
    }

    [Fact]
    public void Double_N_Becomes_Nn()
    {
        Assert.Equal("おんな", RomajiConverter.ToHiragana("onna"));
    }

    [Fact]
    public void Partial_Input_Left_As_Romaji_For_Live_Typing()
    {
        // A lone "sh" could still become し/しゃ etc., so it is preserved.
        Assert.Equal("し", RomajiConverter.ToHiragana("shi"));
        Assert.Equal("sh", RomajiConverter.ToHiragana("sh"));
    }

    [Fact]
    public void Existing_Kana_Passes_Through()
    {
        Assert.Equal("ねこ", RomajiConverter.ToHiragana("ねこ"));
    }
}
