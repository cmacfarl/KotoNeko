namespace KotoNeko.Core.Domain;

public class VocabularyAudio
{
    public int Id { get; set; }
    public int VocabularyId { get; set; }
    public byte[] WordAudio { get; set; } = Array.Empty<byte>();
    public byte[]? SentenceAudio { get; set; }
    public Vocabulary? Vocabulary { get; set; }
}
