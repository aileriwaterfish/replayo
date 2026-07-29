namespace Replayo.Lol;

/// Barème des temps forts (spec Mode LoL). Pur, sans état.
public static class ScoreurEvenements
{
    public const int NonRetenu = -1;

    /// activeplayername renvoie « Nom#TAG » mais les événements (KillerName…)
    /// utilisent selon les patchs le nom avec ou sans tag : on accepte les deux.
    public static bool MemeJoueur(string? nom, string moi)
        => nom is not null && (nom == moi || AvantTag(nom) == AvantTag(moi));

    private static string AvantTag(string nom)
    {
        var i = nom.IndexOf('#');
        return i < 0 ? nom : nom[..i];
    }

    public static int Score(EvenementLol e, string moi) => e.Type switch
    {
        "ChampionKill" when MemeJoueur(e.Tueur, moi) => 25,
        "ChampionKill" when MemeJoueur(e.Victime, moi) => 0, // mort : fusable, ne rapporte rien
        "Multikill" when MemeJoueur(e.Tueur, moi) => e.Serie switch { 2 => 15, 3 => 35, 4 => 55, >= 5 => 75, _ => NonRetenu },
        "FirstBlood" when MemeJoueur(e.Beneficiaire, moi) => 15,
        "DragonKill" when MemeJoueur(e.Tueur, moi) && e.Vole => 70,
        "BaronKill" when MemeJoueur(e.Tueur, moi) && e.Vole => 80,
        "GameEnd" when e.Resultat == "Win" => 30,
        _ => NonRetenu,
    };
}
