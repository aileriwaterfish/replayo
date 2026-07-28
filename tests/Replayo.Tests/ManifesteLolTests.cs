using Replayo.Lol;
using Xunit;

public class ManifesteLolTests
{
    [Fact]
    public void EcrireLire_AllerRetourFidele()
    {
        var chemin = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "manifest.json");
        var manifeste = new ManifesteLol(
            Version: 1, Date: new DateTime(2026, 7, 29, 22, 15, 0), Queue: 420,
            Victoire: true, Retenue: true,
            DureeCibleMinSec: 60, DureeCibleMaxSec: 150, SeuilMinSec: 45,
            Sequences:
            [
                new("seq_01.mp4", Score: 65, DebutSec: 94, FinSec: 112, FichierDebutSec: 90, EvenementsSec: [100, 108]),
                new("seq_02.mp4", Score: 25, DebutSec: 300, FinSec: 315, FichierDebutSec: 295, EvenementsSec: [306]),
            ]);

        manifeste.Ecrire(chemin);
        var relu = ManifesteLol.Lire(chemin);

        Assert.NotNull(relu);
        Assert.Equal(manifeste.Queue, relu.Queue);
        Assert.True(relu.Victoire);
        Assert.Equal(2, relu.Sequences.Count);
        Assert.Equal("seq_01.mp4", relu.Sequences[0].Fichier);
        Assert.Equal(65, relu.Sequences[0].Score);
        Assert.Equal([100, 108], relu.Sequences[0].EvenementsSec);
    }

    [Fact]
    public void Lire_FichierAbsent_Null()
        => Assert.Null(ManifesteLol.Lire(Path.Combine(Path.GetTempPath(), "n_existe_pas", "manifest.json")));

    [Fact]
    public void Lire_FichierCorrompu_Null()
    {
        var chemin = Path.GetTempFileName();
        File.WriteAllText(chemin, "{pas du json");
        Assert.Null(ManifesteLol.Lire(chemin));
    }
}
