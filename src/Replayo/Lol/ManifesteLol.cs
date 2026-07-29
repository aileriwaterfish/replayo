using System.Text.Json;

namespace Replayo.Lol;

/// Une séquence clippée, décrite pour le worker de montage (temps en secondes
/// d'horloge de capture ; Fichier relatif au dossier de la game ; FichierDebutSec =
/// début réel du fichier, frontière de segment ≤ DebutSec, pour la coupe précise).
public sealed record SequenceManifeste(string Fichier, int Score, double DebutSec, double FinSec, double FichierDebutSec, double[] EvenementsSec,
    bool EstResultat = false); // clip du résultat de la game : toujours en clôture, jamais éjecté par le tri

/// Contrat écrit par Replayo en fin de game et consommé par Replayo.Montage.
/// Ne pas renommer les propriétés : c'est un format de fichier.
public sealed record ManifesteLol(
    int Version, DateTime Date, int Queue, bool Victoire, bool Retenue,
    int DureeCibleMinSec, int DureeCibleMaxSec, int SeuilMinSec,
    List<SequenceManifeste> Sequences,
    string? AudioLolFichier = null,   // v2 : piste « jeu seul » (audio_lol.m4a), null si indisponible
    double? AudioLolDebutSec = null)  // v2 : horloge de capture au début de cette piste
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Ecrire(string chemin)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
        File.WriteAllText(chemin, JsonSerializer.Serialize(this, Options));
    }

    public static ManifesteLol? Lire(string chemin)
    {
        try { return JsonSerializer.Deserialize<ManifesteLol>(File.ReadAllText(chemin)); }
        catch { return null; }
    }
}
