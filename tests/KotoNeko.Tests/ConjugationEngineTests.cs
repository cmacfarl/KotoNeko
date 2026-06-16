using KotoNeko.Core.Conjugation;
using KotoNeko.Core.Domain;
using Xunit;

namespace KotoNeko.Tests;

public class ConjugationEngineTests
{
    private static string Get(string reading, VerbClass cls, ConjugationForm form, Polarity polarity)
    {
        IReadOnlyList<ConjugationResult> all = ConjugationEngine.Generate(reading, cls);
        ConjugationResult match = Assert.Single(all, r => r.Form == form && r.Polarity == polarity);
        return match.Kana;
    }

    [Fact]
    public void Generates_All_Fourteen_Forms()
    {
        IReadOnlyList<ConjugationResult> all = ConjugationEngine.Generate("たべる", VerbClass.Ichidan);
        Assert.Equal(14, all.Count);
    }

    [Fact]
    public void NonVerb_Returns_Empty()
    {
        Assert.Empty(ConjugationEngine.Generate("ねこ", VerbClass.None));
    }

    // ----- Godan: 話す (はなす) -----
    [Theory]
    [InlineData(ConjugationForm.Past, Polarity.Affirmative, "はなした")]
    [InlineData(ConjugationForm.Past, Polarity.Negative, "はなさなかった")]
    [InlineData(ConjugationForm.TeForm, Polarity.Affirmative, "はなして")]
    [InlineData(ConjugationForm.TeForm, Polarity.Negative, "はなさなくて")]
    [InlineData(ConjugationForm.Potential, Polarity.Affirmative, "はなせる")]
    [InlineData(ConjugationForm.Potential, Polarity.Negative, "はなせない")]
    [InlineData(ConjugationForm.Passive, Polarity.Affirmative, "はなされる")]
    [InlineData(ConjugationForm.Causative, Polarity.Affirmative, "はなさせる")]
    [InlineData(ConjugationForm.CausativePassive, Polarity.Affirmative, "はなさせられる")]
    [InlineData(ConjugationForm.Imperative, Polarity.Affirmative, "はなせ")]
    [InlineData(ConjugationForm.Imperative, Polarity.Negative, "はなすな")]
    public void Godan_Hanasu(ConjugationForm form, Polarity polarity, string expected)
    {
        Assert.Equal(expected, Get("はなす", VerbClass.Godan, form, polarity));
    }

    // ----- Godan: 書く (かく) — く euphonic て -----
    [Theory]
    [InlineData(ConjugationForm.Past, Polarity.Affirmative, "かいた")]
    [InlineData(ConjugationForm.TeForm, Polarity.Affirmative, "かいて")]
    [InlineData(ConjugationForm.Potential, Polarity.Affirmative, "かける")]
    [InlineData(ConjugationForm.Imperative, Polarity.Affirmative, "かけ")]
    public void Godan_Kaku(ConjugationForm form, Polarity polarity, string expected)
    {
        Assert.Equal(expected, Get("かく", VerbClass.Godan, form, polarity));
    }

    // ----- Godan: 飲む (のむ) — む euphonic んで -----
    [Theory]
    [InlineData(ConjugationForm.TeForm, Polarity.Affirmative, "のんで")]
    [InlineData(ConjugationForm.Past, Polarity.Affirmative, "のんだ")]
    [InlineData(ConjugationForm.Passive, Polarity.Affirmative, "のまれる")]
    [InlineData(ConjugationForm.Causative, Polarity.Affirmative, "のませる")]
    public void Godan_Nomu(ConjugationForm form, Polarity polarity, string expected)
    {
        Assert.Equal(expected, Get("のむ", VerbClass.Godan, form, polarity));
    }

    // ----- Godan: 買う (かう) — う -> わ in a-stem -----
    [Theory]
    [InlineData(ConjugationForm.Past, Polarity.Affirmative, "かった")]
    [InlineData(ConjugationForm.TeForm, Polarity.Affirmative, "かって")]
    [InlineData(ConjugationForm.Past, Polarity.Negative, "かわなかった")]
    [InlineData(ConjugationForm.Causative, Polarity.Affirmative, "かわせる")]
    public void Godan_Kau(ConjugationForm form, Polarity polarity, string expected)
    {
        Assert.Equal(expected, Get("かう", VerbClass.Godan, form, polarity));
    }

