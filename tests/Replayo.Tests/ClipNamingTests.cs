using Replayo.Clip;
using Xunit;

public class ClipNamingTests
{
    [Fact]
    public void ConstruireCheminSortie_RangeParAppliPuisMois()
    {
        var chemin = ClipService.ConstruireCheminSortie(
            @"C:\Videos\Replayo", "RocketLeague", new DateTime(2026, 7, 15, 18, 32, 5), "mp4", null);
        Assert.Equal(@"C:\Videos\Replayo\RocketLeague\2026-07\Replayo_RocketLeague_2026-07-15_18h32m05.mp4", chemin);
    }

    [Fact]
    public void ConstruireCheminSortie_NettoieLesCaracteresInterdits()
    {
        var chemin = ClipService.ConstruireCheminSortie(
            @"C:\V", @"App: <Test>", new DateTime(2026, 1, 2, 3, 4, 5), "mkv", "ecran2");
        Assert.DoesNotContain(':', Path.GetFileName(chemin));
        Assert.DoesNotContain('<', chemin.Substring(3));
        Assert.EndsWith("_ecran2.mkv", chemin);
    }
}
