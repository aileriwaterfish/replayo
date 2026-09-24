using System.Diagnostics;

namespace Replayo.Core;

/// Tourne depuis une copie temporaire de Replayo.Updater.exe, après la fermeture de Replayo.
internal static class UpdateInstaller
{
    private static readonly string[] Fichiers =
        ["Replayo.exe", "Replayo.Updater.exe", "ffmpeg.exe",
         "D3DCompiler_47_cor3.dll", "PenImc_cor3.dll", "PresentationNative_cor3.dll",
         "vcruntime140_cor3.dll", "wpfgfx_cor3.dll",
         "assets/clip.wav", "assets/replayo.ico", "assets/tray.ico"];

    internal static int Executer(string dossier, string installation, int pid)
    {
        try
        {
            try
            {
                using var ancien = Process.GetProcessById(pid);
                if (!ancien.WaitForExit((int)TimeSpan.FromMinutes(5).TotalMilliseconds))
                    throw new TimeoutException("Replayo ne s'est pas fermé à temps.");
            }
            catch (ArgumentException) { /* déjà fermé */ }

            Appliquer(Path.Combine(dossier, "contenu"), installation, Path.Combine(dossier, "sauvegarde"));
            var exe = Path.Combine(installation, "Replayo.exe");
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = installation, UseShellExecute = true });
            Journal.Ecrire("[maj] installation terminée, Replayo relancé");
            return 0;
        }
        catch (Exception ex)
        {
            Journal.Ecrire($"[maj] installation échouée : {ex}");
            // Après restauration, ne pas laisser l'utilisateur sans capture.
            try
            {
                var exe = Path.Combine(installation, "Replayo.exe");
                if (File.Exists(exe))
                    Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = installation, UseShellExecute = true });
            }
            catch (Exception redemarrage) { Journal.Ecrire($"[maj] relance de l'ancienne version échouée : {redemarrage}"); }
            return 1;
        }
    }

    internal static void Appliquer(string contenu, string installation, string sauvegarde)
    {
        foreach (var relatif in Fichiers)
            if (!File.Exists(Path.Combine(contenu, relatif.Replace('/', Path.DirectorySeparatorChar))))
                throw new InvalidDataException($"Fichier manquant dans la mise à jour : {relatif}");

        Directory.CreateDirectory(sauvegarde);
        var touches = new List<(string Cible, string? Ancien)>();
        try
        {
            foreach (var relatif in Fichiers)
            {
                var cheminRelatif = relatif.Replace('/', Path.DirectorySeparatorChar);
                var cible = Path.Combine(installation, cheminRelatif);
                var copie = Path.Combine(sauvegarde, cheminRelatif);
                Directory.CreateDirectory(Path.GetDirectoryName(cible)!);
                string? ancien = null;
                if (File.Exists(cible))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(copie)!);
                    File.Copy(cible, copie, overwrite: true);
                    ancien = copie;
                }
                touches.Add((cible, ancien));
                File.Copy(Path.Combine(contenu, cheminRelatif), cible, overwrite: true);
            }
        }
        catch
        {
            foreach (var (cible, ancien) in touches.AsEnumerable().Reverse())
            {
                try
                {
                    if (ancien is null) File.Delete(cible);
                    else File.Copy(ancien, cible, overwrite: true);
                }
                catch (Exception ex) { Journal.Ecrire($"[maj] restauration échouée pour {cible} : {ex}"); }
            }
            throw;
        }
    }
}
