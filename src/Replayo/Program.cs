using System.Windows;
using Replayo.Core;
using Replayo.Input;
using Replayo.UI;

namespace Replayo;

/// Point d'entrée : application de fond (tray), fenêtres à la demande.
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Worker de montage LoL : même binaire, aucun UI, coexiste avec l'app (pas de mutex).
        if (args is ["--montage", var dossierGame])
        {
            Environment.Exit(Lol.MontageService.ExecuterAsync(dossierGame).GetAwaiter().GetResult());
            return;
        }

        using var mutex = new Mutex(true, "Replayo-Instance-Unique", out var premiere);
        if (!premiere) return; // déjà lancé : ne rien faire

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Theme.Appliquer();
        var store = new ConfigStore();
        using var recorder = new RecorderService();
        SettingsWindow? reglages = null;
        var cfgCourante = store.Charger();

        void OuvrirReglages()
        {
            if (reglages is { IsVisible: true }) { reglages.Activate(); return; }
            reglages = new SettingsWindow(store, recorder);
            reglages.Show();
        }

        Action sauvegarderClip = () => { }; // assignée juste après (dépendance croisée avec le tray)
        using var tray = new TrayIcon(recorder, () => sauvegarderClip(), OuvrirReglages, () => app.Shutdown());
        using var hotkey = new HotkeyManager();

        sauvegarderClip = () => _ = Task.Run(async () =>
        {
            var chemins = await recorder.ClipperAsync();
            foreach (var chemin in chemins)
                app.Dispatcher.Invoke(() =>
                {
                    var final = chemin;
                    if (cfgCourante.NommageManuel)
                    {
                        var dlg = new RenameDialog(chemin);
                        dlg.ShowDialog();
                        final = dlg.CheminFinal;
                    }
                    ToastWindow.Afficher(Path.GetFileName(final), cfgCourante.DureeBufferSecondes);
                });
            if (chemins.Count == 0) tray.Notifier("Aucun clip : la capture n'est pas active.");
        });

        void BrancherRaccourci(ReplayoConfig cfg)
        {
            hotkey.Desenregistrer();
            var ok = hotkey.Enregistrer(cfg.RaccourciModificateurs, cfg.RaccourciTouche, () => sauvegarderClip());
            if (!ok) tray.Notifier("Le raccourci est déjà utilisé par une autre application — changez-le dans les réglages.");
        }

        if (!store.Existe)
        {
            var onboarding = new OnboardingWindow(store);
            if (onboarding.ShowDialog() != true) { app.Shutdown(); return; } // onboarding refusé → quitter
            cfgCourante = store.Charger();
        }
        recorder.Demarrer(cfgCourante);
        BrancherRaccourci(cfgCourante);

        // Mode LoL : détection de game + condensés automatiques (voir docs/superpowers/specs/2026-07-29).
        using var lol = new Replayo.Lol.LolModeService(recorder, () => cfgCourante);
        lol.Notification += tray.Notifier;
        lol.Demarrer();

        // Réglages enregistrés → re-brancher le raccourci (il a pu changer).
        SettingsWindow.ConfigChangee += nouvelle => { cfgCourante = nouvelle; BrancherRaccourci(nouvelle); };

        app.Run();
        recorder.Arreter();
    }
}
