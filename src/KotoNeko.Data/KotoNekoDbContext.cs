using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Data;

public class KotoNekoDbContext : DbContext
{
    public KotoNekoDbContext(DbContextOptions<KotoNekoDbContext> options)
        : base(options)
    {
    }

    public DbSet<SourceMaterial> SourceMaterials => Set<SourceMaterial>();
    public DbSet<StudyItem> StudyItems => Set<StudyItem>();
    public DbSet<Vocabulary> Vocabulary => Set<Vocabulary>();
    public DbSet<VocabularyMeaning> VocabularyMeanings => Set<VocabularyMeaning>();
    public DbSet<Conjugation> Conjugations => Set<Conjugation>();
    public DbSet<SrsItem> SrsItems => Set<SrsItem>();
    public DbSet<ReviewLog> ReviewLogs => Set<ReviewLog>();
    public DbSet<VocabularyAudio> VocabularyAudios => Set<VocabularyAudio>();
    public DbSet<ConjugatedAudio> ConjugatedAudios => Set<ConjugatedAudio>();
    public DbSet<StudySettings> StudySettings => Set<StudySettings>();
    public DbSet<DictionaryEntry> DictionaryEntries => Set<DictionaryEntry>();
    public DbSet<DictionaryImport> DictionaryImports => Set<DictionaryImport>();
    public DbSet<DictionaryMeaning> DictionaryMeanings => Set<DictionaryMeaning>();
    public DbSet<ImeChoice> ImeChoices => Set<ImeChoice>();
    public DbSet<SentenceReading> SentenceReadings => Set<SentenceReading>();
    public DbSet<DictationAttempt> DictationAttempts => Set<DictationAttempt>();
    public DbSet<PracticeSentence> PracticeSentences => Set<PracticeSentence>();
    public DbSet<PracticeSentenceAudio> PracticeSentenceAudios => Set<PracticeSentenceAudio>();
    public DbSet<GrammarListEntry> GrammarList => Set<GrammarListEntry>();
    public DbSet<GrammarPoint> GrammarPoints => Set<GrammarPoint>();
    public DbSet<GrammarExample> GrammarExamples => Set<GrammarExample>();
    public DbSet<GrammarReviewAttempt> GrammarReviewAttempts => Set<GrammarReviewAttempt>();
    public DbSet<ListeningScript> ListeningScripts => Set<ListeningScript>();
    public DbSet<ListeningLine> ListeningLines => Set<ListeningLine>();
    public DbSet<ListeningLineAudio> ListeningLineAudios => Set<ListeningLineAudio>();
    public DbSet<ListeningLog> ListeningLogs => Set<ListeningLog>();
    public DbSet<ListeningAnswer> ListeningAnswers => Set<ListeningAnswer>();
    public DbSet<GrammarExampleAudio> GrammarExampleAudios => Set<GrammarExampleAudio>();
    public DbSet<LlmCall> LlmCalls => Set<LlmCall>();
    public DbSet<GradingTestItem> GradingTestItems => Set<GradingTestItem>();
    public DbSet<GradingEvalRun> GradingEvalRuns => Set<GradingEvalRun>();
    public DbSet<GradingEvalResult> GradingEvalResults => Set<GradingEvalResult>();
    public DbSet<GradingPrelabel> GradingPrelabels => Set<GradingPrelabel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SourceMaterial>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Table-per-type: StudyItems holds what every kind shares, and each derived
        // table (Vocabulary, ...) holds its own columns keyed by the same id.
        modelBuilder.Entity<StudyItem>(entity =>
        {
            entity.UseTptMappingStrategy();
            entity.ToTable("StudyItems");
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Srs)
                .WithOne(s => s.StudyItem)
                .HasForeignKey<SrsItem>(s => s.StudyItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.ReviewLogs)
                .WithOne(r => r.StudyItem)
                .HasForeignKey(r => r.StudyItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.Kind);
            entity.HasIndex(e => e.IsAsleep);
        });

        modelBuilder.Entity<Vocabulary>(entity =>
        {
            entity.ToTable("Vocabulary");
            entity.Property(e => e.Japanese).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Reading).HasMaxLength(100).IsRequired();
            entity.Property(e => e.AlternateReadings).HasMaxLength(500).HasDefaultValue(string.Empty);
            entity.Property(e => e.ContextSentence).HasMaxLength(1000);
            entity.Property(e => e.Memo).HasMaxLength(1000);
            entity.HasIndex(e => e.JlptLevel);

            // Computed properties; do not map them.
            entity.Ignore(e => e.IsVerb);
            entity.Ignore(e => e.PrimaryMeaning);
            entity.Ignore(e => e.MeaningsDisplay);
            entity.Ignore(e => e.HasKanji);
            entity.Ignore(e => e.ShowsFurigana);
            entity.Ignore(e => e.AsksReading);

            entity.HasMany(e => e.Meanings)
                .WithOne(m => m.Vocabulary)
                .HasForeignKey(m => m.VocabularyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceMaterial)
                .WithMany(s => s.Vocabulary)
                .HasForeignKey(e => e.SourceMaterialId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Conjugations)
                .WithOne(c => c.Vocabulary)
                .HasForeignKey(c => c.VocabularyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VocabularyMeaning>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Text).HasMaxLength(300).IsRequired();
            entity.HasIndex(e => new { e.VocabularyId, e.SortOrder });
        });

        modelBuilder.Entity<Conjugation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExpectedKana).HasMaxLength(100).IsRequired();
            entity.HasIndex(e => new { e.VocabularyId, e.Form, e.Polarity }).IsUnique();
        });

        modelBuilder.Entity<SrsItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.IsUnlocked);
            entity.HasIndex(e => e.StudyItemId).IsUnique();
            entity.HasIndex(e => e.NextReviewAt);
            entity.HasIndex(e => e.Stage);
        });

        modelBuilder.Entity<ReviewLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ReviewedAt);
            entity.HasIndex(e => e.WasCorrect);
        });

        modelBuilder.Entity<VocabularyAudio>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WordAudio).HasColumnType("longblob").IsRequired();
            entity.Property(e => e.SentenceAudio).HasColumnType("longblob");
            entity.HasOne(e => e.Vocabulary)
                .WithOne(v => v.Audio)
                .HasForeignKey<VocabularyAudio>(a => a.VocabularyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.VocabularyId).IsUnique();
        });

        modelBuilder.Entity<ConjugatedAudio>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Audio).HasColumnType("longblob").IsRequired();
            entity.HasOne(e => e.Conjugation)
                .WithOne(c => c.ConjugatedAudio)
                .HasForeignKey<ConjugatedAudio>(a => a.ConjugationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ConjugationId).IsUnique();
        });

        // A single row holding the user's preferences, seeded with the defaults.
        modelBuilder.Entity<StudySettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ListeningScriptModel).HasMaxLength(KotoNeko.Core.Domain.StudySettings.MaxModelChoiceLength).IsRequired();
            entity.HasData(new StudySettings());
        });

        // Readings and spellings compare exactly: the default Unicode collations
        // treat は/ば/ぱ and hiragana/katakana as equal, which would merge homophones.
        modelBuilder.Entity<DictionaryEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Reading).HasMaxLength(DictionaryEntry.MaxLength).UseCollation("utf8mb4_bin").IsRequired();
            entity.Property(e => e.Written).HasMaxLength(DictionaryEntry.MaxLength).UseCollation("utf8mb4_bin").IsRequired();
            entity.HasIndex(e => e.Reading);
            // Sentence readings look words up by spelling.
            entity.HasIndex(e => e.Written);
        });

        modelBuilder.Entity<SentenceReading>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TextHash).HasMaxLength(64).IsFixedLength().IsRequired();
            entity.Property(e => e.Text).HasMaxLength(SentenceReading.MaxTextLength).IsRequired();
            entity.Property(e => e.Furigana).HasMaxLength(4000).IsRequired();
            entity.Property(e => e.Words).HasColumnType("text").IsRequired();
            entity.HasIndex(e => e.TextHash).IsUnique();
        });

        modelBuilder.Entity<DictationAttempt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Sentence).HasMaxLength(SentenceReading.MaxTextLength).IsRequired();
            entity.Property(e => e.Typed).HasMaxLength(SentenceReading.MaxTextLength).IsRequired();
            entity.HasOne(e => e.StudyItem)
                .WithMany()
                .HasForeignKey(e => e.StudyItemId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.PracticedAt);
            entity.HasOne(e => e.GrammarExample)
                .WithMany()
                .HasForeignKey(e => e.GrammarExampleId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.PracticeSentence)
                .WithMany()
                .HasForeignKey(e => e.PracticeSentenceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PracticeSentence>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Text).HasMaxLength(PracticeSentence.MaxTextLength).IsRequired();
            entity.Property(e => e.English).HasMaxLength(PracticeSentence.MaxEnglishLength).IsRequired();
            entity.Property(e => e.ReviewNote).HasMaxLength(PracticeSentence.MaxNoteLength);
            entity.Ignore(e => e.InUse);
            entity.HasOne(e => e.Vocabulary)
                .WithMany()
                .HasForeignKey(e => e.VocabularyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Audio)
                .WithOne(a => a.PracticeSentence)
                .HasForeignKey<PracticeSentenceAudio>(a => a.PracticeSentenceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.VocabularyId, e.Status });
        });

        // The grammar list (not study items: a grammar point links to its entry by id).
        modelBuilder.Entity<GrammarListEntry>(entity =>
        {
            entity.ToTable("GrammarList");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SeedKey).HasMaxLength(GrammarListEntry.MaxKeyLength).UseCollation("utf8mb4_bin");
            entity.Property(e => e.Pattern).HasMaxLength(GrammarListEntry.MaxPatternLength).IsRequired();
            entity.Property(e => e.Meaning).HasMaxLength(GrammarListEntry.MaxMeaningLength).IsRequired();
            entity.HasIndex(e => e.SeedKey).IsUnique();
            entity.HasIndex(e => new { e.Level, e.SortOrder });
        });

        // Grammar points as study items (Phase 4). Pattern, meaning and level stay on the
        // list entry; contrasts, mistakes and situations are small lists stored as JSON.
        modelBuilder.Entity<GrammarPoint>(entity =>
        {
            entity.ToTable("GrammarPoints");
            entity.Property(e => e.Explanation).HasMaxLength(GrammarPoint.MaxExplanationLength).IsRequired();
            entity.Property(e => e.Formation).HasMaxLength(GrammarPoint.MaxFormationLength).IsRequired();
            entity.Property(e => e.DraftedBy).HasMaxLength(GrammarPoint.MaxDraftedByLength);
            entity.Property(e => e.Contrasts).HasColumnType("text").IsRequired().AsJsonList();
            entity.Property(e => e.Mistakes).HasColumnType("text").IsRequired().AsJsonList();
            entity.Property(e => e.Situations).HasColumnType("text").IsRequired().AsJsonList();
            entity.HasOne(e => e.Entry)
                .WithMany()
                .HasForeignKey(e => e.GrammarListEntryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.GrammarListEntryId).IsUnique();
            entity.HasMany(e => e.Examples)
                .WithOne(x => x.GrammarPoint)
                .HasForeignKey(x => x.GrammarPointId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GrammarReviewAttempt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Sentence).HasMaxLength(GrammarReviewAttempt.MaxSentenceLength).IsRequired();
            entity.Property(e => e.Intent).HasMaxLength(GrammarReviewAttempt.MaxIntentLength).IsRequired();
            entity.Property(e => e.Confidence).HasMaxLength(10).IsRequired();
            entity.Property(e => e.VerdictJson).HasColumnType("text").IsRequired();
            entity.Property(e => e.PromptVersion).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ContrastPattern).HasMaxLength(GrammarReviewAttempt.MaxContrastLength);
            entity.Property(e => e.SecondVerdictJson).HasColumnType("text");
            entity.Property(e => e.SecondModel).HasMaxLength(GrammarReviewAttempt.MaxModelLength);
            entity.Property(e => e.DisputeComment).HasMaxLength(GrammarReviewAttempt.MaxIntentLength).IsRequired();
            entity.Property(e => e.DisputeVerdictJson).HasColumnType("text");
            entity.Property(e => e.DisputeModel).HasMaxLength(GrammarReviewAttempt.MaxModelLength);
            entity.HasOne(e => e.GrammarPoint)
                .WithMany()
                .HasForeignKey(e => e.GrammarPointId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.GrammarPointId, e.AttemptedAt });
        });

        modelBuilder.Entity<GrammarExample>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Text).HasMaxLength(GrammarExample.MaxTextLength).IsRequired();
            entity.Property(e => e.English).HasMaxLength(GrammarExample.MaxEnglishLength).IsRequired();
            entity.HasOne(e => e.Audio)
                .WithOne(a => a.GrammarExample)
                .HasForeignKey<GrammarExampleAudio>(a => a.GrammarExampleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.GrammarPointId, e.SortOrder });
        });

        // The grading test set (Phase 3): labelled sentences and the eval runs scored against them.
        modelBuilder.Entity<GradingTestItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SeedKey).HasMaxLength(64).UseCollation("utf8mb4_bin");
            entity.Property(e => e.GrammarKey).HasMaxLength(GrammarListEntry.MaxKeyLength).IsRequired();
            entity.Property(e => e.Pattern).HasMaxLength(GrammarListEntry.MaxPatternLength).IsRequired();
            entity.Property(e => e.Meaning).HasMaxLength(GrammarListEntry.MaxMeaningLength).IsRequired();
            entity.Property(e => e.Task).HasMaxLength(GradingTestItem.MaxTaskLength).IsRequired();
            entity.Property(e => e.Sentence).HasMaxLength(GradingTestItem.MaxSentenceLength).IsRequired();
            entity.Property(e => e.Note).HasMaxLength(GradingTestItem.MaxNoteLength);
            entity.HasIndex(e => e.SeedKey).IsUnique();
            entity.HasIndex(e => e.GrammarKey);
        });

        modelBuilder.Entity<GradingPrelabel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Model).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Effort).HasMaxLength(20);
            entity.Property(e => e.PromptVersion).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Explanation).HasColumnType("text").IsRequired();
            entity.HasOne<GradingTestItem>()
                .WithMany()
                .HasForeignKey(e => e.ItemId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ItemId, e.Provider, e.Model, e.PromptVersion }).IsUnique();
        });

        modelBuilder.Entity<GradingEvalRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Effort).HasMaxLength(20);
            entity.Property(e => e.Provider).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Model).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PromptVersion).HasMaxLength(100).IsRequired();
            entity.HasMany(e => e.Results)
                .WithOne(r => r.Run)
                .HasForeignKey(r => r.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GradingEvalResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Confidence).HasMaxLength(20);
            entity.Property(e => e.VerdictJson).HasColumnType("text");
            entity.Property(e => e.Error).HasMaxLength(500);
            entity.HasIndex(e => e.ItemId);
        });

        modelBuilder.Entity<LlmCall>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Model).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PromptVersion).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Error).HasMaxLength(500);
            entity.Property(e => e.Cost).HasPrecision(14, 8);
            entity.HasIndex(e => e.CalledAt);
        });

        modelBuilder.Entity<PracticeSentenceAudio>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Audio).HasColumnType("longblob").IsRequired();
            entity.HasIndex(e => e.PracticeSentenceId).IsUnique();
        });

        modelBuilder.Entity<ListeningScript>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(ListeningScript.MaxTitleLength).IsRequired();
            entity.Property(e => e.TitleEnglish).HasMaxLength(ListeningScript.MaxTitleLength).IsRequired();
            entity.Property(e => e.Topic).HasMaxLength(ListeningScript.MaxTopicLength).IsRequired();
            entity.Property(e => e.Question).HasMaxLength(ListeningScript.MaxQuestionLength).IsRequired();
            entity.Property(e => e.AnswerPoints).HasColumnType("text").IsRequired();
            entity.Property(e => e.NewWords).HasColumnType("text").IsRequired();
            entity.Property(e => e.Writer).HasMaxLength(ListeningScript.MaxWriterLength).IsRequired();
            entity.Property(e => e.FlagNote).HasMaxLength(ListeningScript.MaxFlagNoteLength).IsRequired();
            entity.HasMany(e => e.Lines)
                .WithOne(l => l.ListeningScript)
                .HasForeignKey(l => l.ListeningScriptId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<ListeningLine>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Speaker).HasMaxLength(1).IsRequired();
            entity.Property(e => e.Text).HasMaxLength(ListeningLine.MaxTextLength).IsRequired();
            entity.Property(e => e.English).HasMaxLength(ListeningLine.MaxEnglishLength).IsRequired();
            entity.HasOne(e => e.Audio)
                .WithOne(a => a.ListeningLine)
                .HasForeignKey<ListeningLineAudio>(a => a.ListeningLineId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ListeningScriptId, e.SortOrder });
        });

        modelBuilder.Entity<ListeningLineAudio>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Audio).HasColumnType("longblob").IsRequired();
            entity.HasIndex(e => e.ListeningLineId).IsUnique();
        });

        modelBuilder.Entity<ListeningLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.ListeningScript).WithMany().HasForeignKey(e => e.ListeningScriptId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ListenedAt);
        });

        modelBuilder.Entity<ListeningAnswer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Answer).HasMaxLength(ListeningAnswer.MaxAnswerLength).IsRequired();
            entity.Property(e => e.VerdictJson).HasColumnType("text").IsRequired();
            entity.Property(e => e.PromptVersion).HasMaxLength(100).IsRequired();
            entity.HasOne(e => e.ListeningScript).WithMany().HasForeignKey(e => e.ListeningScriptId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ListeningScriptId, e.AnsweredAt });
        });

        modelBuilder.Entity<GrammarExampleAudio>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Audio).HasColumnType("longblob").IsRequired();
            entity.HasIndex(e => e.GrammarExampleId).IsUnique();
        });

        modelBuilder.Entity<DictionaryMeaning>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Senses).HasColumnType("mediumtext").IsRequired();
        });

        modelBuilder.Entity<ImeChoice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Reading).HasMaxLength(DictionaryEntry.MaxLength).UseCollation("utf8mb4_bin").IsRequired();
            entity.Property(e => e.Written).HasMaxLength(DictionaryEntry.MaxLength).UseCollation("utf8mb4_bin").IsRequired();
            entity.HasIndex(e => new { e.Reading, e.Written }).IsUnique();
        });

        modelBuilder.Entity<DictionaryImport>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Source).HasMaxLength(200);
            entity.Property(e => e.Version).HasMaxLength(100);
            entity.Property(e => e.DictionaryDate).HasMaxLength(50);
            // Imports made before this column existed had no meanings: format 1.
            entity.Property(e => e.FormatVersion).HasDefaultValue(1);
        });
    }
}
