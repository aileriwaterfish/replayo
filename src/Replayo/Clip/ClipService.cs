using System.Diagnostics;
using Replayo.Buffer;
using Replayo.Core;

namespace Replayo.Clip;

/// Assemble les N dernières secondes en un fichier final SANS ré-encodage (ffmpeg -c copy)
/// et range le fichier (Appli/AAAA-MM). Pas de son : le toast sert de confirmation.
public sealed class ClipService(ReplayoConfig cfg)
{
    public static string ConstruireCheminSortie(string racine, string app, DateTime quand, string format, string? suffixe)
    {
        var appPropre = string.Join("", app.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (appPropre.Length == 0) appPropre = "Bureau";
        var nom = $"Replayo_{appPropre}_{quand:yyyy-MM-dd_HH\\hmm\\mss}{(suffixe is null ? "" : "_" + suffixe)}.{format}";
        return Path.Combine(racine, appPropre, $"{quand:yyyy-MM}", nom);
    }

    public async Task<string?> CreerClipAsync(SegmentRing ring, TimeSpan horloge, string? suffixe = null, string? audioMix = null)
    {
        var fenetre = TimeSpan.FromSeconds(cfg.DureeBufferSecondes);
        var debutFenetre = horloge - fenetre; if (debutFenetre < TimeSpan.Zero) debutFenetre = TimeSpan.Zero;
        using var location = ring.LouerIntervalle(debutFenetre, horloge);
        if (location.Segments.Count == 0) return null;

        var racine = string.IsNullOrWhiteSpace(cfg.DossierSortie) ? AppPaths.DossierSortieDefaut : cfg.DossierSortie;
        var sortie = CheminDisponible(ConstruireCheminSortie(
            racine, ForegroundAppTracker.NomApplication(), DateTime.Now, cfg.FormatSortie, suffixe));

        return await AssemblerAsync(location.Segments, sortie, audioMix, location.DebutPremier.TotalSeconds) ? sortie : null;
    }

    internal static string CheminDisponible(string chemin)
    {
        if (!File.Exists(chemin)) return chemin;
        var dossier = Path.GetDirectoryName(chemin)!;
        var nom = Path.GetFileNameWithoutExtension(chemin);
        var extension = Path.GetExtension(chemin);
        for (var i = 2; ; i++)
        {
            var candidat = Path.Combine(dossier, $"{nom}_{i}{extension}");
            if (!File.Exists(candidat)) return candidat;
        }
    }

    /// Assemble des segments (vidéo seule) en un fichier final SANS ré-encodage
    /// (démuxeur concat + -c copy). Si audioMix est fourni (piste AAC/ADTS continue
    /// de la session), l'audio y est découpé par horloge de capture et muxé tel quel.
    public static async Task<bool> AssemblerAsync(IReadOnlyList<string> segments, string sortie,
        string? audioMix = null, double departAudioSec = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(sortie)!);

        // Liste concat ffmpeg (chemins entre apostrophes).
        var liste = Path.Combine(Path.GetTempPath(), $"replayo_concat_{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(liste, segments.Select(s => $"file '{s.Replace("'", "'\\''")}'"));

        var argsAudio = audioMix is null || !File.Exists(audioMix)
            ? ""
            : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"-ss {departAudioSec:F3} -i \"{audioMix}\" -map 0:v -map 1:a -bsf:a aac_adtstoasc -shortest ");
        var psi = new ProcessStartInfo(AppPaths.FfmpegExe,
            $"-hide_banner -loglevel error -f concat -safe 0 -i \"{liste}\" {argsAudio}-c copy -y \"{sortie}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };

        using var proc = Process.Start(psi)!;
        var erreurs = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        File.Delete(liste);

        if (proc.ExitCode != 0)
        {
            Journal.Ecrire($"[clip] ffmpeg : {erreurs}");
            try { File.Delete(sortie); } catch { /* sortie partielle déjà absente ou verrouillée */ }
            return false;
        }
        return true;
    }
}
