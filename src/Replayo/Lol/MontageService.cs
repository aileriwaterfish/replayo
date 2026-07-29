using System.Diagnostics;
using System.Globalization;
using Replayo.Core;

namespace Replayo.Lol;

/// Un plan de la timeline du condensé : départ et durée DANS le fichier source,
/// plus le départ en horloge de capture (pour aligner la piste audio isolée).
public sealed record PlanDeCoupe(string Fichier, double DepartSec, double DureeSec, double DebutCaptureSec = 0);

/// Worker de montage : manifest → condensé vertical 1080×1920 (cold open +
/// chronologique, crop central, fondus audio 0,2 s, aucun texte, aucune musique).
/// Lancé par `Replayo.exe --montage <dossierGame>` — aucun UI, sortie console.
public static class MontageService
{
    private const double FonduAudioSec = 0.2;
    private const double FonduVideoSec = 0.25; // micro fondu au noir entre séquences

    /// Ordre chronologique conservé ; retire la séquence au score le plus faible
    /// (à égalité : la plus longue) tant que la durée totale dépasse maxSec.
    /// Le clip du résultat de la game n'est JAMAIS éjecté et reste en clôture
    /// (son temps est déduit du budget des autres séquences).
    public static List<SequenceManifeste> Selectionner(List<SequenceManifeste> seqs, int maxSec)
    {
        var resultat = seqs.Where(s => s.EstResultat).OrderBy(s => s.DebutSec).ToList();
        var retenues = seqs.Where(s => !s.EstResultat).OrderBy(s => s.DebutSec).ToList();
        var budget = maxSec - resultat.Sum(s => s.FinSec - s.DebutSec);
        while (retenues.Count > 1 && retenues.Sum(s => s.FinSec - s.DebutSec) > budget)
        {
            var victime = retenues.OrderBy(s => s.Score).ThenByDescending(s => s.FinSec - s.DebutSec).First();
            retenues.Remove(victime);
        }
        retenues.AddRange(resultat);
        return retenues;
    }

    /// Timeline purement chronologique (cold open retiré à la demande de
    /// l'utilisateur le 30/07) ; le résultat de la game arrive naturellement en dernier.
    public static List<PlanDeCoupe> Timeline(ManifesteLol m)
        => Selectionner(m.Sequences, m.DureeCibleMaxSec)
            .Select(s => new PlanDeCoupe(s.Fichier, s.DebutSec - s.FichierDebutSec, s.FinSec - s.DebutSec, s.DebutSec))
            .ToList();

    /// Commande ffmpeg complète : -ss/-t par entrée, crop central 9:16 + scale,
    /// fondus audio en entrée/sortie de chaque plan, concat, H.264 + AAC.
    /// Si audioLol est fourni (piste « jeu seul », plan C), l'audio de chaque plan
    /// est pris dans cette piste (aligné par horloge de capture) au lieu du mix des clips.
    public static string ArgumentsFfmpeg(List<PlanDeCoupe> plans, string dossier, string sortie,
        string? audioLol = null, double audioLolDebutSec = 0)
    {
        var inv = CultureInfo.InvariantCulture;
        var n = plans.Count;
        var entrees = string.Join(" ", plans.Select(p =>
            string.Create(inv, $"-ss {p.DepartSec:F3} -t {p.DureeSec:F3} -i \"{Path.Combine(dossier, p.Fichier)}\"")));

        if (audioLol is not null)
            entrees += " " + string.Join(" ", plans.Select(p =>
                string.Create(inv, $"-ss {Math.Max(0, p.DebutCaptureSec - audioLolDebutSec):F3} -t {p.DureeSec:F3} -i \"{Path.Combine(dossier, audioLol)}\"")));

        // L'audio du plan i vient de l'entrée i (mix du clip) ou n+i (piste jeu seul).
        int EntreeAudio(int i) => audioLol is null ? i : n + i;

        // Rendu « zoom réduit » : carré central 1080×1080 (~56 % de la largeur — le
        // champion sort rarement du cadre en caméra libre) affiché pleine largeur,
        // sur un fond du même gameplay flouté qui remplit le 9:16.
        // fps=60 + settb : normalise les entrées à cadence irrégulière (capture VFR)
        // avant le concat, sinon les timestamps se cassent et la vidéo saccade.
        // Micro fondu au noir (0,25 s) en entrée/sortie de chaque plan : respiration
        // entre les séquences (feedback : transitions trop soudaines).
        var filtres = string.Join("", plans.Select((p, i) =>
            string.Create(inv,
                $"[{i}:v]fps=60,setsar=1,split=2[bg{i}][fg{i}];" +
                $"[bg{i}]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=luma_radius=25:luma_power=2[b{i}];" +
                $"[fg{i}]crop=1080:1080:420:0[f{i}];" +
                $"[b{i}][f{i}]overlay=0:420,fade=t=in:d={FonduVideoSec:F2},fade=t=out:st={Math.Max(0, p.DureeSec - FonduVideoSec):F3}:d={FonduVideoSec:F2},settb=AVTB[v{i}];" +
                $"[{EntreeAudio(i)}:a]aresample=48000:async=1,afade=t=in:d={FonduAudioSec:F1},afade=t=out:st={Math.Max(0, p.DureeSec - FonduAudioSec):F3}:d={FonduAudioSec:F1}[a{i}];")));
        var concat = string.Join("", plans.Select((_, i) => $"[v{i}][a{i}]")) +
                     $"concat=n={n}:v=1:a=1[v][a]";

        return $"-hide_banner -loglevel error {entrees} -filter_complex \"{filtres}{concat}\" " +
               $"-map \"[v]\" -map \"[a]\" -r 60 -c:v libx264 -preset veryfast -crf 21 -pix_fmt yuv420p " +
               $"-c:a aac -b:a 160k -movflags +faststart -y \"{sortie}\"";
    }

