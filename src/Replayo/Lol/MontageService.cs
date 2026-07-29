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
    private const double TeaserSec = 3.5;
    private const double MargeResolutionSec = 0.5;
    private const double FonduAudioSec = 0.2;

    /// Ordre chronologique conservé ; retire la séquence au score le plus faible
    /// (à égalité : la plus longue) tant que la durée totale dépasse maxSec.
    public static List<SequenceManifeste> Selectionner(List<SequenceManifeste> seqs, int maxSec)
    {
        var retenues = seqs.OrderBy(s => s.DebutSec).ToList();
        while (retenues.Count > 1 && retenues.Sum(s => s.FinSec - s.DebutSec) > maxSec)
        {
            var victime = retenues.OrderBy(s => s.Score).ThenByDescending(s => s.FinSec - s.DebutSec).First();
            retenues.Remove(victime);
        }
        return retenues;
    }

    /// Teaser : ~3,5 s de la meilleure séquence, coupé ~0,5 s avant son dernier
    /// événement (la résolution reste à découvrir dans le corps de la vidéo).
    public static PlanDeCoupe ColdOpen(SequenceManifeste meilleure)
    {
        var dernierEvt = meilleure.EvenementsSec.Length > 0 ? meilleure.EvenementsSec.Max() : meilleure.FinSec;
        var finTeaser = dernierEvt - MargeResolutionSec;
        var depart = Math.Max(meilleure.FichierDebutSec, finTeaser - TeaserSec);
        return new(meilleure.Fichier, depart - meilleure.FichierDebutSec, Math.Max(0.5, finTeaser - depart), depart);
    }

    /// Cold open (si ≥ 2 séquences) puis toutes les séquences en ordre chronologique.
    public static List<PlanDeCoupe> Timeline(ManifesteLol m)
    {
        var retenues = Selectionner(m.Sequences, m.DureeCibleMaxSec);
        var plans = new List<PlanDeCoupe>();
        if (retenues.Count >= 2)
            plans.Add(ColdOpen(retenues.OrderByDescending(s => s.Score).First()));
        plans.AddRange(retenues.Select(s =>
            new PlanDeCoupe(s.Fichier, s.DebutSec - s.FichierDebutSec, s.FinSec - s.DebutSec, s.DebutSec)));
        return plans;
    }

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

        var filtres = string.Join("", plans.Select((p, i) =>
            string.Create(inv,
                $"[{i}:v]crop=608:1080:656:0,scale=1080:1920,setsar=1[v{i}];" +
                $"[{EntreeAudio(i)}:a]afade=t=in:d={FonduAudioSec:F1},afade=t=out:st={Math.Max(0, p.DureeSec - FonduAudioSec):F3}:d={FonduAudioSec:F1}[a{i}];")));
        var concat = string.Join("", plans.Select((_, i) => $"[v{i}][a{i}]")) +
                     $"concat=n={n}:v=1:a=1[v][a]";

        return $"-hide_banner -loglevel error {entrees} -filter_complex \"{filtres}{concat}\" " +
               $"-map \"[v]\" -map \"[a]\" -c:v libx264 -preset veryfast -crf 21 -pix_fmt yuv420p " +
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
