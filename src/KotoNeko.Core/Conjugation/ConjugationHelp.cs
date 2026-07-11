using KotoNeko.Core.Domain;

namespace KotoNeko.Core.Conjugation;

public record VerbClassRule(VerbClass Class, string Label, string Pattern, (string Dict, string Conjugated)[] Examples);

public record FormHelp(string Meaning, string[] EnglishExamples, VerbClassRule[] Rules, string? Note = null);

public static class ConjugationHelp
{
    private static VerbClassRule R(VerbClass c, string label, string pattern, (string, string)[] examples)
        => new(c, label, pattern, examples);

    private static readonly Dictionary<(ConjugationForm, Polarity), FormHelp> _data = new()
    {
        [(ConjugationForm.Past, Polarity.Affirmative)] = new(
            "Expresses that an action was completed or a state existed in the past.",
            ["I ate sushi.", "She wrote the letter.", "He came home."],
            [
                R(VerbClass.Ichidan, "Ichidan (る verbs)", "Drop る, add た",
                    [("食べる", "食べた"), ("起きる", "起きた")]),
                R(VerbClass.Godan, "Godan (う verbs)", "Euphonic change + た/だ: む/ぬ/ぶ→んだ, く→いた (行く is irregular: 行った), す→した, う/つ/る→った",
                    [("飲む", "飲んだ"), ("書く", "書いた"), ("話す", "話した"), ("買う", "買った")]),
                R(VerbClass.Suru, "Suru (する verbs)", "Replace する with した",
                    [("勉強する", "勉強した"), ("する", "した")]),
                R(VerbClass.Kuru, "Kuru (くる)", "Irregular — becomes きた",
                    [("くる", "きた")]),
            ]),

        [(ConjugationForm.Past, Polarity.Negative)] = new(
            "Expresses that an action did NOT occur or a state did NOT exist in the past.",
            ["I did not eat that.", "She didn't write back.", "He never came."],
            [
                R(VerbClass.Ichidan, "Ichidan (る verbs)", "Drop る, add なかった",
                    [("食べる", "食べなかった"), ("起きる", "起きなかった")]),
                R(VerbClass.Godan, "Godan (う verbs)", "Change to あ-row, add なかった (う→わ)",
                    [("飲む", "飲まなかった"), ("書く", "書かなかった"), ("買う", "買わなかった")]),
                R(VerbClass.Suru, "Suru (する verbs)", "Replace する with しなかった",
                    [("勉強する", "勉強しなかった")]),
                R(VerbClass.Kuru, "Kuru (くる)", "Irregular — becomes こなかった",
                    [("くる", "こなかった")]),
            ]),

        [(ConjugationForm.TeForm, Polarity.Affirmative)] = new(
            "The te-form is a connector, not a tense. It links sequential actions (\"did X and then Y\"), makes polite requests (〜てください), and enables the progressive (〜ている) and many other patterns.",
            ["Please eat it (食べてください).", "I washed my hands and then ate.", "I am studying Japanese (勉強している)."],
            [
                R(VerbClass.Ichidan, "Ichidan (る verbs)", "Drop る, add て",
                    [("食べる", "食べて"), ("起きる", "起きて")]),
                R(VerbClass.Godan, "Godan (う verbs)", "Euphonic change + て/で: む/ぬ/ぶ→んで, く→いて (行く is irregular: 行って), す→して, う/つ/る→って",
                    [("飲む", "飲んで"), ("書く", "書いて"), ("話す", "話して"), ("買う", "買って")]),
                R(VerbClass.Suru, "Suru (する verbs)", "Replace する with して",
                    [("勉強する", "勉強して")]),
                R(VerbClass.Kuru, "Kuru (くる)", "Irregular — becomes きて",
                    [("くる", "きて")]),
            ]),

        [(ConjugationForm.TeForm, Polarity.Negative)] = new(
            "The negative te-form. Used in \"without doing\" constructions and negative te-chains. Less common than the affirmative te-form.",
            ["Without studying, I took the test.", "Because I didn't sleep, I'm tired."],
            [
                R(VerbClass.Ichidan, "Ichidan (る verbs)", "Drop る, add なくて",
                    [("食べる", "食べなくて"), ("起きる", "起きなくて")]),
                R(VerbClass.Godan, "Godan (う verbs)", "Change to あ-row, add なくて",
                    [("飲む", "飲まなくて"), ("書く", "書かなくて")]),
                R(VerbClass.Suru, "Suru (する verbs)", "Replace する with しなくて",
                    [("勉強する", "勉強しなくて")]),
                R(VerbClass.Kuru, "Kuru (くる)", "Irregular — becomes こなくて",
                    [("くる", "こなくて")]),
            ]),

        [(ConjugationForm.Potential, Polarity.Affirmative)] = new(
            "Expresses ability or possibility — \"can do\" or \"is possible to do\".",
            ["I can eat spicy food.", "She can read kanji.", "Can you come tomorrow?"],
            [
                R(VerbClass.Ichidan, "Ichidan (る verbs)", "Drop る, add られる",
                    [("食べる", "食べられる"), ("起きる", "起きられる")]),
                R(VerbClass.Godan, "Godan (う verbs)", "Change to え-row, add る",
                    [("飲む", "飲める"), ("書く", "書ける"), ("話す", "話せる")]),
                R(VerbClass.Suru, "Suru (する verbs)", "Replace する with できる",
                    [("勉強する", "勉強できる")]),
                R(VerbClass.Kuru, "Kuru (くる)", "Irregular — becomes こられる",
                    [("くる", "こられる")]),
            ],
            "Ichidan potential (食べられる) is identical in form to Ichidan passive. Context determines the meaning."),

        [(ConjugationForm.Potential, Polarity.Negative)] = new(
            "Expresses inability or impossibility — \"cannot do\".",
            ["I can't eat raw fish.", "She can't read that word.", "He can't come today."],
            [
                R(VerbClass.Ichidan, "Ichidan (る verbs)", "Drop る, add られない",
                    [("食べる", "食べられない"), ("起きる", "起きられない")]),
                R(VerbClass.Godan, "Godan (う verbs)", "Change to え-row, add ない",
                    [("飲む", "飲めない"), ("書く", "書けない"), ("話す", "話せない")]),
                R(VerbClass.Suru, "Suru (する verbs)", "Replace する with できない",
                    [("勉強する", "勉強できない")]),
                R(VerbClass.Kuru, "Kuru (くる)", "Irregular — becomes こられない",
                    [("くる", "こられない")]),
            ]),

        [(ConjugationForm.Passive, Polarity.Affirmative)] = new(
            "The subject receives the action rather than doing it. Also used when something happens to someone — often with an adverse or unexpected nuance from the subject's perspective.",
            ["The letter was written by her.", "I was scolded by my teacher.", "My wallet was stolen."],
            [
                R(VerbClass.Ichidan, "Ichidan (る verbs)", "Drop る, add られる",
                    [("食べる", "食べられる"), ("起きる", "起きられる")]),
                R(VerbClass.Godan, "Godan (う verbs)", "Change to あ-row, add れる",
                    [("飲む", "飲まれる"), ("書く", "書かれる"), ("話す", "話される")]),
                R(VerbClass.Suru, "Suru (する verbs)", "Replace する with される",
                    [("勉強する", "勉強される")]),
                R(VerbClass.Kuru, "Kuru (くる)", "Irregular — becomes こられる",
                    [("くる", "こられる")]),
            ],
            "Ichidan passive (食べられる) is identical in form to Ichidan potential. Context determines the meaning."),

        [(ConjugationForm.Passive, Polarity.Negative)] = new(
            "Something is NOT done to the subject, or an action does not happen to someone.",
            ["The report was not written.", "I was not scolded.", "Nothing was said."],
            [
                R(VerbClass.Ichidan, "Ichidan (る verbs)", "Drop る, add られない",
                    [("食べる", "食べられない"), ("起きる", "起きられない")]),
                R(VerbClass.Godan, "Godan (う verbs)", "Change to あ-row, add れない",
                    [("飲む", "飲まれない"), ("書く", "書かれない")]),
                R(VerbClass.Suru, "Suru (する verbs)", "Replace する with されない",
                    [("勉強する", "勉強されない")]),
                R(VerbClass.Kuru, "Kuru (くる)", "Irregular — becomes こられない",
                    [("くる", "こられない")]),
            ]),

        [(ConjugationForm.Causative, Polarity.Affirmative)] = new(
            "Someone causes or allows another person to do something. The causer is the subject; the person made to act is marked with に (for volitional actions) or を (for movement verbs).",
            ["I made my child eat vegetables.", "The teacher let the students leave early.", "She had him write the report."],
            [
                R(VerbClass.Ichidan, "Ichidan (る verbs)", "Drop る, add させる",
                    [("食べる", "食べさせる"), ("起きる", "起きさせる")]),
                R(VerbClass.Godan, "Godan (う verbs)", "Change to あ-row, add せる",
                    [("飲む", "飲ませる"), ("書く", "書かせる"), ("話す", "話させる")]),
                R(VerbClass.Suru, "Suru (する verbs)", "Replace する with させる",
                    [("勉強する", "勉強させる")]),
                R(VerbClass.Kuru, "Kuru (くる)", "Irregular — becomes こさせる",
                    [("くる", "こさせる")]),
            ]),

        [(ConjugationForm.Causative, Polarity.Negative)] = new(
            "Someone does NOT cause or allow another person to do something — prevention, refusal to permit.",
            ["I won't let my child eat sweets.", "She didn't make him go.", "They prevented us from entering."],
            [
                R(VerbClass.Ichidan, "Ichidan (る verbs)", "Drop る, add させない",
                    [("食べる", "食べさせない"), ("起きる", "起きさせない")]),
                R(VerbClass.Godan, "Godan (う verbs)", "Change to あ-row, add せない",
                    [("飲む", "飲ませない"), ("書く", "書かせない")]),
                R(VerbClass.Suru, "Suru (する verbs)", "Replace する with させない",
                    [("勉強する", "勉強させない")]),
                R(VerbClass.Kuru, "Kuru (くる)", "Irregular — becomes こさせない",
                    [("くる", "こさせない")]),
            ]),

        [(ConjugationForm.CausativePassive, Polarity.Affirmative)] = new(
            "The subject is FORCED to do something — \"made to do\" against one's will. Combines causative + passive: the subject both performs the action and receives the coercion. Often carries a sense of reluctance or imposition.",
            ["I was made to eat vegetables by my parents.", "He was forced to write an apology.", "We were made to wait for two hours."],
            [
                R(VerbClass.Ichidan, "Ichidan (る verbs)", "Drop る, add させられる",
                    [("食べる", "食べさせられる"), ("起きる", "起きさせられる")]),
                R(VerbClass.Godan, "Godan (う verbs)", "Change to あ-row, add せられる",
                    [("飲む", "飲ませられる"), ("書く", "書かせられる")]),
                R(VerbClass.Suru, "Suru (する verbs)", "Replace する with させられる",
                    [("勉強する", "勉強させられる")]),
                R(VerbClass.Kuru, "Kuru (くる)", "Irregular — becomes こさせられる",
                    [("くる", "こさせられる")]),
            ],
            "The contracted form (飲まされる) is common in speech, but this app uses the full form (飲ませられる)."),

        [(ConjugationForm.CausativePassive, Polarity.Negative)] = new(
            "The subject is NOT forced to do something — the coercion does not apply.",
            ["I was not made to eat it.", "He wasn't forced to attend.", "We weren't required to stay."],
            [
                R(VerbClass.Ichidan, "Ichidan (る verbs)", "Drop る, add させられない",
                    [("食べる", "食べさせられない"), ("起きる", "起きさせられない")]),
                R(VerbClass.Godan, "Godan (う verbs)", "Change to あ-row, add せられない",
                    [("飲む", "飲ませられない"), ("書く", "書かせられない")]),
                R(VerbClass.Suru, "Suru (する verbs)", "Replace する with させられない",
                    [("勉強する", "勉強させられない")]),
                R(VerbClass.Kuru, "Kuru (くる)", "Irregular — becomes こさせられない",
                    [("くる", "こさせられない")]),
            ]),

        [(ConjugationForm.Imperative, Polarity.Affirmative)] = new(
            "A blunt, direct command. More forceful than 〜てください. Natural in emergencies, between close friends, in masculine speech, or when speaking to subordinates. Not appropriate in polite conversation.",
            ["Eat! (食べろ)", "Write! (書け)", "Come here! (こい)"],
            [
                R(VerbClass.Ichidan, "Ichidan (る verbs)", "Drop る, add ろ",
                    [("食べる", "食べろ"), ("起きる", "起きろ")]),
                R(VerbClass.Godan, "Godan (う verbs)", "Change to え-row — no suffix needed",
                    [("飲む", "飲め"), ("書く", "書け"), ("話す", "話せ")]),
                R(VerbClass.Suru, "Suru (する verbs)", "Replace する with しろ",
                    [("勉強する", "勉強しろ")]),
                R(VerbClass.Kuru, "Kuru (くる)", "Irregular — becomes こい",
                    [("くる", "こい")]),
            ]),

        [(ConjugationForm.Imperative, Polarity.Negative)] = new(
            "A direct prohibition. Add な to the plain (dictionary) form of any verb. The rule is uniform across ALL verb classes — no stem change whatsoever.",
            ["Don't eat that! (食べるな)", "Don't drink! (飲むな)", "Don't come! (くるな)"],
            [
                R(VerbClass.Ichidan, "Ichidan (る verbs)", "Dictionary form + な",
                    [("食べる", "食べるな"), ("起きる", "起きるな")]),
                R(VerbClass.Godan, "Godan (う verbs)", "Dictionary form + な (no stem change)",
                    [("飲む", "飲むな"), ("書く", "書くな")]),
                R(VerbClass.Suru, "Suru (する verbs)", "Dictionary form + な",
                    [("勉強する", "勉強するな")]),
                R(VerbClass.Kuru, "Kuru (くる)", "Dictionary form + な",
                    [("くる", "くるな")]),
            ],
            "Unlike most forms, the negative imperative does NOT change the verb stem. Always use the plain dictionary form."),
    };

    public static FormHelp? Get(ConjugationForm form, Polarity polarity)
        => _data.TryGetValue((form, polarity), out FormHelp? help) ? help : null;
}
