namespace Replayo.Lol;

/// Séquence de temps forts : fenêtre à clipper (horloge de capture) + score.
public sealed record SequenceLol(TimeSpan Debut, TimeSpan Fin, int Score, IReadOnlyList<TimeSpan> Evenements);

/// Fusionne les événements proches en séquences (spec : < fusion entre événements,
/// fenêtre avant/après, séquence à 0 pt jamais émise — la mort isolée disparaît).
public static class ConstructeurSequences
{
    public static List<SequenceLol> Construire(
        IEnumerable<(TimeSpan T, int Score)> evenements, TimeSpan fusion, TimeSpan avant, TimeSpan apres)
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
                var debut = groupe[0].T - avant;
                if (debut < TimeSpan.Zero) debut = TimeSpan.Zero;
                sequences.Add(new(debut, groupe[^1].T + apres, score, groupe.Select(g => g.T).ToList()));
            }
            groupe.Clear();
        }

        foreach (var e in tri)
        {
            if (groupe.Count > 0 && e.T - groupe[^1].T > fusion) Emettre();
            groupe.Add(e);
        }
        Emettre();
        return sequences;
    }
}
