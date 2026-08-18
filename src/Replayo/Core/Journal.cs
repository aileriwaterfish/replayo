namespace Replayo.Core;

/// Journal de diagnostic sur fichier. Indispensable : le projet est `WinExe`, donc
/// tout `Console.Error.WriteLine` part dans le vide — un buffer gelé pendant 5 h
/// (incident des nuits du 16 et du 17/08/2026) n'a laissé AUCUNE trace.
/// Écriture synchrone sous verrou : les volumes sont minuscules (quelques lignes
/// par heure) et on veut la ligne sur le disque même si le process meurt juste après.
public static class Journal
{
    private const long TailleMaxOctets = 2L * 1024 * 1024;
    private static readonly object Verrou = new();

    /// Redirection réservée aux tests (le vrai journal vit sous %LocalAppData%).
    internal static string? CheminForce;

    public static string Chemin => CheminForce ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Replayo", "replayo.log");

    /// Ajoute une ligne horodatée. N'échoue jamais : un journal cassé ne doit pas
    /// tuer la capture. La sortie console est conservée en plus, pour les runs CLI.
    public static void Ecrire(string message)
    {
        Console.Error.WriteLine(message);
        try
        {
            lock (Verrou)
            {
                var chemin = Chemin;
                Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
                Roter(chemin);
                File.AppendAllText(chemin, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch { /* disque plein, fichier verrouillé : on abandonne cette ligne */ }
    }

    /// Une seule génération conservée (`.1`) : on veut la trace de l'incident, pas un historique.
    private static void Roter(string chemin)
    {
        var info = new FileInfo(chemin);
        if (!info.Exists || info.Length < TailleMaxOctets) return;
        var precedent = chemin + ".1";
        File.Delete(precedent);
        File.Move(chemin, precedent);
    }
}
