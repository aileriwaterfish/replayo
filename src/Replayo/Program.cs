using Replayo.Audio;
using Replayo.Buffer;
using Replayo.Capture;
using Replayo.Clip;
using Replayo.Core;
using Replayo.Encoding;
using Replayo.Input;

// Replayo — Milestone A : runner console.
// Capture l'écran (config), maintient l'anneau, clippe sur Alt+F10. Ctrl+C pour quitter.

var config = new ConfigStore().Charger();
var preset = QualityPreset.DepuisNom(config.Preset);
var ecrans = MonitorInfo.EnumererEcrans();
var sources = config.SourcesEcrans.Count == 0
    ? ecrans.Where(e => e.Principal).ToList()
    : ecrans.Where(e => config.SourcesEcrans.Contains(e.Index)).ToList();
if (sources.Count == 0) { Console.Error.WriteLine("Aucun écran source."); return 1; }

Console.WriteLine($"Replayo — buffer {config.DureeBufferSecondes}s, préréglage {config.Preset}, " +
                  $"{sources.Count} écran(s), audio système={config.AudioSysteme} micro={config.AudioMicro}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var audio = AudioEngine.CreerSiActive(config);
audio?.Demarrer();

// Un pipeline complet (capture → encodeur → anneau) par écran sélectionné.
// L'audio n'est mixé que dans le premier pipeline (une piste par clip suffit).
var pipelines = new List<(CaptureEngine Capture, SegmentEncoder Enc, SegmentRing Ring, Task Boucle, string? Suffixe)>();
foreach (var (ecran, i) in sources.Select((e, i) => (e, i)))
{
    var ring = new SegmentRing(Path.Combine(AppPaths.DossierBuffer, $"ecran{ecran.Index}"), config.DureeBufferSecondes);
    ring.PurgerAuDemarrage();
    var capture = new CaptureEngine(ecran);
    capture.CaptureInterrompue += () => Console.Error.WriteLine($"[capture] écran {ecran.Index} interrompu — redémarrer Replayo.");
    capture.Demarrer();
    var enc = new SegmentEncoder(capture.Frames, i == 0 ? audio : null, ring, preset, capture.Taille);
    var boucle = enc.BoucleEncodageAsync(cts.Token);
    pipelines.Add((capture, enc, ring, boucle, sources.Count > 1 ? $"ecran{ecran.Index + 1}" : null));
}

await Task.Delay(3000); // laisse la 1re session d'encodage démarrer
if (pipelines.Any(p => !p.Enc.EncodageMateriel))
    Console.WriteLine("⚠ Encodeur matériel indisponible : repli logiciel (CPU accru).");

var clips = new ClipService(config);
using var hotkey = new HotkeyManager();
bool ok = hotkey.Enregistrer(HotkeyManager.MOD_ALT, HotkeyManager.VK_F10, () =>
{
    _ = Task.Run(async () =>
    {
        foreach (var p in pipelines)
        {
            var chemin = await clips.CreerClipAsync(p.Ring, p.Enc.HorlogeCapture, p.Suffixe);
            Console.WriteLine(chemin is null ? "[clip] échec (voir erreurs)" : $"[clip] ✓ {chemin}");
        }
    });
});
Console.WriteLine(ok ? "Alt+F10 → clip. Ctrl+C → quitter." : "⚠ Alt+F10 déjà utilisé par une autre application.");

try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (OperationCanceledException) { }
foreach (var p in pipelines) p.Capture.Arreter();
audio?.Dispose();
return 0;
