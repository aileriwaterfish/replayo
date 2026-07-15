using Replayo.UI;
using Xunit;

public class RenameTests
{
    private static string ClipTemporaire()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var chemin = Path.Combine(dir, "Replayo_App_2026-07-15_18h00m00.mp4");
        File.WriteAllText(chemin, "x");
        return chemin;
    }

    [Fact]
    public void RenommerFichier_DeplaceVersLeNouveauNom()
    {
        var clip = ClipTemporaire();
        var resultat = RenameDialog.RenommerFichier(clip, "Mon Super Clip");
        Assert.EndsWith("Mon Super Clip.mp4", resultat);
        Assert.True(File.Exists(resultat));
        Assert.False(File.Exists(clip));
    }

    [Fact]
    public void RenommerFichier_NomVide_GardeLAncien()
    {
        var clip = ClipTemporaire();
        var resultat = RenameDialog.RenommerFichier(clip, "   ");
        Assert.Equal(clip, resultat);
        Assert.True(File.Exists(clip));
    }

    [Fact]
    public void RenommerFichier_FiltreLesCaracteresInterdits()
    {
        var clip = ClipTemporaire();
        var resultat = RenameDialog.RenommerFichier(clip, "a<b>c:d");
        Assert.EndsWith("abcd.mp4", resultat);
        Assert.True(File.Exists(resultat));
    }
}
