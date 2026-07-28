namespace Replayo.Buffer;

/// Anneau de segments encodés sur disque. Ne connaît ni la capture ni l'encodage :
/// il ne gère que des fichiers + leurs intervalles de temps (horloge de capture).
public sealed class SegmentRing(string dossierBuffer, int dureeMaxSecondes)
{
    private readonly record struct Entree(string Chemin, TimeSpan Debut, TimeSpan Fin);
    private readonly List<Entree> _entrees = new();
    private readonly object _verrou = new();
    private int _compteur;

    public void PurgerAuDemarrage()
    {
        Directory.CreateDirectory(dossierBuffer);
        var indexMaxSurvivant = 0;
        foreach (var f in Directory.GetFiles(dossierBuffer))
        {
            try { File.Delete(f); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Segment encore verrouillé (ex. ffmpeg résiduel d'une session précédente) :
                // on le laisse et on démarre le compteur au-delà pour éviter toute collision.
                var nom = Path.GetFileNameWithoutExtension(f);
                if (nom.StartsWith("seg_") && int.TryParse(nom.AsSpan(4), out var n))
                    indexMaxSurvivant = Math.Max(indexMaxSurvivant, n);
            }
        }
        lock (_verrou)
        {
            _entrees.Clear();
            _compteur = Math.Max(_compteur, indexMaxSurvivant);
        }
    }

    public string ProchainCheminSegment()
    {
        Directory.CreateDirectory(dossierBuffer);
        return Path.Combine(dossierBuffer, $"seg_{Interlocked.Increment(ref _compteur):D6}.mp4");
    }

    public void Ajouter(string chemin, TimeSpan debut, TimeSpan fin)
    {
        lock (_verrou)
        {
            _entrees.Add(new(chemin, debut, fin));
            // Supprime tout segment entièrement antérieur à la fenêtre maximale.
            // (La fenêtre d'un clip se termine à l'horloge courante, jamais avant la fin
            // du dernier segment : aucun segment sous cette limite ne peut être requis.)
            var limite = fin - TimeSpan.FromSeconds(dureeMaxSecondes);
            for (int i = _entrees.Count - 1; i >= 0; i--)
            {
                if (_entrees[i].Fin < limite)
                {
                    try { File.Delete(_entrees[i].Chemin); } catch { /* best effort */ }
                    _entrees.RemoveAt(i);
                }
            }
        }
    }

    public IReadOnlyList<string> SegmentsPourDuree(TimeSpan duree, TimeSpan maintenant)
    {
        var debutFenetre = maintenant - duree;
        lock (_verrou)
            return _entrees.Where(e => e.Fin > debutFenetre)
                           .OrderBy(e => e.Debut)
                           .Select(e => e.Chemin)
                           .ToList();
    }
}
