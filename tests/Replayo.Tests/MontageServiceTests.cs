using Replayo.Lol;
using Xunit;

public class MontageServiceTests
{
    private static SequenceManifeste Seq(string fichier, int score, double debut, double fin, double fichierDebut, params double[] evts)
        => new(fichier, score, debut, fin, fichierDebut, evts);

    private static SequenceManifeste Resultat(double debut, double fin)
        => new("seq_fin.mp4", 0, debut, fin, debut - 3, [debut + 6], EstResultat: true);

    [Fact]
    public void Selectionner_LeResultatNestJamaisEjecte_EtResteEnDernier()
    {
        var seqs = new List<SequenceManifeste>
        {
            Seq("seq_01.mp4", 40, 100, 220, 95, 130),  // 120 s
            Seq("seq_02.mp4", 25, 300, 420, 295, 330), // 120 s — score le plus faible
            Resultat(2000, 2012),                       // 12 s, score 0
        };
        var retenues = MontageService.Selectionner(seqs, maxSec: 150);
        // budget hors résultat = 138 s → une seule séquence normale tient
        Assert.Equal(["seq_01.mp4", "seq_fin.mp4"], retenues.Select(s => s.Fichier));
        Assert.True(retenues[^1].EstResultat);
    }

    [Fact]
    public void Timeline_LeResultatClotLaVideo()
    {
        var m = Manifeste(
            Seq("seq_01.mp4", 25, 100, 130, 95, 110),
            Resultat(2000, 2012));
        var plans = MontageService.Timeline(m);
        Assert.Equal(2, plans.Count);
        Assert.Equal("seq_01.mp4", plans[0].Fichier);
        Assert.Equal("seq_fin.mp4", plans[^1].Fichier); // le résultat clôt la vidéo
    }

    private static ManifesteLol Manifeste(params SequenceManifeste[] seqs)
        => new(1, new DateTime(2026, 7, 29), 420, Victoire: false, Retenue: true, 60, 150, 45, [.. seqs]);

    [Fact]
    public void Selectionner_CoupeAuScoreLePlusFaible_OrdreChronoConserve()
    {
        var seqs = new List<SequenceManifeste>
        {
            Seq("seq_01.mp4", 40, 100, 160, 95, 130),  // 60 s
            Seq("seq_02.mp4", 25, 300, 360, 295, 330), // 60 s — score le plus faible
            Seq("seq_03.mp4", 65, 500, 560, 495, 530), // 60 s
        };
        var retenues = MontageService.Selectionner(seqs, maxSec: 150);
        Assert.Equal(["seq_01.mp4", "seq_03.mp4"], retenues.Select(s => s.Fichier));
    }

    [Fact]
    public void Selectionner_SousLaLimite_ToutGarde()
    {
        var seqs = new List<SequenceManifeste> { Seq("seq_01.mp4", 25, 100, 130, 95, 110) };
        Assert.Single(MontageService.Selectionner(seqs, 150));
    }

    [Fact]
    public void Timeline_PurementChronologique_SansColdOpen()
    {
        var m = Manifeste(
            Seq("seq_01.mp4", 40, 100, 160, 95, 130),
            Seq("seq_02.mp4", 65, 300, 360, 295, 330));
        var plans = MontageService.Timeline(m);
        Assert.Equal(2, plans.Count);                 // aucun teaser ajouté
        Assert.Equal("seq_01.mp4", plans[0].Fichier); // ordre chronologique strict
        Assert.Equal(5.0, plans[0].DepartSec, 3);     // 100 − 95
        Assert.Equal(60.0, plans[0].DureeSec, 3);
        Assert.Equal("seq_02.mp4", plans[1].Fichier);
    }

    [Fact]
    public void ArgumentsFfmpeg_AudioIsole_UtiliseLaPisteDediee()
    {
        var plans = new List<PlanDeCoupe>
        {
            new("seq_01.mp4", 4, 18, DebutCaptureSec: 94),
            new("seq_02.mp4", 4, 18, DebutCaptureSec: 304),
        };
        var args = MontageService.ArgumentsFfmpeg(plans, @"C:\game", @"C:\game\condense.mp4",
            audioLol: "audio_lol.m4a", audioLolDebutSec: 10);
        Assert.Contains("audio_lol.m4a", args);
        Assert.Contains("[2:a]", args);               // audio du plan 0 = entrée n+0
        Assert.Contains("[3:a]", args);
        Assert.DoesNotContain("[0:a]", args);         // le mix des clips n'est pas utilisé
        Assert.Contains("-ss 84.000", args);          // 94 − 10 : alignement horloge de capture
        Assert.Contains("-ss 294.000", args);
    }

    [Fact]
    public void ArgumentsFfmpeg_CropCentralEtConcat()
    {
        var plans = new List<PlanDeCoupe>
        {
            new("seq_01.mp4", 5, 3.5), new("seq_01.mp4", 5, 60), new("seq_02.mp4", 5, 60),
        };
        var args = MontageService.ArgumentsFfmpeg(plans, @"C:\game", @"C:\game\condense.mp4");
        Assert.Contains("crop=1080:1080:420:0", args); // carré central (zoom réduit)
        Assert.Contains("boxblur", args);              // fond flouté 9:16
        Assert.Contains("overlay=0:420", args);
        Assert.Contains("concat=n=3:v=1:a=1", args);
        Assert.Contains("libx264", args);
        Assert.Contains("condense.mp4", args);
    }

    /// Cas réel du 2026-08-17 : la séquence commence AVANT le segment qui la porte
    /// (DebutSec 12446,471 < FichierDebutSec 12448,371), ce qui produisait
    /// `-ss -1.900` et un plan qui partait n'importe où.
    [Fact]
    public void Timeline_SequenceCommencantAvantSonFichier_NeProduitPasDeDepartNegatif()
    {
        var m = Manifeste(Seq("seq_01.mp4", 355, 12446.4712895, 12461.4521244, 12448.3705874, 12456.5));
        var plans = MontageService.Timeline(m);
        var p = Assert.Single(plans);
        Assert.Equal(0.0, p.DepartSec, 3);
        // Fin inchangée : on ne perd que la tête manquante (1,900 s sur 14,981 s)
        // → 12461,4521244 − 12448,3705874.
        Assert.Equal(13.081537, p.DureeSec, 5);
        // L'audio isolé doit être décalé d'autant, sinon il part 1,9 s trop tôt.
        Assert.Equal(12448.3705874, p.DebutCaptureSec, 4);
    }

    [Fact]
    public void Timeline_SequenceEntierementHorsDeSonFichier_EstEcartee()
    {
        var m = Manifeste(Seq("seq_01.mp4", 100, 500, 510, 520, 505));
        Assert.Empty(MontageService.Timeline(m));
    }

    [Fact]
    public void Timeline_CasNormal_InchangeParLeGarde()
    {
        var m = Manifeste(Seq("seq_01.mp4", 40, 100, 160, 95, 130));
        var p = Assert.Single(MontageService.Timeline(m));
        Assert.Equal(5.0, p.DepartSec, 3);
        Assert.Equal(60.0, p.DureeSec, 3);
        Assert.Equal(100.0, p.DebutCaptureSec, 3);
    }
}
