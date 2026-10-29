namespace KotoNeko.Web.Services;

/// <summary>
/// Text from a <c>JapaneseInput</c>: what was written, and the kana typed to produce
/// it (null when it can't be known, e.g. after editing a converted word).
/// </summary>
public sealed record JapaneseText(string Text, string? Kana);
