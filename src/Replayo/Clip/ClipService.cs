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

    public async Task<string?> CreerClipAsync(SegmentRing ring, TimeSpan horloge, string? suffixe = null)
    {
        var segments = ring.SegmentsPourDuree(TimeSpan.FromSeconds(cfg.DureeBufferSecondes), horloge);
        if (segments.Count == 0) return null;

        var racine = string.IsNullOrWhiteSpace(cfg.DossierSortie) ? AppPaths.DossierSortieDefaut : cfg.DossierSortie;
        var sortie = ConstruireCheminSortie(racine, ForegroundAppTracker.NomApplication(), DateTime.Now, cfg.FormatSortie, suffixe);
        Directory.CreateDirectory(Path.GetDirectoryName(sortie)!);

        // Liste concat ffmpeg (démuxeur concat : chemins entre apostrophes).
        var liste = Path.Combine(Path.GetTempPath(), $"replayo_concat_{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(liste, segments.Select(s => $"file '{s.Replace("'", "'\\''")}'"));

        var psi = new ProcessStartInfo(AppPaths.FfmpegExe,
            $"-hide_banner -loglevel error -f concat -safe 0 -i \"{liste}\" -c copy -y \"{sortie}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };

        using var proc = Process.Start(psi)!;
        var erreurs = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        File.Delete(liste);

        if (proc.ExitCode != 0) { Console.Error.WriteLine($"[clip] ffmpeg : {erreurs}"); return null; }

        return sortie; // pas de son : le toast suffit comme confirmation
    }
}
