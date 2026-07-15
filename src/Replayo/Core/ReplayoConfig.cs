namespace Replayo.Core;

/// Réglages utilisateur persistés en JSON (%AppData%\Replayo\config.json).
public sealed class ReplayoConfig
{
    public int DureeBufferSecondes { get; set; } = 300;
    public string Preset { get; set; } = "equilibre";
    public string FormatSortie { get; set; } = "mp4"; // "mp4" | "mkv"
    public List<int> SourcesEcrans { get; set; } = new(); // indices d'écrans ; vide = principal
    public bool AudioSysteme { get; set; } = true;
    public bool AudioMicro { get; set; } = false;
    public bool NommageManuel { get; set; } = false;
    public string DossierSortie { get; set; } = ""; // vide = Vidéos\Replayo
}
