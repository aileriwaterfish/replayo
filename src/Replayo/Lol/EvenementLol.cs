namespace Replayo.Lol;

/// Événement de l'API Live Client, aplati (seuls les champs utiles au scoring).
public sealed record EvenementLol(
    int Id, string Type, double TempsJeuSec,
    string? Tueur = null, string? Victime = null, string? Beneficiaire = null,
    int Serie = 0, bool Vole = false, string? Resultat = null);