    /// Point d'entrée du worker : lit le manifest, monte, copie vers le dossier
    /// « TikTok en attente ». Code retour 0 = OK ou game non retenue.
    public static async Task<int> ExecuterAsync(string dossierGame)
    {
        var manifeste = ManifesteLol.Lire(Path.Combine(dossierGame, "manifest.json"));
        if (manifeste is null) { Console.Error.WriteLine("[montage] manifest illisible"); return 1; }
        if (!manifeste.Retenue || manifeste.Sequences.Count == 0)
        { Console.WriteLine("[montage] game non retenue : rien à monter"); return 0; }

        var plans = Timeline(manifeste);
        var sortie = Path.Combine(dossierGame, "condense_vertical.mp4");

        // Piste « jeu seul » (plan C) si présente ; sinon mix complet + avertissement.
        var audioLol = manifeste.AudioLolFichier is { } a && File.Exists(Path.Combine(dossierGame, a)) ? a : null;
        if (audioLol is null)
            Console.Error.WriteLine("[montage] pas de piste audio isolée : le condensé contient le MIX COMPLET (musique/vocal inclus).");

        var psi = new ProcessStartInfo(AppPaths.FfmpegExe,
            ArgumentsFfmpeg(plans, dossierGame, sortie, audioLol, manifeste.AudioLolDebutSec ?? 0))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };

        using var proc = Process.Start(psi)!;
        var erreurs = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0) { Console.Error.WriteLine($"[montage] ffmpeg : {erreurs}"); return 2; }

        // Livraison manuelle (décision du 29/07 : pas d'API TikTok) : copie dans
        // Vidéos\Replayo\TikTok en attente, et dans OneDrive s'il existe pour que
        // le condensé arrive tout seul sur l'iPhone (app OneDrive → Photos → TikTok).
        var nom = $"Replayo_LoL_{manifeste.Date:yyyy-MM-dd_HH\\hmm}.mp4";
        var attente = Path.Combine(AppPaths.DossierSortieDefaut, "TikTok en attente");
        Directory.CreateDirectory(attente);
        File.Copy(sortie, Path.Combine(attente, nom), overwrite: true);

        if (Environment.GetEnvironmentVariable("OneDrive") is { Length: > 0 } oneDrive && Directory.Exists(oneDrive))
        {
            var dossierTel = Path.Combine(oneDrive, "Replayo TikTok");
            Directory.CreateDirectory(dossierTel);
            File.Copy(sortie, Path.Combine(dossierTel, nom), overwrite: true);
        }

        Console.WriteLine($"[montage] condensé prêt : {Path.Combine(attente, nom)}");
        return 0;
    }
}
