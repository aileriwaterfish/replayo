namespace Replayo.Lol;

/// Séquence de temps forts : fenêtre à clipper (horloge de capture) + score.
public sealed record SequenceLol(TimeSpan Debut, TimeSpan Fin, int Score, IReadOnlyList<TimeSpan> Evenements);

/// Fusionne les événements proches en séquences (spec : < fusion entre événements,
/// fenêtre avant/après, séquence à 0 pt jamais émise — la mort isolée disparaît).
/// Mise en scène proportionnelle (style IrelKing) : un gros play (score ≥ seuilMajeur)
/// s'ouvre avec avantMajeur de contexte — on voit la rotation et l'engagement.
public static class ConstructeurSequences
{
    public static List<SequenceLol> Construire(
        IEnumerable<(TimeSpan T, int Score)> evenements, TimeSpan fusion, TimeSpan avant, TimeSpan apres,
        TimeSpan? avantMajeur = null, int seuilMajeur = 50, TimeSpan? fusionContexte = null)
    {
        var tri = evenements.OrderBy(e => e.T).ToList();
        var sequences = new List<SequenceLol>();
        var groupe = new List<(TimeSpan T, int Score)>();

        void Emettre()
        {
            if (groupe.Count == 0) return;
            var score = groupe.Sum(g => g.Score);
            if (score > 0)
            {
                var ouverture = score >= seuilMajeur && avantMajeur is { } aM ? aM : avant;
                var debut = groupe[0].T - ouverture;
                if (debut < TimeSpan.Zero) debut = TimeSpan.Zero;
                sequences.Add(new(debut, groupe[^1].T + apres, score, groupe.Select(g => g.T).ToList()));
            }
            groupe.Clear();
        }

        foreach (var e in tri)
        {
            if (groupe.Count > 0)
            {
                // Un événement de contexte (score 0 : ma mort, kill/mort d'un allié)
                // ne se rattache à un play que de très près (fusionContexte) : le
                // teamfight se montre en entier, mais mourir ou voir un kill allié
                // 20 s après mon action est un temps mort qui n'a rien à faire dedans.
                var seuil = fusionContexte is { } fc && (e.Score == 0 || groupe[^1].Score == 0) ? fc : fusion;
                if (e.T - groupe[^1].T > seuil) Emettre();
            }
            groupe.Add(e);
        }
        Emettre();
        return sequences;
    }
}
