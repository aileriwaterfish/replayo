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
    // Référence directe au pipeline principal : les horloges sont lues depuis le timer
    // de surveillance et depuis le mode LoL, pendant qu'Arreter() peut vider la liste.
    private volatile Pipeline? _principal;
    private AudioEngine? _audio;
    private AudioMixRecorder? _audioMix;
    private CancellationTokenSource? _cts;
    private System.Threading.Timer? _surveillanceDisque;
    private System.Threading.Timer? _surveillanceCapture;
    private readonly SurveillanceCapture _chienDeGarde = new();
    private int _redemarrageEnCours;
    private bool _reactionsSystemeBranchees;
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
        // Mix audio encodé en continu à côté du buffer (les segments sont vidéo seule) ;
        // l'audio est remis au moment du clip, découpé par horloge de capture.
        if (_audio is not null)
            _audioMix = AudioMixRecorder.Demarrer(_audio, Path.Combine(AppPaths.DossierBuffer, "audio_mix.aac"));

        foreach (var (ecran, i) in sources.Select((e, i) => (e, i)))
        {
            var ring = new SegmentRing(Path.Combine(AppPaths.DossierBuffer, $"ecran{ecran.Index}"), cfg.DureeBufferSecondes);
            ring.PurgerAuDemarrage();
            var capture = new CaptureEngine(ecran);
            capture.CaptureInterrompue += () =>
            {
                Notification?.Invoke($"Écran {ecran.Index + 1} interrompu — capture relancée.");
                PlanifierRedemarrage($"écran {ecran.Index + 1} interrompu");
            };
            capture.Demarrer();
            var enc = new SegmentEncoder(capture.Frames, ring, preset, capture.Taille);
            _ = enc.BoucleEncodageAsync(_cts.Token);
            var pipeline = new Pipeline(capture, enc, ring, sources.Count > 1 ? $"ecran{ecran.Index + 1}" : null);
            _pipelines.Add(pipeline);
            _principal ??= pipeline;
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

        // Chien de garde : une session Windows Graphics Capture peut mourir sans rien
        // signaler (mise en veille du poste), l'horloge se figeant simplement — on a
        // ainsi enregistré 4 parties de LoL sur un bureau périmé les 16 et 17/08/2026.
        _chienDeGarde.Reinitialiser(DateTime.UtcNow);
        _surveillanceCapture = new System.Threading.Timer(_ =>
        {
            try
            {
                switch (_chienDeGarde.Observer(HorlogeCaptureLive, DateTime.UtcNow))
                {
                    case DecisionSurveillance.Redemarrer:
                        PlanifierRedemarrage($"horloge de capture gelée (tentative {_chienDeGarde.RedemarragesConsecutifs})");
                        break;
                    case DecisionSurveillance.Abandon:
                        Journal.Ecrire("[capture] gelée et non récupérable après plusieurs relances : abandon.");
                        Notification?.Invoke("Capture bloquée : relances sans effet. Redémarre Replayo.");
                        break;
                }
            }
            catch (Exception e) { Journal.Ecrire($"[capture] surveillance : {e.Message}"); }
        }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        BrancherReactionsSysteme();

        EnCapture = true;
        // Une ligne au démarrage : le journal n'est utile que si on peut vérifier
        // qu'il fonctionne AVANT l'incident, et elle date chaque session de capture.
        Journal.Ecrire($"[capture] démarrée — {_pipelines.Count} écran(s) : " +
                       string.Join(", ", _pipelines.Select(p => $"{p.Capture.Taille.Width}x{p.Capture.Taille.Height}")) +
                       $", buffer {cfg.DureeBufferSecondes} s, préréglage {cfg.Preset}");
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
        _surveillanceCapture?.Dispose(); _surveillanceCapture = null;
        _cts?.Cancel();
        _principal = null;
        foreach (var p in _pipelines) p.Capture.Arreter();
        _pipelines.Clear();
        _audioMix?.Dispose(); _audioMix = null;
        _audio?.Dispose(); _audio = null;
    }

    public void Redemarrer(ReplayoConfig cfg) { Arreter(); Demarrer(cfg); }

    /// Réveil du poste et changement de configuration d'écran invalident la session de
    /// capture sans que WGC ne signale quoi que ce soit : on relance sans attendre que
    /// le chien de garde constate le gel (90 s de buffer perdu sinon).
    private void BrancherReactionsSysteme()
    {
        if (_reactionsSystemeBranchees) return;
        _reactionsSystemeBranchees = true;
        Microsoft.Win32.SystemEvents.PowerModeChanged += SurChangementAlimentation;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SurChangementAffichage;
    }

    private void SurChangementAlimentation(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume) PlanifierRedemarrage("sortie de veille");
    }

    private void SurChangementAffichage(object? sender, EventArgs e)
        => PlanifierRedemarrage("changement de configuration d'écran");

    /// Relance le pipeline hors du thread appelant (timer, événement système), une
    /// seule à la fois. Le délai laisse l'affichage se stabiliser après un réveil.
    private void PlanifierRedemarrage(string raison)
    {
        if (!EnCapture) return;
        if (Interlocked.Exchange(ref _redemarrageEnCours, 1) == 1) return;
        Journal.Ecrire($"[capture] relance demandée : {raison}");
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000);
                var cfg = _cfg;
                Redemarrer(cfg);
                Journal.Ecrire($"[capture] relance terminée : EnCapture={EnCapture}, pipelines={_pipelines.Count}");
                if (!EnCapture) Notification?.Invoke("Capture non relancée — vérifie les écrans.");
            }
            catch (Exception ex) { Journal.Ecrire($"[capture] relance échouée : {ex}"); }
            finally { Interlocked.Exchange(ref _redemarrageEnCours, 0); }
        });
    }

    /// Horloge de capture du pipeline principal (null si capture arrêtée).
    /// ATTENTION : c'est l'horloge de l'ENCODEUR (frame que le transcodeur vient de
    /// tirer) — elle marque « jusqu'où le buffer est écrit », d'où son usage pour
    /// clipper. Elle retarde sur le direct de la profondeur de la FrameQueue.
    public TimeSpan? HorlogeCapture => _principal?.Enc.HorlogeCapture;

    /// Horloge de capture VIVE (dernière frame capturée) : « maintenant » sur la
    /// timeline des segments. C'est elle qui doit dater un événement temps réel
    /// (mode LoL), sinon l'événement est placé ~1,5 s trop tôt.
    public TimeSpan? HorlogeCaptureLive => _principal?.Capture.HorlogeLive;

    /// Fenêtre de rétention de tous les anneaux (mode LoL : étendue à 120 s en game).
    public void FenetreBuffer(int secondes) { foreach (var p in _pipelines) p.Ring.DureeMaxSecondes = secondes; }

    /// Chemin du mix audio continu de la session (null si audio désactivé/indisponible).
    public string? CheminAudioMix => _audioMix?.Chemin;

    /// Clip d'un intervalle de l'horloge de capture, sans ré-encodage, vers un chemin
    /// imposé. Rend aussi le début réel du fichier (frontière de segment ≤ debut).
    public async Task<(string Chemin, TimeSpan DebutReel)?> ClipperIntervalleAsync(TimeSpan debut, TimeSpan fin, string sortie)
    {
        if (!EnCapture || _pipelines.Count == 0) return null;
        var principal = _principal;
        if (principal is null) return null;
        var (segments, debutPremier) = principal.Ring.IntervalleAvecDebut(debut, fin);
        if (segments.Count == 0) return null;
        return await ClipService.AssemblerAsync(segments, sortie, CheminAudioMix, debutPremier.TotalSeconds)
            ? (sortie, debutPremier) : null;
    }

    public async Task<List<string>> ClipperAsync()
    {
        var resultats = new List<string>();
        if (!EnCapture) return resultats;
        var clips = new ClipService(_cfg);
        foreach (var (p, i) in _pipelines.Select((p, i) => (p, i)))
        {
            // L'audio (mix) n'accompagne que l'écran principal, comme avant le découplage.
            var chemin = await clips.CreerClipAsync(p.Ring, p.Enc.HorlogeCapture, p.Suffixe,
                i == 0 ? CheminAudioMix : null);
            if (chemin is not null) resultats.Add(chemin);
        }
        return resultats;
    }

    public void Dispose()
    {
        if (_reactionsSystemeBranchees)
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= SurChangementAlimentation;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= SurChangementAffichage;
            _reactionsSystemeBranchees = false;
        }
        Arreter();
    }
}
