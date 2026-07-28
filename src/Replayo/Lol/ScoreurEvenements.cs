namespace Replayo.Lol;

/// Barème des temps forts (spec Mode LoL). Pur, sans état.
public static class ScoreurEvenements
{
    public const int NonRetenu = -1;

    public static int Score(EvenementLol e, string moi) => e.Type switch
    {
        "ChampionKill" when e.Tueur == moi => 25,
        "ChampionKill" when e.Victime == moi => 0, // mort : fusable, ne rapporte rien
        "Multikill" when e.Tueur == moi => e.Serie switch { 2 => 15, 3 => 35, 4 => 55, >= 5 => 75, _ => NonRetenu },
        "FirstBlood" when e.Beneficiaire == moi => 15,
        "DragonKill" when e.Tueur == moi && e.Vole => 70,
        "BaronKill" when e.Tueur == moi && e.Vole => 80,
        "GameEnd" when e.Resultat == "Win" => 30,
        _ => NonRetenu,
    };
}
