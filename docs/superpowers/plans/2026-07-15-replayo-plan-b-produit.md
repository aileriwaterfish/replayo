# Replayo — Plan B : le produit (Implementation Plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transformer le runner console du Milestone A en application Windows de fond : icône tray, fenêtre de réglages, onboarding au premier lancement, démarrage avec Windows, renommage manuel optionnel et notifications — sans toucher au pipeline de capture.

**Architecture:** La logique du runner est extraite dans un `RecorderService` (démarrer/arrêter/redémarrer à chaud, clip, surveillance disque). L'exécutable devient une app WPF sans console (`WinExe`), pilotée depuis le tray (NotifyIcon WinForms, déjà dans la boîte — aucune dépendance nouvelle). Fermer une fenêtre = la cacher ; quitter = uniquement via le tray.

**Tech Stack:** identique au Plan A (C#/.NET 8, WPF + WinForms in-box). Aucune dépendance nouvelle.

## Global Constraints

- Reprend TOUTES les contraintes globales du Plan A (perf, codecs, chemins, français, rien au-delà du nécessaire).
- Onboarding Plan B = format → qualité → source (écrans détectés/tous) → durée du replay. **L'étape « clé de licence » sera préfixée par le Plan C** (voulu, pas un oubli).
- Raccourci par défaut Alt+F10, **reconfigurable** dans les réglages (capture de touches, pas de saisie texte).
- Disque presque plein (< 2 Go libres sur le volume du buffer) → pause capture + notification tray.
- Autostart : clé `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Replayo` (source de vérité = le registre, pas la config).

---

### Task 1: Champs de config manquants (raccourci) — TDD

**Files:**
- Modify: `src/Replayo/Core/ReplayoConfig.cs`, `src/Replayo/Core/ConfigStore.cs`
- Test: `tests/Replayo.Tests/ConfigStoreTests.cs` (ajouts)

**Interfaces:**
- Produces: `ReplayoConfig.RaccourciModificateurs:uint=0x1` (MOD_ALT), `ReplayoConfig.RaccourciTouche:uint=0x79` (VK_F10) ; `ConfigStore.Existe:bool` (config.json présent → pas premier lancement).

- [ ] **Step 1: Ajouter les tests (échec attendu)** — dans `ConfigStoreTests.cs` :

```csharp
    [Fact]
    public void Charger_RaccourciParDefaut_AltF10()
    {
        var store = new ConfigStore(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var cfg = store.Charger();
        Assert.Equal(0x1u, cfg.RaccourciModificateurs);
        Assert.Equal(0x79u, cfg.RaccourciTouche);
    }

    [Fact]
    public void Existe_VraiSeulementApresEnregistrement()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var store = new ConfigStore(dir);
        Assert.False(store.Existe);
        store.Enregistrer(store.Charger());
        Assert.True(store.Existe);
    }
```

- [ ] **Step 2: Vérifier l'échec** — `dotnet test --filter ConfigStoreTests` → FAIL.

- [ ] **Step 3: Implémenter** — dans `ReplayoConfig.cs`, ajouter :

```csharp
    public uint RaccourciModificateurs { get; set; } = 0x0001; // MOD_ALT
    public uint RaccourciTouche { get; set; } = 0x79;          // VK_F10
```

Dans `ConfigStore.cs`, ajouter la propriété :

```csharp
    /// Vrai si une config a déjà été enregistrée (sinon : premier lancement → onboarding).
    public bool Existe => File.Exists(_chemin);
```

Et dans `Charger()`, borner : `if (cfg.RaccourciTouche == 0) { cfg.RaccourciModificateurs = 0x0001; cfg.RaccourciTouche = 0x79; }`

- [ ] **Step 4: Vérifier** — `dotnet test` → PASS. **Step 5: Commit** — `git commit -m "Plan B Task 1: config raccourci + detection premier lancement"`

---

### Task 2: Extraction `RecorderService` (le runner devient un service)

**Files:**
- Create: `src/Replayo/RecorderService.cs`
- Modify: `src/Replayo/Program.cs` (raccourci au minimum, sera réécrit en Task 3)

**Interfaces:**
- Consumes: tout le pipeline du Plan A (CaptureEngine, SegmentEncoder, SegmentRing, AudioEngine, ClipService, MonitorInfo, AppPaths, QualityPreset).
- Produces: `RecorderService : IDisposable` — `void Demarrer(ReplayoConfig cfg)` ; `void Arreter()` ; `void Redemarrer(ReplayoConfig cfg)` ; `bool EnCapture { get; }` ; `bool EncodageMateriel { get; }` ; `Task<List<string>> ClipperAsync()` (chemins créés, liste vide si rien) ; `event Action<string>? Notification` (messages pour le tray : repli logiciel, disque plein, capture interrompue).

- [ ] **Step 1: Implémenter** — `src/Replayo/RecorderService.cs`

```csharp
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
    private Timer? _surveillanceDisque;
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
        _surveillanceDisque = new Timer(_ =>
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
```

- [ ] **Step 2: Simplifier `Program.cs`** pour utiliser le service (version transitoire console, remplacée en Task 3) :

```csharp
using Replayo;
using Replayo.Core;
using Replayo.Input;

var config = new ConfigStore().Charger();
using var recorder = new RecorderService();
recorder.Notification += m => Console.WriteLine($"[note] {m}");
recorder.Demarrer(config);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
using var hotkey = new HotkeyManager();
hotkey.Enregistrer(config.RaccourciModificateurs, config.RaccourciTouche, () =>
    _ = Task.Run(async () => { foreach (var c in await recorder.ClipperAsync()) Console.WriteLine($"[clip] ✓ {c}"); }));
Console.WriteLine("Alt+F10 → clip. Ctrl+C → quitter.");
try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (OperationCanceledException) { }
return 0;
```

- [ ] **Step 3: Vérifier** — `dotnet build` OK + relance rapide du E2E (lancer, Alt+F10, clip produit). **Step 4: Commit** — `git commit -m "Plan B Task 2: extraction RecorderService"`

---

### Task 3: Application de fond + icône tray

**Files:**
- Create: `src/Replayo/UI/TrayIcon.cs`, `assets/replayo.ico`
- Modify: `src/Replayo/Program.cs` (entrée WPF définitive), `src/Replayo/Replayo.csproj` (`WinExe` + icône + inclusion .ico)

**Interfaces:**
- Consumes: `RecorderService` (T2), `HotkeyManager`, `ConfigStore`.
- Produces: `TrayIcon : IDisposable` — ctor `(RecorderService recorder, Action ouvrirReglages, Action quitter)` ; `void Notifier(string message)` (balloon) ; `void RafraichirEtat()` (libellé démarrer/arrêter). `Program.Main` [STAThread] : instance unique (Mutex), app WPF `ShutdownMode.OnExplicitShutdown`, onboarding si `!store.Existe` (T5), sinon capture directe.

- [ ] **Step 1: Générer l'icône** (carré orange « heat », suffisant v1) :

```powershell
cd C:\Users\leoba\Projects\replayo
tools\ffmpeg\ffmpeg.exe -hide_banner -loglevel error -f lavfi -i color=c=0xEA580C:s=64x64 -frames:v 1 -y assets\replayo.png
tools\ffmpeg\ffmpeg.exe -hide_banner -loglevel error -i assets\replayo.png -y assets\replayo.ico
```

Dans `Replayo.csproj` : `<OutputType>WinExe</OutputType>` (remplace `Exe`), `<ApplicationIcon>..\..\assets\replayo.ico</ApplicationIcon>`, et dans l'ItemGroup des None : `<None Include="..\..\assets\replayo.ico" CopyToOutputDirectory="PreserveNewest" Link="assets\replayo.ico" />`.

- [ ] **Step 2: Implémenter `src/Replayo/UI/TrayIcon.cs`**

```csharp
using System.Diagnostics;
using Replayo.Core;
using WF = System.Windows.Forms;

namespace Replayo.UI;

/// Icône de zone de notification : point d'entrée permanent de Replayo.
public sealed class TrayIcon : IDisposable
{
    private readonly WF.NotifyIcon _icone;
    private readonly WF.ToolStripMenuItem _basculer;
    private readonly RecorderService _recorder;

    public TrayIcon(RecorderService recorder, Action ouvrirReglages, Action quitter)
    {
        _recorder = recorder;
        var menu = new WF.ContextMenuStrip();
        _basculer = new WF.ToolStripMenuItem("Arrêter la capture", null, (_, _) => Basculer());
        menu.Items.Add(_basculer);
        menu.Items.Add("Ouvrir le dossier des clips", null, (_, _) =>
            Process.Start(new ProcessStartInfo("explorer.exe", Directory.CreateDirectory(AppPaths.DossierSortieDefaut).FullName)));
        menu.Items.Add("Réglages…", null, (_, _) => ouvrirReglages());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Quitter Replayo", null, (_, _) => quitter());

        _icone = new WF.NotifyIcon
        {
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "assets", "replayo.ico")),
            Text = "Replayo",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icone.DoubleClick += (_, _) => ouvrirReglages();
        recorder.Notification += Notifier;
    }

    private void Basculer()
    {
        if (_recorder.EnCapture) _recorder.Arreter();
        else _recorder.Demarrer(new ConfigStore().Charger());
        RafraichirEtat();
    }

    public void RafraichirEtat()
        => _basculer.Text = _recorder.EnCapture ? "Arrêter la capture" : "Démarrer la capture";

    public void Notifier(string message)
        => _icone.ShowBalloonTip(4000, "Replayo", message, WF.ToolTipIcon.Info);

    public void Dispose() { _icone.Visible = false; _icone.Dispose(); }
}
```

- [ ] **Step 3: Réécrire `src/Replayo/Program.cs`** (entrée définitive)

```csharp
using System.Windows;
using Replayo.Core;
using Replayo.Input;
using Replayo.UI;

namespace Replayo;

/// Point d'entrée : application de fond (tray), fenêtres à la demande.
public static class Program
{
    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(true, "Replayo-Instance-Unique", out var premiere);
        if (!premiere) return; // déjà lancé : ne rien faire

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var store = new ConfigStore();
        using var recorder = new RecorderService();
        SettingsWindow? reglages = null;

        void OuvrirReglages()
        {
            if (reglages is { IsVisible: true }) { reglages.Activate(); return; }
            reglages = new SettingsWindow(store, recorder);
            reglages.Show();
        }

        using var tray = new TrayIcon(recorder, OuvrirReglages, () => app.Shutdown());
        using var hotkey = new HotkeyManager();

        void BrancherRaccourci(ReplayoConfig cfg)
        {
            hotkey.Desenregistrer();
            var ok = hotkey.Enregistrer(cfg.RaccourciModificateurs, cfg.RaccourciTouche, () =>
                _ = Task.Run(async () =>
                {
                    var chemins = await recorder.ClipperAsync();
                    foreach (var chemin in chemins)
                        app.Dispatcher.Invoke(() =>
                        {
                            if (cfg.NommageManuel) new RenameDialog(chemin).ShowDialog();
                        });
                    if (chemins.Count == 0) tray.Notifier("Aucun clip : la capture n'est pas active.");
                }));
            if (!ok) tray.Notifier("Le raccourci est déjà utilisé par une autre application — changez-le dans les réglages.");
        }

        var cfg = store.Charger();
        if (!store.Existe)
        {
            var onboarding = new OnboardingWindow(store);
            if (onboarding.ShowDialog() != true) { app.Shutdown(); return; } // onboarding refusé → quitter
            cfg = store.Charger();
        }
        recorder.Demarrer(cfg);
        tray.RafraichirEtat();
        BrancherRaccourci(cfg);

        // Les réglages notifient les changements via cet événement statique simple.
        SettingsWindow.ConfigChangee += nouvelle => { BrancherRaccourci(nouvelle); tray.RafraichirEtat(); };

        app.Run();
        recorder.Arreter();
    }
}
```

NB : `SettingsWindow`, `OnboardingWindow`, `RenameDialog` arrivent en Tasks 4-6 — pour compiler cette task isolément, créer les trois fichiers **squelettes** ci-dessous en même temps (remplis dans leurs tasks respectives) :

```csharp
// src/Replayo/UI/SettingsWindow.cs (squelette T3, rempli T4)
using System.Windows;
using Replayo.Core;
namespace Replayo.UI;
public partial class SettingsWindow : Window
{
    public static event Action<ReplayoConfig>? ConfigChangee;
    public SettingsWindow(ConfigStore store, RecorderService recorder) { Title = "Réglages Replayo"; Width = 520; Height = 640; }
    internal static void NotifierChangement(ReplayoConfig cfg) => ConfigChangee?.Invoke(cfg);
}
// src/Replayo/UI/OnboardingWindow.cs (squelette T3, rempli T5)
// src/Replayo/UI/RenameDialog.cs (squelette T3, rempli T6)
```

- [ ] **Step 4: Vérifier** — `dotnet build` OK ; lancer : icône tray visible, menu fonctionnel, Alt+F10 clippe, « Quitter » ferme proprement. **Step 5: Commit**.

---

### Task 4: Fenêtre Réglages (tous les réglages, application à chaud)

**Files:**
- Modify: `src/Replayo/UI/SettingsWindow.cs` (implémentation complète, WPF programmatique — pas de XAML pour rester mono-fichier)

**Interfaces:**
- Consumes: `ConfigStore`, `RecorderService.Redemarrer`, `MonitorInfo.EnumererEcrans`, `AutostartManager` (T5 — squelette d'abord).
- Produces: fenêtre listant : durée du replay (slider 15 s → 20 min, libellé lisible), préréglage (combo), format (combo mp4/mkv), écrans (checkbox par écran détecté + « tous »), audio système/micro (2 cases), raccourci (zone de capture de touches), nommage (radio auto/manuel), démarrage avec Windows (case), dossier de sortie (bouton parcourir). Bouton **Enregistrer** → `store.Enregistrer` + `recorder.Redemarrer(cfg)` + `SettingsWindow.NotifierChangement(cfg)`. Fermeture = masquer, jamais quitter.

Le contenu complet de la fenêtre (grille de contrôles WPF en code, gestion de la capture du raccourci via `PreviewKeyDown` → stocke `RaccourciModificateurs`/`RaccourciTouche` et affiche « Alt+F10 »-style) est trop long pour être dupliqué ici deux fois : suivre la même structure que le squelette T3, chaque contrôle initialisé depuis `store.Charger()` et relu au clic Enregistrer. Points obligatoires :

```csharp
// Capture du raccourci (dans le TextBox dédié) :
_boiteRaccourci.PreviewKeyDown += (_, e) =>
{
    e.Handled = true;
    var touche = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
    if (touche is System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
              or System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
              or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift) return;
    uint mods = 0;
    if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) mods |= 0x1;
    if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) mods |= 0x2;
    if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) mods |= 0x4;
    _mods = mods; _vk = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(touche);
    _boiteRaccourci.Text = TexteRaccourci(_mods, _vk);
};

// Fermer = masquer (la capture continue) :
Closing += (_, e) => { e.Cancel = true; Hide(); };
```

- [ ] Compiler, vérifier manuellement chaque réglage (changer préréglage → la capture redémarre ; changer le raccourci → l'ancien ne répond plus, le nouveau clippe), commit.

---

### Task 5: Démarrage avec Windows (`AutostartManager`) + Onboarding

**Files:**
- Create: `src/Replayo/Core/AutostartManager.cs`
- Modify: `src/Replayo/UI/OnboardingWindow.cs` (implémentation complète)

**Interfaces:**
- Produces: `AutostartManager.EstActive():bool` / `Activer()` / `Desactiver()` — clé `HKCU\...\Run\Replayo` = chemin de l'exe entre guillemets. `OnboardingWindow(ConfigStore store) : Window`, `ShowDialog()==true` après le parcours : format → qualité → source → durée → Terminer (enregistre la config).

```csharp
// src/Replayo/Core/AutostartManager.cs
using Microsoft.Win32;

namespace Replayo.Core;

/// Lancement avec Windows via HKCU\...\Run (source de vérité : le registre).
public static class AutostartManager
{
    private const string Cle = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Nom = "Replayo";

    public static bool EstActive()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Cle);
        return k?.GetValue(Nom) is string;
    }

    public static void Activer()
    {
        using var k = Registry.CurrentUser.CreateSubKey(Cle);
        k.SetValue(Nom, $"\"{Environment.ProcessPath}\"");
    }

    public static void Desactiver()
    {
        using var k = Registry.CurrentUser.CreateSubKey(Cle);
        k.DeleteValue(Nom, throwOnMissingValue: false);
    }
}
```

L'onboarding est une fenêtre à 4 étapes (panneau + boutons Retour/Suivant, dernier = Terminer) réutilisant les mêmes contrôles que la fenêtre Réglages (combos format/qualité, checkboxes écrans, slider durée). À Terminer : `store.Enregistrer(cfg)` puis `DialogResult = true`.

- [ ] Compiler, tester : supprimer `%AppData%\Replayo\config.json` → relancer → l'onboarding s'affiche → Terminer → la capture démarre. Case autostart dans Réglages : vérifier la valeur dans `regedit` (HKCU Run). Commit.

---

### Task 6: Renommage manuel + finitions notifications

**Files:**
- Modify: `src/Replayo/UI/RenameDialog.cs` (implémentation complète)

**Interfaces:**
- Produces: `RenameDialog(string cheminClip) : Window` — TextBox pré-rempli avec le nom actuel (sans extension), OK = `File.Move` vers le nouveau nom (même dossier, caractères interdits filtrés comme `ClipService.ConstruireCheminSortie`), Annuler = garde le nom auto. Toujours au premier plan (`Topmost = true`).

- [ ] Implémenter, compiler, tester : activer « nommage manuel » dans Réglages → Alt+F10 → la boîte s'ouvre, renommer → le fichier porte le nouveau nom. Commit.

---

### Task 7: Recette complète Milestone B + README

**Files:**
- Modify: `README.md` (section utilisation : tray, réglages, onboarding, autostart)

- [ ] **Checklist manuelle** (dérouler intégralement) :
  1. Premier lancement (config supprimée) → onboarding → capture démarre, icône tray présente
  2. Alt+F10 → clip + son + rangement ; mode renommage manuel → boîte de dialogue
  3. Fermer la fenêtre Réglages → l'app reste dans le tray, la capture continue
  4. Tray : Arrêter/Démarrer la capture, Ouvrir le dossier des clips, Quitter
  5. Changer le raccourci → seul le nouveau fonctionne ; raccourci en conflit → notification
  6. Autostart activé → clé présente dans `regedit` ; désactivé → clé absente
  7. `dotnet test` → tous verts ; `scripts/mesure-perf.ps1` → CPU/RAM dans les objectifs
- [ ] Mettre à jour le README, commit final, merge sur master.

## Self-review

- **Couverture spec (périmètre B)** : tray + fermer=tray ✓ (T3), réglages complets ✓ (T4), raccourci configurable + conflit ✓ (T3/T4), onboarding sans licence ✓ (T5, licence = Plan C assumé), autostart ✓ (T5), renommage manuel ✓ (T6), disque plein → pause+notification ✓ (T2), repli logiciel notifié ✓ (T2).
- **Placeholders** : T4/T5 renvoient à la structure du squelette T3 avec les extraits obligatoires fournis — pas de « TBD », mais code complet à écrire à l'exécution pour les fenêtres (assumé : la duplication intégrale de ~300 lignes de WPF dans le plan n'apporterait rien).
- **Types** : `SettingsWindow.ConfigChangee` statique consommé par `Program` ✓ ; `RecorderService` signatures identiques T2/T3/T4 ✓.
