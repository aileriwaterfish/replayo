using System.Windows;
using Replayo.Core;
using Replayo.Input;
using Replayo.UI;

namespace Replayo;

/// Point d'entrée : application de fond (tray), fenêtres à la demande.
public static class Program
{
    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(true, "Replayo-Instance-Unique", out var premiere);
        if (!premiere) return; // déjà lancé : ne rien faire

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var store = new ConfigStore();
        using var recorder = new RecorderService();
        SettingsWindow? reglages = null;

        void OuvrirReglages()
        {
            if (reglages is { IsVisible: true }) { reglages.Activate(); return; }
            reglages = new SettingsWindow(store, recorder);
            reglages.Show();
        }

        using var tray = new TrayIcon(recorder, OuvrirReglages, () => app.Shutdown());
        using var hotkey = new HotkeyManager();

        void BrancherRaccourci(ReplayoConfig cfg)
        {
            hotkey.Desenregistrer();
            var ok = hotkey.Enregistrer(cfg.RaccourciModificateurs, cfg.RaccourciTouche, () =>
                _ = Task.Run(async () =>
                {
                    var chemins = await recorder.ClipperAsync();
                    foreach (var chemin in chemins)
                        app.Dispatcher.Invoke(() =>
                        {
                            if (cfg.NommageManuel) new RenameDialog(chemin).ShowDialog();
                        });
                    if (chemins.Count == 0) tray.Notifier("Aucun clip : la capture n'est pas active.");
                }));
            if (!ok) tray.Notifier("Le raccourci est déjà utilisé par une autre application — changez-le dans les réglages.");
        }

        var cfg = store.Charger();
        if (!store.Existe)
        {
            var onboarding = new OnboardingWindow(store);
            if (onboarding.ShowDialog() != true) { app.Shutdown(); return; } // onboarding refusé → quitter
            cfg = store.Charger();
        }
        recorder.Demarrer(cfg);
        tray.RafraichirEtat();
        BrancherRaccourci(cfg);

        // Réglages enregistrés → re-brancher le raccourci (il a pu changer) + rafraîchir le tray.
        SettingsWindow.ConfigChangee += nouvelle => { BrancherRaccourci(nouvelle); tray.RafraichirEtat(); };

        app.Run();
        recorder.Arreter();
    }
}
