namespace Replayo.Buffer;

/// Anneau de segments encodés sur disque. Ne connaît ni la capture ni l'encodage :
/// il ne gère que des fichiers + leurs intervalles de temps (horloge de capture).
public sealed class SegmentRing(string dossierBuffer, int dureeMaxSecondes)
{
    private readonly record struct Entree(string Chemin, TimeSpan Debut, TimeSpan Fin);
    private readonly List<Entree> _entrees = new();
    private readonly object _verrou = new();
    private readonly Dictionary<string, int> _locations = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _suppressionsDifferees = new(StringComparer.OrdinalIgnoreCase);
    private int _compteur;
    private TimeSpan? _debutProtege;
    private TimeSpan _derniereFin;

    /// Fige la liste des segments pendant qu'un ffmpeg les lit. Sans cette location,
    /// l'anneau pouvait supprimer le premier fichier entre la sélection et l'ouverture
    /// par ffmpeg, ce qui produisait aléatoirement « No such file or directory ».
    public sealed class LocationSegments : IDisposable
    {
        private SegmentRing? _proprietaire;

        internal LocationSegments(SegmentRing proprietaire, IReadOnlyList<string> segments, TimeSpan debutPremier)
        {
            _proprietaire = proprietaire;
            Segments = segments;
            DebutPremier = debutPremier;
        }

        public IReadOnlyList<string> Segments { get; }
        public TimeSpan DebutPremier { get; }

        public void Dispose()
        {
            var proprietaire = Interlocked.Exchange(ref _proprietaire, null);
            proprietaire?.Liberer(Segments);
        }
    }

    /// Fenêtre de rétention, modifiable à chaud (mode LoL : 120 s le temps d'une game).
    public int DureeMaxSecondes { get; set; } = dureeMaxSecondes;

    public void PurgerAuDemarrage()
    {
        lock (_verrou)
        {
            Directory.CreateDirectory(dossierBuffer);
            var indexMaxSurvivant = 0;
            foreach (var f in Directory.GetFiles(dossierBuffer))
            {
                if (_locations.ContainsKey(f)) _suppressionsDifferees.Add(f);
                else
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
            }
            _entrees.Clear();
            _debutProtege = null;
            _derniereFin = TimeSpan.Zero;
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
            _derniereFin = fin;
            PurgerAnciens(fin);
        }
    }

    /// Conserve les segments nécessaires à un enregistrement manuel, même si le
    /// mode LoL modifie entre-temps la durée normale du buffer.
    public void ProtegerDepuis(TimeSpan debut)
    {
        lock (_verrou) _debutProtege = debut;
    }

    public void NePlusProteger()
    {
        lock (_verrou)
        {
            _debutProtege = null;
            PurgerAnciens(_derniereFin);
        }
    }

    private void PurgerAnciens(TimeSpan fin)
    {
        // Supprime tout segment entièrement antérieur à la fenêtre maximale.
        // (La fenêtre d'un clip se termine à l'horloge courante, jamais avant la fin
        // du dernier segment : aucun segment sous cette limite ne peut être requis.)
        var limite = fin - TimeSpan.FromSeconds(DureeMaxSecondes);
        if (_debutProtege is { } debut) limite = TimeSpan.FromTicks(Math.Min(limite.Ticks, debut.Ticks));
        for (int i = _entrees.Count - 1; i >= 0; i--)
        {
            if (_entrees[i].Fin < limite)
            {
                SupprimerOuDifferer(_entrees[i].Chemin);
                _entrees.RemoveAt(i);
            }
        }
    }

    public IReadOnlyList<string> SegmentsPourIntervalle(TimeSpan debut, TimeSpan fin)
        => IntervalleAvecDebut(debut, fin).Segments;

    /// Segments chevauchant [debut, fin] + début réel du premier (la coupe précise
    /// du montage a besoin de savoir où le fichier assemblé commence vraiment).
    public (IReadOnlyList<string> Segments, TimeSpan DebutPremier) IntervalleAvecDebut(TimeSpan debut, TimeSpan fin)
    {
        lock (_verrou)
        {
            var retenus = _entrees.Where(e => e.Fin > debut && e.Debut < fin)
                                  .OrderBy(e => e.Debut)
                                  .ToList();
            return (retenus.Select(e => e.Chemin).ToList(),
                    retenus.Count > 0 ? retenus[0].Debut : TimeSpan.Zero);
        }
    }

    public LocationSegments LouerIntervalle(TimeSpan debut, TimeSpan fin)
    {
        lock (_verrou)
        {
            var retenus = _entrees.Where(e => e.Fin > debut && e.Debut < fin)
                                  .OrderBy(e => e.Debut)
                                  .ToList();
            var chemins = retenus.Select(e => e.Chemin).ToList();
            foreach (var chemin in chemins)
                _locations[chemin] = _locations.GetValueOrDefault(chemin) + 1;
            return new LocationSegments(this, chemins,
                retenus.Count > 0 ? retenus[0].Debut : TimeSpan.Zero);
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

    private void SupprimerOuDifferer(string chemin)
    {
        if (_locations.ContainsKey(chemin)) _suppressionsDifferees.Add(chemin);
        else try { File.Delete(chemin); } catch { /* best effort */ }
    }

    private void Liberer(IReadOnlyList<string> chemins)
    {
        lock (_verrou)
        {
            foreach (var chemin in chemins)
            {
                if (!_locations.TryGetValue(chemin, out var compte)) continue;
                if (compte > 1) { _locations[chemin] = compte - 1; continue; }
                _locations.Remove(chemin);
                if (_suppressionsDifferees.Remove(chemin))
                    try { File.Delete(chemin); } catch { /* best effort */ }
            }
        }
    }
}
