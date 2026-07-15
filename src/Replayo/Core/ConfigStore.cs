using System.Text.Json;

namespace Replayo.Core;

/// Lecture/écriture de la config. Les valeurs aberrantes sont bornées au chargement.
public sealed class ConfigStore(string? dossier = null)
{
    private readonly string _chemin = Path.Combine(dossier ?? AppPaths.DossierConfig, "config.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public ReplayoConfig Charger()
    {
        ReplayoConfig cfg = new();
        if (File.Exists(_chemin))
        {
            try { cfg = JsonSerializer.Deserialize<ReplayoConfig>(File.ReadAllText(_chemin)) ?? new(); }
            catch { cfg = new(); } // fichier corrompu → défauts, jamais de crash
        }
        cfg.DureeBufferSecondes = Math.Clamp(cfg.DureeBufferSecondes, 15, 1200);
        if (cfg.FormatSortie is not ("mp4" or "mkv")) cfg.FormatSortie = "mp4";
        return cfg;
    }

    public void Enregistrer(ReplayoConfig cfg)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_chemin)!);
        File.WriteAllText(_chemin, JsonSerializer.Serialize(cfg, Options));
    }
}
