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

    [Fact]
    public void SequenceDejaClippee_Ignoree()
    {
        var deja = new HashSet<TimeSpan> { TimeSpan.FromSeconds(94) };
        var clippables = LolModeService.SequencesClippables(
            [Seq(94, 112)], TimeSpan.FromSeconds(130), deja, Fusion);
        Assert.Empty(clippables);
    }
}
