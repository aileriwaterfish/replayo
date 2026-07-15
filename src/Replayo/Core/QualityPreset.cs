namespace Replayo.Core;

/// Préréglage de qualité. Largeur/Hauteur null = résolution native de l'écran.
public sealed record QualityPreset(int? Largeur, int? Hauteur, int Fps, uint DebitBitsParSeconde)
{
    public static QualityPreset DepuisNom(string nom) => nom switch
    {
        "eco" => new(1920, 1080, 30, 8_000_000),
        "qualite" => new(null, null, 60, 40_000_000),
        _ => new(null, null, 60, 20_000_000), // "equilibre" = défaut
    };
}
