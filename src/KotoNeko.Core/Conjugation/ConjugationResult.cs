using KotoNeko.Core.Domain;

namespace KotoNeko.Core.Conjugation;

/// <summary>A single generated conjugation (form + polarity + kana value).</summary>
public readonly record struct ConjugationResult(ConjugationForm Form, Polarity Polarity, string Kana);
