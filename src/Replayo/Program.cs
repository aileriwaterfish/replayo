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

        if (args is ["--apply-update", var dossierMaj, var installation, var pidAncien]
            && int.TryParse(pidAncien, out var pidMaj))
        {
            Environment.ExitCode = UpdateInstaller.Executer(dossierMaj, installation, pidMaj);
            return;
        }

        // Diagnostic audio : capture N secondes du son d'un processus vers un wav.
        if (args is ["--test-loopback", var pid, var secondes, var sortieWav])
        {
            using var capture = new Audio.ProcessLoopbackCapture(int.Parse(pid));
            using var wav = new NAudio.Wave.WaveFileWriter(sortieWav, Audio.ProcessLoopbackCapture.Format);
            capture.EchantillonsRecus += (octets, n) => { lock (wav) wav.Write(octets, 0, n); };
            capture.Demarrer();
            Thread.Sleep(TimeSpan.FromSeconds(int.Parse(secondes)));
            Environment.Exit(0);
            return;
        }

        using var mutex = new Mutex(true, "Replayo-Instance-Unique", out var premiere);
        if (!premiere) return; // déjà lancé : ne rien faire

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Theme.Appliquer();
        var store = new ConfigStore();
        using var recorder = new RecorderService();
        using var annulationMaj = new CancellationTokenSource();
        var jetonMaj = annulationMaj.Token;
        var serviceMaj = new UpdateService();
        var verificationMajEnCours = 0;
        SettingsWindow? reglages = null;
        var cfgCourante = store.Charger();

        void OuvrirReglages()
        {
            if (reglages is { IsVisible: true }) { reglages.Activate(); return; }
            reglages = new SettingsWindow(store, recorder);
            reglages.Show();
        }

        Action sauvegarderClip = () => { }; // assignée juste après (dépendance croisée avec le tray)
        Action basculerEnregistrement = () => { };
        Action verifierMisesAJour = () => { };
        using var tray = new TrayIcon(recorder, () => sauvegarderClip(), () => basculerEnregistrement(),
            () => verifierMisesAJour(), OuvrirReglages, () => app.Shutdown());
        using var hotkey = new HotkeyManager();

        async Task VerifierMisesAJourAsync(bool manuel)
        {
            if (Interlocked.Exchange(ref verificationMajEnCours, 1) == 1) return;
            try
            {
                var maj = await serviceMaj.VerifierAsync(jetonMaj).ConfigureAwait(false);
                if (maj is null)
                {
                    if (manuel) app.Dispatcher.Invoke(() => tray.Notifier("Replayo est à jour."));
                    return;
                }

                if (!manuel)
                {
                    app.Dispatcher.Invoke(() => tray.Notifier($"Replayo v{maj.Version} disponible — ouvre le menu pour l'installer."));
                    return;
                }

                var accord = app.Dispatcher.Invoke(() => System.Windows.MessageBox.Show(
                    $"Replayo v{maj.Version} est disponible. Télécharger et installer ?\n\n" +
                    "Replayo redémarrera. Un REC en cours sera sauvegardé avant la fermeture.",
                    "Mise à jour Replayo", MessageBoxButton.YesNo, MessageBoxImage.Question));
                if (accord != MessageBoxResult.Yes) return;

                app.Dispatcher.Invoke(() => tray.Notifier("Téléchargement de la mise à jour…"));
                var dossier = await serviceMaj.PreparerAsync(maj, jetonMaj).ConfigureAwait(false);
                app.Dispatcher.Invoke(() =>
                {
                    UpdateService.LancerInstallation(dossier);
                    app.Shutdown();
                });
            }
            catch (OperationCanceledException) when (jetonMaj.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Journal.Ecrire($"[maj] erreur : {ex}");
                if (manuel && !app.Dispatcher.HasShutdownStarted)
                    app.Dispatcher.Invoke(() => tray.Notifier("Mise à jour impossible — consulte replayo.log."));
            }
            finally { Interlocked.Exchange(ref verificationMajEnCours, 0); }
        }

        verifierMisesAJour = () => _ = VerifierMisesAJourAsync(true);

        sauvegarderClip = () => _ = Task.Run(async () =>
        {
            try
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
                if (chemins.Count == 0) tray.Notifier("Aucun clip : le buffer n'est pas encore prêt.");
            }
            catch (Exception ex)
            {
                Journal.Ecrire($"[clip] tâche interrompue : {ex}");
                tray.Notifier("Clip non sauvegardé — consulte replayo.log.");
            }
        });

        basculerEnregistrement = () =>
        {
            if (recorder.EnEnregistrement)
                _ = Task.Run(async () =>
                {
                    try { await recorder.ArreterEnregistrementAsync(); }
                    catch (Exception ex) { Journal.Ecrire($"[rec] arrêt : {ex}"); }
                });
            else if (!recorder.DemarrerEnregistrement())
                tray.Notifier("Démarre d'abord le replay avant de lancer un REC.");
        };

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

        // Un contrôle discret au démarrage puis chaque jour ; installation seulement
        // après confirmation explicite dans le menu.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), jetonMaj);
                while (!jetonMaj.IsCancellationRequested)
                {
                    await VerifierMisesAJourAsync(false);
                    await Task.Delay(TimeSpan.FromDays(1), jetonMaj);
                }
            }
            catch (OperationCanceledException) { }
        });

        app.Run();
        annulationMaj.Cancel();
        recorder.Arreter();
    }
}
