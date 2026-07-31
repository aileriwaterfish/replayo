using Microsoft.Win32;
using Replayo.Core;
using Xunit;

// Vérifie l'écriture/lecture réelle dans HKCU\...\Run, sur une valeur bidon.
// Ne JAMAIS écrire sur la vraie valeur "Replayo" ici : Activer() enregistre
// Environment.ProcessPath, qui vaut testhost.exe sous le runner — le test y laissait
// le chemin du runner et Replayo ne démarrait plus avec la session (31/07/2026).
public class AutostartManagerTests
{
    private const string CleRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NomBidon = "Replayo.TestAutostart";

    [Fact]
    public void Activer_Desactiver_RefleteLEtatDansLeRegistre()
    {
        try
        {
            AutostartManager.Activer(NomBidon);
            Assert.True(AutostartManager.EstActive(NomBidon));
            AutostartManager.Desactiver(NomBidon);
            Assert.False(AutostartManager.EstActive(NomBidon));
        }
        finally
        {
            AutostartManager.Desactiver(NomBidon);
        }
    }

    // Filet : si un test réintroduit une écriture sur la vraie valeur, il échoue ici.
    [Fact]
    public void LesTests_NeTouchentPas_ALaVraieValeurDeDemarrage()
    {
        using var k = Registry.CurrentUser.OpenSubKey(CleRun);
        if (k?.GetValue("Replayo") is not string chemin) return; // autostart désactivé : rien à protéger

        Assert.DoesNotContain("testhost", chemin, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\bin\", chemin, StringComparison.OrdinalIgnoreCase);
    }
}
