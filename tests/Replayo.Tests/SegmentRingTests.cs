using Replayo.Buffer;
using Xunit;

public class SegmentRingTests
{
    private static SegmentRing NouvelAnneau(int dureeMax = 60)
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return new SegmentRing(dir, dureeMax);
    }

    [Fact]
    public void SegmentsPourDuree_RendLesSegmentsCouvrantLaFenetre()
    {
        var ring = NouvelAnneau(dureeMax: 60);
        // 6 segments de 10 s : [0-10] ... [50-60]
        for (int i = 0; i < 6; i++)
        {
            var chemin = ring.ProchainCheminSegment();
            File.WriteAllText(chemin, "x");
            ring.Ajouter(chemin, TimeSpan.FromSeconds(i * 10), TimeSpan.FromSeconds(i * 10 + 10));
        }
        // Les 25 dernières secondes à t=60 → doit couvrir [35,60] → segments [30-40],[40-50],[50-60]
        var clips = ring.SegmentsPourDuree(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(60));
        Assert.Equal(3, clips.Count);
    }

    [Fact]
    public void Ajouter_SupprimeLesSegmentsHorsFenetre()
    {
        var ring = NouvelAnneau(dureeMax: 30); // fenêtre max 30 s + marge d'1 segment
        var chemins = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            var chemin = ring.ProchainCheminSegment();
            File.WriteAllText(chemin, "x");
            chemins.Add(chemin);
            ring.Ajouter(chemin, TimeSpan.FromSeconds(i * 10), TimeSpan.FromSeconds(i * 10 + 10));
        }
        // à t=60, fenêtre 30 s → [30,60] : les segments [0-10] et [10-20] doivent être supprimés du disque
        Assert.False(File.Exists(chemins[0]));
        Assert.False(File.Exists(chemins[1]));
        Assert.True(File.Exists(chemins[5]));
    }

    [Fact]
    public void SegmentsPourIntervalle_RendLesSegmentsChevauchants()
    {
        var ring = NouvelAnneau(dureeMax: 60);
        for (int i = 0; i < 3; i++)
        {
            var chemin = ring.ProchainCheminSegment();
            File.WriteAllText(chemin, "x");
            ring.Ajouter(chemin, TimeSpan.FromSeconds(i * 10), TimeSpan.FromSeconds(i * 10 + 10));
        }
        Assert.Single(ring.SegmentsPourIntervalle(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(18)));
        Assert.Equal(3, ring.SegmentsPourIntervalle(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(22)).Count);
        Assert.Empty(ring.SegmentsPourIntervalle(TimeSpan.FromSeconds(35), TimeSpan.FromSeconds(40)));
    }

    [Fact]
    public void IntervalleAvecDebut_RendLeDebutDuPremierSegment()
    {
        var ring = NouvelAnneau(dureeMax: 60);
        for (int i = 0; i < 3; i++)
        {
            var chemin = ring.ProchainCheminSegment();
            File.WriteAllText(chemin, "x");
            ring.Ajouter(chemin, TimeSpan.FromSeconds(i * 10), TimeSpan.FromSeconds(i * 10 + 10));
        }
        var (segments, debut) = ring.IntervalleAvecDebut(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(18));
        Assert.Single(segments);
        Assert.Equal(TimeSpan.FromSeconds(10), debut);
    }

    [Fact]
    public void DureeMax_ModifiableAChaud()
    {
        var ring = NouvelAnneau(dureeMax: 20);
        ring.DureeMaxSecondes = 120; // mode LoL : fenêtre étendue
        var chemins = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            var chemin = ring.ProchainCheminSegment();
            File.WriteAllText(chemin, "x");
            chemins.Add(chemin);
            ring.Ajouter(chemin, TimeSpan.FromSeconds(i * 10), TimeSpan.FromSeconds(i * 10 + 10));
        }
        Assert.True(File.Exists(chemins[0])); // à t=60, fenêtre 120 s → rien purgé

        ring.DureeMaxSecondes = 20; // fin de game : retour à la normale
        var dernier = ring.ProchainCheminSegment();
        File.WriteAllText(dernier, "x");
        ring.Ajouter(dernier, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(70));
        Assert.False(File.Exists(chemins[0])); // purge reprise au prochain Ajouter
        Assert.True(File.Exists(dernier));
    }

    [Fact]
    public void PurgerAuDemarrage_VideLeDossier()
    {
        var ring = NouvelAnneau();
        var chemin = ring.ProchainCheminSegment();
        File.WriteAllText(chemin, "x");
        ring.PurgerAuDemarrage();
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(chemin)!));
    }

    [Fact]
    public void Location_ProtegeUnSegmentContreLaPurgePuisLeSupprimeALaLiberation()
    {
        var ring = NouvelAnneau(dureeMax: 20);
        var ancien = ring.ProchainCheminSegment();
        File.WriteAllText(ancien, "x");
        ring.Ajouter(ancien, TimeSpan.Zero, TimeSpan.FromSeconds(10));

        using (var location = ring.LouerIntervalle(TimeSpan.Zero, TimeSpan.FromSeconds(10)))
        {
            Assert.Equal(ancien, Assert.Single(location.Segments));
            ring.PurgerAuDemarrage();
            Assert.True(File.Exists(ancien));
        }

        Assert.False(File.Exists(ancien));
    }

    [Fact]
    public void LocationsImbriquees_AttendentLaDerniereLiberation()
    {
        var ring = NouvelAnneau(dureeMax: 20);
        var ancien = ring.ProchainCheminSegment();
        File.WriteAllText(ancien, "x");
        ring.Ajouter(ancien, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        using var premiere = ring.LouerIntervalle(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        var seconde = ring.LouerIntervalle(TimeSpan.Zero, TimeSpan.FromSeconds(10));

        ring.PurgerAuDemarrage();
        seconde.Dispose();
        Assert.True(File.Exists(ancien));
        premiere.Dispose();
        Assert.False(File.Exists(ancien));
    }

    [Fact]
    public void Enregistrement_ProtegeLesSegmentsMalgreLaFenetreLolPuisLesLibere()
    {
        var ring = NouvelAnneau(dureeMax: 20);
        ring.ProtegerDepuis(TimeSpan.FromSeconds(5));
        var chemins = new List<string>();
        for (int i = 0; i < 7; i++)
        {
            var chemin = ring.ProchainCheminSegment();
            File.WriteAllText(chemin, "x");
            chemins.Add(chemin);
            ring.Ajouter(chemin, TimeSpan.FromSeconds(i * 10), TimeSpan.FromSeconds(i * 10 + 10));
            if (i == 3) ring.DureeMaxSecondes = 120;
            if (i == 5) ring.DureeMaxSecondes = 20;
        }

        Assert.All(chemins, chemin => Assert.True(File.Exists(chemin)));
        using (var location = ring.LouerIntervalle(TimeSpan.FromSeconds(5), TimeSpan.MaxValue))
        {
            ring.NePlusProteger();
            Assert.Equal(7, location.Segments.Count);
            Assert.True(File.Exists(chemins[0]));
        }
        Assert.False(File.Exists(chemins[0]));
        Assert.True(File.Exists(chemins[6]));
    }
}
