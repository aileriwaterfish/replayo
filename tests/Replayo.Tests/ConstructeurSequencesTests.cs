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
    public void DebutClampeAZero()
    {
        var seqs = Construire((2, 25));
        Assert.Equal(TimeSpan.Zero, Assert.Single(seqs).Debut);
    }

    [Fact]
    public void EntreeNonTriee_MemeResultat()
    {
        var desordre = Construire((108, 25), (100, 25));
        var ordre = Construire((100, 25), (108, 25));
        Assert.Equal(ordre.Select(s => (s.Debut, s.Fin, s.Score)), desordre.Select(s => (s.Debut, s.Fin, s.Score)));
    }
}
