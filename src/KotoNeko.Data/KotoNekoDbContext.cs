using KotoNeko.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Data;

public class KotoNekoDbContext : DbContext
{
    public KotoNekoDbContext(DbContextOptions<KotoNekoDbContext> options)
        : base(options)
    {
    }

    public DbSet<SourceMaterial> SourceMaterials => Set<SourceMaterial>();
    public DbSet<Vocabulary> Vocabulary => Set<Vocabulary>();
    public DbSet<VocabularyMeaning> VocabularyMeanings => Set<VocabularyMeaning>();
    public DbSet<Conjugation> Conjugations => Set<Conjugation>();
    public DbSet<SrsItem> SrsItems => Set<SrsItem>();
    public DbSet<ReviewLog> ReviewLogs => Set<ReviewLog>();
    public DbSet<VocabularyAudio> VocabularyAudios => Set<VocabularyAudio>();

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

        modelBuilder.Entity<Vocabulary>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Japanese).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Reading).HasMaxLength(100).IsRequired();
            entity.Property(e => e.AlternateReadings).HasMaxLength(500).HasDefaultValue(string.Empty);
            entity.Property(e => e.ContextSentence).HasMaxLength(1000);
            entity.Property(e => e.Memo).HasMaxLength(1000);

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

            entity.HasOne(e => e.Srs)
                .WithOne(s => s.Vocabulary)
                .HasForeignKey<SrsItem>(s => s.VocabularyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Conjugations)
                .WithOne(c => c.Vocabulary)
                .HasForeignKey(c => c.VocabularyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.ReviewLogs)
                .WithOne(r => r.Vocabulary)
                .HasForeignKey(r => r.VocabularyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.IsAsleep);
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
            entity.HasIndex(e => e.VocabularyId).IsUnique();
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
    }
}
