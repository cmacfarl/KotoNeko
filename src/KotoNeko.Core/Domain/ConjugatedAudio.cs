namespace KotoNeko.Core.Domain;

public class ConjugatedAudio
{
    public int Id { get; set; }
    public int ConjugationId { get; set; }
    public Conjugation Conjugation { get; set; } = null!;
    public byte[] Audio { get; set; } = Array.Empty<byte>();
}
