using Replayo.Audio;
using Replayo.Buffer;
using Replayo.Capture;
using Replayo.Clip;
using Replayo.Core;
using Replayo.Encoding;

namespace Replayo;

/// Orchestrateur : possède les pipelines (capture → encodeur → anneau) et l'audio.
/// Démarrable/arrêtable/redémarrable à chaud (changement de réglages), surveille
/// l'espace disque (< 2 Go → pause + notification).
public sealed class RecorderService : IDisposable
{
    private sealed record Pipeline(CaptureEngine Capture, SegmentEncoder Enc, SegmentRing Ring, string? Suffixe);

    private readonly List<Pipeline> _pipelines = new();
    private AudioEngine? _audio;
    private CancellationTokenSource? _cts;
    private System.Threading.Timer? _surveillanceDisque;
    private ReplayoConfig _cfg = new();

    public bool EnCapture { get; private set; }
    public bool EncodageMateriel => _pipelines.Count == 0 || _pipelines.All(p => p.Enc.EncodageMateriel);
    public event Action<string>? Notification;

    public void Demarrer(ReplayoConfig cfg)
    {
        if (EnCapture) return;
        _cfg = cfg;
        var preset = QualityPreset.DepuisNom(cfg.Preset);
        var ecrans = MonitorInfo.EnumererEcrans();
        var sources = cfg.SourcesEcrans.Count == 0
            ? ecrans.Where(e => e.Principal).ToList()
            : ecrans.Where(e => cfg.SourcesEcrans.Contains(e.Index)).ToList();
        if (sources.Count == 0) { Notification?.Invoke("Aucun écran source disponible."); return; }

        _cts = new CancellationTokenSource();
        _audio = AudioEngine.CreerSiActive(cfg);
        _audio?.Demarrer();

        foreach (var (ecran, i) in sources.Select((e, i) => (e, i)))
        {
            var ring = new SegmentRing(Path.Combine(AppPaths.DossierBuffer, $"ecran{ecran.Index}"), cfg.DureeBufferSecondes);
            ring.PurgerAuDemarrage();
            var capture = new CaptureEngine(ecran);
            capture.CaptureInterrompue += () => Notification?.Invoke($"Écran {ecran.Index + 1} interrompu — capture arrêtée pour cet écran.");
            capture.Demarrer();
            var enc = new SegmentEncoder(capture.Frames, i == 0 ? _audio : null, ring, preset, capture.Taille);
            _ = enc.BoucleEncodageAsync(_cts.Token);
            _pipelines.Add(new(capture, enc, ring, sources.Count > 1 ? $"ecran{ecran.Index + 1}" : null));
        }

        // Surveillance disque : < 2 Go libres sur le volume du buffer → pause.
        _surveillanceDisque = new System.Threading.Timer(_ =>
        {
            try
            {
                var libre = new DriveInfo(Path.GetPathRoot(AppPaths.DossierBuffer)!).AvailableFreeSpace;
                if (libre < 2L * 1024 * 1024 * 1024)
                {
                    Arreter();
                    Notification?.Invoke("Disque presque plein (< 2 Go) : capture mise en pause.");
                }
            }
            catch { /* volume indisponible : ignoré */ }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        EnCapture = true;
        _ = Task.Delay(3000).ContinueWith(_ =>
        {
            if (EnCapture && !EncodageMateriel)
                Notification?.Invoke("Encodeur matériel indisponible : repli logiciel (CPU accru).");
        });
    }

    public void Arreter()
    {
        if (!EnCapture) return;
        EnCapture = false;
        _surveillanceDisque?.Dispose(); _surveillanceDisque = null;
        _cts?.Cancel();
        foreach (var p in _pipelines) p.Capture.Arreter();
        _pipelines.Clear();
        _audio?.Dispose(); _audio = null;
    }

    public void Redemarrer(ReplayoConfig cfg) { Arreter(); Demarrer(cfg); }

    /// Horloge de capture du pipeline principal (null si capture arrêtée).
    public TimeSpan? HorlogeCapture => _pipelines.Count > 0 ? _pipelines[0].Enc.HorlogeCapture : null;

    /// Fenêtre de rétention de tous les anneaux (mode LoL : étendue à 120 s en game).
    public void FenetreBuffer(int secondes) { foreach (var p in _pipelines) p.Ring.DureeMaxSecondes = secondes; }

    /// Clip d'un intervalle de l'horloge de capture, sans ré-encodage, vers un chemin
    /// imposé. Rend aussi le début réel du fichier (frontière de segment ≤ debut).
    public async Task<(string Chemin, TimeSpan DebutReel)?> ClipperIntervalleAsync(TimeSpan debut, TimeSpan fin, string sortie)
    {
        if (!EnCapture || _pipelines.Count == 0) return null;
        var (segments, debutPremier) = _pipelines[0].Ring.IntervalleAvecDebut(debut, fin);
        if (segments.Count == 0) return null;
        return await ClipService.AssemblerAsync(segments, sortie) ? (sortie, debutPremier) : null;
    }

    public async Task<List<string>> ClipperAsync()
    {
        var resultats = new List<string>();
        if (!EnCapture) return resultats;
        var clips = new ClipService(_cfg);
        foreach (var p in _pipelines)
        {
            var chemin = await clips.CreerClipAsync(p.Ring, p.Enc.HorlogeCapture, p.Suffixe);
            if (chemin is not null) resultats.Add(chemin);
        }
        return resultats;
    }

    public void Dispose() => Arreter();
}
