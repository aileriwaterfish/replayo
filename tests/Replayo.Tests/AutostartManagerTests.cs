using Replayo.Core;
using Xunit;

// Vérifie l'écriture/lecture réelle dans HKCU\...\Run (registre utilisateur, non destructif).
public class AutostartManagerTests
{
    [Fact]
    public void Activer_Desactiver_RefleteLEtatDansLeRegistre()
    {
        var etatInitial = AutostartManager.EstActive();
        try
        {
            AutostartManager.Activer();
            Assert.True(AutostartManager.EstActive());
            AutostartManager.Desactiver();
            Assert.False(AutostartManager.EstActive());
        }
        finally
        {
            // Restaure l'état d'avant le test.
            if (etatInitial) AutostartManager.Activer(); else AutostartManager.Desactiver();
        }
    }
}
