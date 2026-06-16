namespace KotoNeko.Core.Domain;

/// <summary>
/// The grammatical class of a verb, which determines how it conjugates.
/// </summary>
public enum VerbClass
{
    /// <summary>Not a verb (default for plain vocabulary).</summary>
    None = 0,

    /// <summary>Godan (う-verb / 五段) verb, e.g. 話す, 書く, 飲む.</summary>
    Godan = 1,

    /// <summary>Ichidan (る-verb / 一段) verb, e.g. 食べる, 見る.</summary>
    Ichidan = 2,

    /// <summary>The irregular verb する and する-compounds, e.g. 勉強する.</summary>
    Suru = 3,

    /// <summary>The irregular verb 来る (くる).</summary>
    Kuru = 4,
}

/// <summary>
/// The conjugation forms we quiz on. Each combines with a <see cref="Polarity"/>.
/// </summary>
public enum ConjugationForm
{
    Past = 0,
    TeForm = 1,
    Potential = 2,
    Passive = 3,
    Causative = 4,
    CausativePassive = 5,
    Imperative = 6,
}

/// <summary>Affirmative or negative polarity for a conjugation.</summary>
public enum Polarity
{
    Affirmative = 0,
    Negative = 1,
}

/// <summary>
/// WaniKani-style SRS stages. The numeric value is the stage index used for
/// interval lookup and advance/drop arithmetic.
/// </summary>
public enum SrsStage
{
    /// <summary>Not yet learned; awaiting a lesson. No reviews scheduled.</summary>
    Locked = 0,
    Apprentice1 = 1,
    Apprentice2 = 2,
    Apprentice3 = 3,
    Apprentice4 = 4,
    Guru1 = 5,
    Guru2 = 6,
    Master = 7,
    Enlightened = 8,
    Burned = 9,
}

/// <summary>The kind of question asked during a review.</summary>
public enum QuestionType
{
    Reading = 0,
    Meaning = 1,
    Conjugation = 2,
}

/// <summary>A loose categorisation for source materials, used only for display.</summary>
public enum SourceKind
{
    Other = 0,
    Textbook = 1,
    Manga = 2,
    Anime = 3,
    Game = 4,
    Book = 5,
    Web = 6,
}
