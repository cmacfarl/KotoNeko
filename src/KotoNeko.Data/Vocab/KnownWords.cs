using System.Collections.Concurrent;
using System.Linq.Expressions;
using KotoNeko.Core.Conjugation;
using KotoNeko.Core.Domain;
using KotoNeko.Data.Dictionary;

namespace KotoNeko.Data.Vocab;

/// <summary>
/// The known-words pool: the words practice sentences may use besides their target,
/// and that need no furigana. A word is in the pool when it isn't marked "do not
/// use" and is either asleep and never studied (a word you know but don't review)
/// or studied to at least the threshold stage (Guru or Master, from Settings).
/// </summary>
public static class KnownWords
{
    private static readonly ConcurrentDictionary<SrsStage, Func<Vocabulary, bool>> Compiled = new();

    /// <summary>The pool rule, for database queries.</summary>
    public static Expression<Func<Vocabulary, bool>> Rule(SrsStage threshold) => v =>
        !v.DoNotUse
        && ((v.IsAsleep && (v.Srs == null || v.Srs.Stage == SrsStage.Locked))
            || (v.Srs != null && v.Srs.Stage >= threshold));

    /// <summary>Only the words in the pool.</summary>
    public static IQueryable<Vocabulary> InPool(this IQueryable<Vocabulary> query, SrsStage threshold) =>
        query.Where(Rule(threshold));

    /// <summary>
    /// Words the user knows or is studying: everything except words that are asleep,
    /// never studied and marked "do not use" (a seeded N3 word not yet picked out as
    /// known, say). Kanji conversion prefers these, and listening accepts their
    /// meanings for a word that sounds the same.
    /// </summary>
    public static IQueryable<Vocabulary> NotHeldBack(this IQueryable<Vocabulary> query) =>
        query.Where(v => !(v.DoNotUse && v.IsAsleep && (v.Srs == null || v.Srs.Stage == SrsStage.Locked)));

    /// <summary>Whether a loaded word is in the pool (its <see cref="StudyItem.Srs"/> must be loaded).</summary>
    public static bool IsInPool(Vocabulary word, SrsStage threshold) =>
        Compiled.GetOrAdd(threshold, t => Rule(t).Compile())(word);

    /// <summary>
    /// A new asleep word made from a dictionary match: its reading, main meanings and
    /// verb class (with conjugations). It counts as known unless
    /// <paramref name="doNotUse"/>, and gets no audio until it's studied.
    /// </summary>
    public static Vocabulary FromDictionary(DictionaryWordMatch match, DateTime now, bool doNotUse = false)
    {
        Vocabulary word = new() {
            Japanese = match.Written,
            Reading = match.Reading,
            VerbClass = match.VerbClass,
            IsAsleep = true,
            DoNotUse = doNotUse,
            CreatedAt = now,
        };
        word.Meanings.AddRange(match.MainMeanings.Select((m, i) => new VocabularyMeaning { Text = Truncate(m, 300), SortOrder = i }));
        if (word.VerbClass != VerbClass.None) {
            word.Conjugations.AddRange(ConjugationEngine.Generate(word.Reading, word.VerbClass)
                .Select(c => new Conjugation { Form = c.Form, Polarity = c.Polarity, ExpectedKana = c.Kana }));
        }
        return word;
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];
}
