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
    public void Timeline_ColdOpenJamaisSurLeResultat()
    {
        var m = Manifeste(
            Seq("seq_01.mp4", 25, 100, 130, 95, 110),
            Resultat(2000, 2012));
        var plans = MontageService.Timeline(m);
        Assert.Equal(3, plans.Count);
        Assert.Equal("seq_01.mp4", plans[0].Fichier);   // teaser sur le play, pas la fin de game
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
    public void ColdOpen_TeaserAvantLaResolution()
    {
        // dernier événement à 108 s ; fichier démarre à 90 s
        var s = Seq("seq_01.mp4", 65, 94, 112, 90, 100, 108);
        var plan = MontageService.ColdOpen(s);
        Assert.Equal("seq_01.mp4", plan.Fichier);
        Assert.Equal(14.0, plan.DepartSec, 3);  // (108 − 0,5 − 3,5) − 90
        Assert.Equal(3.5, plan.DureeSec, 3);
    }

    [Fact]
    public void ColdOpen_ClampeAuDebutDuFichier()
    {
        var s = Seq("seq_01.mp4", 25, 91, 99, 90, 92); // événement 2 s après le début du fichier
        var plan = MontageService.ColdOpen(s);
        Assert.Equal(0.0, plan.DepartSec, 3);
        Assert.True(plan.DureeSec is > 0 and <= 3.5);
    }

    [Fact]
    public void Timeline_ColdOpenPuisChrono()
    {
        var m = Manifeste(
            Seq("seq_01.mp4", 40, 100, 160, 95, 130),
            Seq("seq_02.mp4", 65, 300, 360, 295, 330));
        var plans = MontageService.Timeline(m);
        Assert.Equal(3, plans.Count);
        Assert.Equal("seq_02.mp4", plans[0].Fichier);        // teaser = meilleure séquence
        Assert.Equal(3.5, plans[0].DureeSec, 3);
        Assert.Equal("seq_01.mp4", plans[1].Fichier);        // puis chronologique
        Assert.Equal(5.0, plans[1].DepartSec, 3);            // 100 − 95
        Assert.Equal(60.0, plans[1].DureeSec, 3);
        Assert.Equal("seq_02.mp4", plans[2].Fichier);
    }

    [Fact]
    public void Timeline_UneSeuleSequence_PasDeTeaser()
    {
        var m = Manifeste(Seq("seq_01.mp4", 40, 100, 160, 95, 130));
        Assert.Single(MontageService.Timeline(m));
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
}
