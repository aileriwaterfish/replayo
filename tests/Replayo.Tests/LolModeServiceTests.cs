using Replayo.Lol;
using Xunit;

public class LolModeServiceTests
{
    private static readonly TimeSpan Fusion = TimeSpan.FromSeconds(12);

    private static SequenceLol Seq(double debut, double fin)
        => new(TimeSpan.FromSeconds(debut), TimeSpan.FromSeconds(fin), 25, [TimeSpan.FromSeconds(debut + 6)]);

    [Fact]
    public void SequenceFinieDepuisPlusDeFusion_Clippable()
    {
        var clippables = LolModeService.SequencesClippables(
            [Seq(94, 112)], TimeSpan.FromSeconds(130), new HashSet<TimeSpan>(), Fusion);
        Assert.Single(clippables);
    }

    [Fact]
    public void SequenceEncoreOuverte_PasClippable()
    {
        // Fin=112, horloge=120 : un événement peut encore fusionner (120 < 112+12... la fenêtre
        // se mesure au dernier événement + fusion ; ici on reste prudent : Fin + fusion)
        var clippables = LolModeService.SequencesClippables(
            [Seq(94, 112)], TimeSpan.FromSeconds(120), new HashSet<TimeSpan>(), Fusion);
        Assert.Empty(clippables);
    }

    private static List<(TimeSpan T, double Pv)> Pv(params (double T, double Pv)[] e)
        => e.Select(x => (TimeSpan.FromSeconds(x.T), x.Pv)).ToList();

    [Fact]
    public void DebutCalme_PvStables_OuvertureInchangee()
        => Assert.Equal(TimeSpan.FromSeconds(100), LolModeService.DebutCalme(
            TimeSpan.FromSeconds(100), Pv((94, 800), (96, 800), (98, 800), (100, 800))));

    [Fact]
    public void DebutCalme_DejaEnCombat_ReculeJusquAuCalme()
    {
        // PV stables jusqu'à 92 s, puis chute (combat) : l'ouverture prévue à 100 s
        // recule avant l'engagement
        var pv = Pv((84, 800), (86, 800), (88, 800), (90, 800), (92, 800), (94, 640), (96, 500), (98, 420), (100, 350));
        var debut = LolModeService.DebutCalme(TimeSpan.FromSeconds(100), pv);
        Assert.True(debut <= TimeSpan.FromSeconds(92), $"début={debut}");
    }

    [Fact]
    public void DebutCalme_JamaisCalme_PlafonneAuReculMax()
    {
        var pv = Pv((80, 999), (82, 950), (84, 900), (86, 850), (88, 800), (90, 750), (92, 700), (94, 650), (96, 600), (98, 550), (100, 500));
        Assert.Equal(TimeSpan.FromSeconds(85), LolModeService.DebutCalme(TimeSpan.FromSeconds(100), pv));
    }

    [Fact]
    public void DebutCalme_SansEchantillons_OuvertureInchangee()
        => Assert.Equal(TimeSpan.FromSeconds(100), LolModeService.DebutCalme(TimeSpan.FromSeconds(100), Pv()));

    [Fact]
    public void SequenceDejaClippee_Ignoree()
    {
        var deja = new HashSet<TimeSpan> { TimeSpan.FromSeconds(94) };
        var clippables = LolModeService.SequencesClippables(
            [Seq(94, 112)], TimeSpan.FromSeconds(130), deja, Fusion);
        Assert.Empty(clippables);
    }
}
