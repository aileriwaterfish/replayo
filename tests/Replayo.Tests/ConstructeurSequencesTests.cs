using Replayo.Lol;
using Xunit;

public class ConstructeurSequencesTests
{
    private static readonly TimeSpan Fusion = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan Avant = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan Apres = TimeSpan.FromSeconds(4);

    private static List<SequenceLol> Construire(params (double T, int Score)[] evts)
        => ConstructeurSequences.Construire(
            evts.Select(e => (TimeSpan.FromSeconds(e.T), e.Score)), Fusion, Avant, Apres);

    [Fact]
    public void DeuxKillsProches_UneSequenceFusionnee()
    {
        var seqs = Construire((100, 25), (108, 25));
        var s = Assert.Single(seqs);
        Assert.Equal(TimeSpan.FromSeconds(94), s.Debut);   // 100 − 6
        Assert.Equal(TimeSpan.FromSeconds(112), s.Fin);    // 108 + 4
        Assert.Equal(50, s.Score);
        Assert.Equal(2, s.Evenements.Count);
    }

    [Fact]
    public void DeuxKillsEloignes_DeuxSequences()
    {
        var seqs = Construire((100, 25), (120, 25));
        Assert.Equal(2, seqs.Count);
    }

    [Fact]
    public void KillPuisMort_UneSequenceIncluantLaMort()
    {
        // le 1v3 : je tue, puis je meurs 5 s après → le play se montre en entier
        var seqs = Construire((100, 25), (105, 0));
        var s = Assert.Single(seqs);
        Assert.Equal(25, s.Score);
        Assert.Equal(TimeSpan.FromSeconds(109), s.Fin); // mort (105) + 4
    }

    [Fact]
    public void MortIsolee_AucuneSequence()
        => Assert.Empty(Construire((200, 0)));

    [Fact]
    public void MortLoinApresLeKill_EcarteeDeLaSequence()
    {
        // kill à 100 s, mort à 115 s (15 s plus tard : hors fenêtre mort de 8 s,
        // mais dans la fusion normale de 12 s ? non — 15 > 12 ici ; testons avec fusion 18)
        var seqs = ConstructeurSequences.Construire(
            [(TimeSpan.FromSeconds(100), 25), (TimeSpan.FromSeconds(115), 0)],
            fusion: TimeSpan.FromSeconds(18), Avant, Apres, fusionContexte: TimeSpan.FromSeconds(8));
        var s = Assert.Single(seqs); // la mort est écartée (groupe séparé à 0 pt)
        Assert.Equal(TimeSpan.FromSeconds(104), s.Fin); // 100 + 4 (Apres du test) : fini au kill
        Assert.Single(s.Evenements);
    }

    [Fact]
    public void MortJusteApresLeKill_ResteDansLaSequence()
    {
        // le 1v3 : mort 5 s après le kill → dans la fenêtre mort de 8 s
        var seqs = ConstructeurSequences.Construire(
            [(TimeSpan.FromSeconds(100), 25), (TimeSpan.FromSeconds(105), 0)],
            fusion: TimeSpan.FromSeconds(18), Avant, Apres, fusionContexte: TimeSpan.FromSeconds(8));
        var s = Assert.Single(seqs);
        Assert.Equal(TimeSpan.FromSeconds(109), s.Fin); // mort (105) + 4
        Assert.Equal(2, s.Evenements.Count);
    }

    [Fact]
    public void MortLoinAvantLeKill_NAllongePasLOuverture()
    {
        // mort à 100 s, kill à 115 s : sans la fenêtre mort, le kill fusionnerait
        // (15 < 18) et la séquence commencerait 5 s avant la mort → 20 s creuses
        var seqs = ConstructeurSequences.Construire(
            [(TimeSpan.FromSeconds(100), 0), (TimeSpan.FromSeconds(115), 25)],
            fusion: TimeSpan.FromSeconds(18), Avant, Apres, fusionContexte: TimeSpan.FromSeconds(8));
        var s = Assert.Single(seqs);
        Assert.Equal(TimeSpan.FromSeconds(109), s.Debut); // 115 − 6 (Avant du test) : la mort est écartée
    }

    [Fact]
    public void DebutClampeAZero()
    {
        var seqs = Construire((2, 25));
        Assert.Equal(TimeSpan.Zero, Assert.Single(seqs).Debut);
    }

    [Fact]
    public void GrosPlay_OuvertureElargie()
    {
        // triple kill (25+25+25+35 = 110 ≥ 50) → l'avant passe à 10 s (rotation visible)
        var seqs = ConstructeurSequences.Construire(
            [(TimeSpan.FromSeconds(100), 25), (TimeSpan.FromSeconds(104), 25), (TimeSpan.FromSeconds(108), 60)],
            Fusion, Avant, Apres, avantMajeur: TimeSpan.FromSeconds(10), seuilMajeur: 50);
        Assert.Equal(TimeSpan.FromSeconds(90), Assert.Single(seqs).Debut); // 100 − 10
    }

    [Fact]
    public void KillIsole_OuvertureNormale_MemeAvecAvantMajeur()
    {
        var seqs = ConstructeurSequences.Construire(
            [(TimeSpan.FromSeconds(100), 25)],
            Fusion, Avant, Apres, avantMajeur: TimeSpan.FromSeconds(10), seuilMajeur: 50);
        Assert.Equal(TimeSpan.FromSeconds(94), Assert.Single(seqs).Debut); // 100 − 6 (Avant du test)
    }

    [Fact]
    public void EntreeNonTriee_MemeResultat()
    {
        var desordre = Construire((108, 25), (100, 25));
        var ordre = Construire((100, 25), (108, 25));
        Assert.Equal(ordre.Select(s => (s.Debut, s.Fin, s.Score)), desordre.Select(s => (s.Debut, s.Fin, s.Score)));
    }
}