    // ----- Irregular godan: 行く (いく) -> って/った -----
    [Theory]
    [InlineData(ConjugationForm.TeForm, Polarity.Affirmative, "いって")]
    [InlineData(ConjugationForm.Past, Polarity.Affirmative, "いった")]
    public void Godan_Iku_Irregular(ConjugationForm form, Polarity polarity, string expected)
    {
        Assert.Equal(expected, Get("いく", VerbClass.Godan, form, polarity));
    }

    // ----- Irregular: ある -> negative is ない -----
    [Theory]
    [InlineData(ConjugationForm.Past, Polarity.Negative, "なかった")]
    [InlineData(ConjugationForm.TeForm, Polarity.Negative, "なくて")]
    public void Godan_Aru_Irregular(ConjugationForm form, Polarity polarity, string expected)
    {
        Assert.Equal(expected, Get("ある", VerbClass.Godan, form, polarity));
    }

    // ----- Ichidan: 食べる (たべる) -----
    [Theory]
    [InlineData(ConjugationForm.Past, Polarity.Affirmative, "たべた")]
    [InlineData(ConjugationForm.Past, Polarity.Negative, "たべなかった")]
    [InlineData(ConjugationForm.TeForm, Polarity.Affirmative, "たべて")]
    [InlineData(ConjugationForm.Potential, Polarity.Affirmative, "たべられる")]
    [InlineData(ConjugationForm.Passive, Polarity.Affirmative, "たべられる")]
    [InlineData(ConjugationForm.Causative, Polarity.Affirmative, "たべさせる")]
    [InlineData(ConjugationForm.CausativePassive, Polarity.Affirmative, "たべさせられる")]
    [InlineData(ConjugationForm.Imperative, Polarity.Affirmative, "たべろ")]
    [InlineData(ConjugationForm.Imperative, Polarity.Negative, "たべるな")]
    public void Ichidan_Taberu(ConjugationForm form, Polarity polarity, string expected)
    {
        Assert.Equal(expected, Get("たべる", VerbClass.Ichidan, form, polarity));
    }

    // ----- Suru: 勉強する (べんきょうする) -----
    [Theory]
    [InlineData(ConjugationForm.Past, Polarity.Affirmative, "べんきょうした")]
    [InlineData(ConjugationForm.TeForm, Polarity.Affirmative, "べんきょうして")]
    [InlineData(ConjugationForm.Potential, Polarity.Affirmative, "べんきょうできる")]
    [InlineData(ConjugationForm.Passive, Polarity.Affirmative, "べんきょうされる")]
    [InlineData(ConjugationForm.Causative, Polarity.Affirmative, "べんきょうさせる")]
    [InlineData(ConjugationForm.Imperative, Polarity.Affirmative, "べんきょうしろ")]
    [InlineData(ConjugationForm.Imperative, Polarity.Negative, "べんきょうするな")]
    public void Suru_Benkyou(ConjugationForm form, Polarity polarity, string expected)
    {
        Assert.Equal(expected, Get("べんきょうする", VerbClass.Suru, form, polarity));
    }

    // ----- Kuru: 来る (くる) -----
    [Theory]
    [InlineData(ConjugationForm.Past, Polarity.Affirmative, "きた")]
    [InlineData(ConjugationForm.Past, Polarity.Negative, "こなかった")]
    [InlineData(ConjugationForm.TeForm, Polarity.Affirmative, "きて")]
    [InlineData(ConjugationForm.Potential, Polarity.Affirmative, "こられる")]
    [InlineData(ConjugationForm.Causative, Polarity.Affirmative, "こさせる")]
    [InlineData(ConjugationForm.Imperative, Polarity.Affirmative, "こい")]
    [InlineData(ConjugationForm.Imperative, Polarity.Negative, "くるな")]
    public void Kuru(ConjugationForm form, Polarity polarity, string expected)
    {
        Assert.Equal(expected, Get("くる", VerbClass.Kuru, form, polarity));
    }
}
