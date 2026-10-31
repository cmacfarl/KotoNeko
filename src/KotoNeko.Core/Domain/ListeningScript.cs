namespace KotoNeko.Core.Domain;

/// <summary>What kind of listening script: a short story with a narrator, or two people talking.</summary>
public enum ListeningKind
{
    Story = 0,
    Dialogue = 1,
}

/// <summary>How well a comprehension answer showed the script was understood.</summary>
public enum ListeningUnderstanding
{
    No = 0,
    Partly = 1,
    Yes = 2,
}

/// <summary>
/// A short story or two-person dialogue for extensive listening (Phase 6), written by
/// an LLM from the user's known words and grammar, voiced line by line. Not an SRS
/// item: scripts are listened to as often as the user likes.
/// </summary>
public class ListeningScript
{
    public const int MaxTitleLength = 200;
    public const int MaxTopicLength = 200;
    public const int MaxQuestionLength = 500;
    public const int MaxWriterLength = 200;
    public const int MaxFlagNoteLength = 500;

    public int Id { get; set; }

    public ListeningKind Kind { get; set; }

    /// <summary>A short title in Japanese.</summary>
    public string Title { get; set; } = string.Empty;

    public string TitleEnglish { get; set; } = string.Empty;

    /// <summary>The topic the user asked for; empty when they left it to the writer.</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>The level it was written for.</summary>
    public JlptLevel Level { get; set; }

    /// <summary>The comprehension question, in English.</summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>What a good answer covers, one point per line (for the grader).</summary>
    public string AnswerPoints { get; set; } = string.Empty;

    /// <summary>Words the writer says are new to the learner, one "word (reading): meaning" per line.</summary>
    public string NewWords { get; set; } = string.Empty;

    /// <summary>Who wrote it: provider · model · prompt version.</summary>
    public string Writer { get; set; } = string.Empty;

    /// <summary>Flagged as unnatural or wrong, for a native speaker to look at.</summary>
    public bool Flagged { get; set; }

    public string FlagNote { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public List<ListeningLine> Lines { get; set; } = new();
}

/// <summary>One line of a listening script, voiced on its own so it can be replayed.</summary>
public class ListeningLine
{
    public const int MaxTextLength = 300;
    public const int MaxEnglishLength = 500;

    public int Id { get; set; }

    public int ListeningScriptId { get; set; }
    public ListeningScript? ListeningScript { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Who says it: "A" or "B" in a dialogue, "N" (narrator) in a story.</summary>
    public string Speaker { get; set; } = "N";

    public string Text { get; set; } = string.Empty;

    public string English { get; set; } = string.Empty;

    public bool HasAudio { get; set; }

    public ListeningLineAudio? Audio { get; set; }
}

public class ListeningLineAudio
{
    public int Id { get; set; }

    public int ListeningLineId { get; set; }
    public ListeningLine? ListeningLine { get; set; }

    public byte[] Audio { get; set; } = Array.Empty<byte>();
}

/// <summary>Seconds of a script's audio played, for the daily listening minutes.</summary>
public class ListeningLog
{
    public long Id { get; set; }

    public int ListeningScriptId { get; set; }
    public ListeningScript? ListeningScript { get; set; }

    public DateTime ListenedAt { get; set; }

    public int Seconds { get; set; }
}

/// <summary>An answer to a script's comprehension question, and its grade.</summary>
public class ListeningAnswer
{
    public const int MaxAnswerLength = 1000;

    public long Id { get; set; }

    public int ListeningScriptId { get; set; }
    public ListeningScript? ListeningScript { get; set; }

    public DateTime AnsweredAt { get; set; }

    public string Answer { get; set; } = string.Empty;

    public ListeningUnderstanding Understood { get; set; }

    public string VerdictJson { get; set; } = string.Empty;

    public string PromptVersion { get; set; } = string.Empty;
}
