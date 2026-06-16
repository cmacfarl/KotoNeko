using KotoNeko.Core.Domain;

namespace KotoNeko.Core.Conjugation;

/// <summary>Human-friendly labels for conjugation forms and polarities.</summary>
public static class ConjugationLabels
{
    public static string Form(ConjugationForm form) => form switch
    {
        ConjugationForm.Past => "Past",
        ConjugationForm.TeForm => "Te-form",
        ConjugationForm.Potential => "Potential",
        ConjugationForm.Passive => "Passive",
        ConjugationForm.Causative => "Causative",
        ConjugationForm.CausativePassive => "Causative-passive",
        ConjugationForm.Imperative => "Imperative",
        _ => form.ToString(),
    };

    public static string Polarity(Polarity polarity) =>
        polarity == Domain.Polarity.Affirmative ? "Affirmative" : "Negative";

    /// <summary>A combined label, e.g. "Te-form · Negative".</summary>
    public static string Describe(ConjugationForm form, Polarity polarity) =>
        $"{Form(form)} · {Polarity(polarity)}";
}
