# Replayo — Plan A : noyau de capture (Implementation Plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Un exécutable Windows minimal qui capture l'écran en continu (WGC → encodeur matériel), maintient un anneau de segments sur disque, et produit un clip des N dernières secondes via Alt+F10, rangé et accompagné d'un son.

**Architecture:** Une file de frames GPU alimentée par Windows Graphics Capture ; un encodeur par segments de 10 s (MediaStreamSource + MediaTranscoder, accélération matérielle) ; un anneau de segments MP4 sur disque ; le clip assemble les segments couvrant la durée demandée avec ffmpeg `-c copy` (sans ré-encodage). Audio : WASAPI loopback (+ micro optionnel) mixé et encodé AAC dans la même session.

**Tech Stack:** C#/.NET 8 (`net8.0-windows10.0.19041.0`, projections WinRT intégrées), WPF/WinForms activés (plans B), NAudio (audio), ffmpeg.exe embarqué (mux), xUnit (tests).

## Global Constraints

- Cible : Windows 10 (1903+) / Windows 11 uniquement ; x64.
- Priorité absolue : consommation minimale — encodage matériel obligatoire si disponible (`HardwareAccelerationEnabled = true`), repli logiciel signalé.
- Buffer circulaire réglable **15 s → 20 min** (v1 : valeur dans config.json), segments ~**10 s**.
- Codecs : H.264 + AAC. Formats de sortie : MP4 ou MKV (config).
- Rangement : `Vidéos\Replayo\<Application>\<AAAA-MM>\` ; appli indétectable → `Bureau`.
- Raccourci par défaut : **Alt+F10**.
- Préréglages qualité : Éco (1080p 30 fps 8 Mb/s) · Équilibré (natif 60 fps 20 Mb/s) · Qualité (natif 60 fps 40 Mb/s).
- Aucun secret en dur. Code commenté en français. Pas de fichier/abstraction au-delà du nécessaire.
- Granularité v1 assumée : le clip commence sur un début de segment → durée réelle ∈ [N, N+10 s] (documenté README).
- Dépendances approuvées le 15/07/2026 : ffmpeg.exe (LGPL, embarqué non versionné) + NAudio (MIT). Toute autre dépendance = STOP et demander.

---

### Task 1: Squelette de la solution + ffmpeg

**Files:**
- Create: `Replayo.sln`, `src/Replayo/Replayo.csproj`, `tests/Replayo.Tests/Replayo.Tests.csproj`, `src/Replayo/Program.cs` (provisoire), `scripts/installer-ffmpeg.ps1`, `README.md`
- Modify: `.gitignore`

**Interfaces:**
- Produces: solution compilable `dotnet build` ; `tools/ffmpeg/ffmpeg.exe` présent localement (git-ignoré).

- [ ] **Step 1: Créer les projets**

```powershell
cd C:\Users\leoba\Projects\replayo
dotnet new sln -n Replayo
dotnet new console -o src/Replayo -n Replayo
dotnet new xunit -o tests/Replayo.Tests -n Replayo.Tests
dotnet sln add src/Replayo tests/Replayo.Tests
dotnet add tests/Replayo.Tests reference src/Replayo
dotnet add src/Replayo package NAudio
```

- [ ] **Step 2: Remplacer `src/Replayo/Replayo.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PlatformTarget>x64</PlatformTarget>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NAudio" Version="2.2.1" />
  </ItemGroup>
  <ItemGroup>
    <None Include="..\..\tools\ffmpeg\ffmpeg.exe" CopyToOutputDirectory="PreserveNewest" Link="ffmpeg.exe" Condition="Exists('..\..\tools\ffmpeg\ffmpeg.exe')" />
    <None Include="..\..\assets\clip.wav" CopyToOutputDirectory="PreserveNewest" Link="assets\clip.wav" Condition="Exists('..\..\assets\clip.wav')" />
  </ItemGroup>
</Project>
```

`tests/Replayo.Tests/Replayo.Tests.csproj` : changer le TargetFramework en `net8.0-windows10.0.19041.0` (sinon la référence au projet principal ne compile pas).

Créer `src/Replayo/app.manifest` (DPI + version Windows) :

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 / 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 3: Script `scripts/installer-ffmpeg.ps1`** (téléchargement manuel une fois — le binaire n'est PAS versionné)

```powershell
# Télécharge ffmpeg (build essentials gyan.dev) dans tools\ffmpeg\ — à lancer une fois.
$ErrorActionPreference = "Stop"
$dest = Join-Path $PSScriptRoot "..\tools\ffmpeg"
New-Item -ItemType Directory -Force $dest | Out-Null
$zip = Join-Path $env:TEMP "ffmpeg-release-essentials.zip"
Invoke-WebRequest "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip" -OutFile $zip
Expand-Archive $zip (Join-Path $env:TEMP "ffmpeg-x") -Force
$exe = Get-ChildItem (Join-Path $env:TEMP "ffmpeg-x") -Recurse -Filter ffmpeg.exe | Select-Object -First 1
Copy-Item $exe.FullName (Join-Path $dest "ffmpeg.exe") -Force
& (Join-Path $dest "ffmpeg.exe") -version
```

Ajouter à `.gitignore` : `tools/ffmpeg/` et `assets/clip.wav` NON (le wav sera versionné, petit).

- [ ] **Step 4: Vérifier la compilation**

Run: `dotnet build` — Expected: `Build succeeded` (avertissements OK).
Run: `powershell -ExecutionPolicy Bypass -File scripts/installer-ffmpeg.ps1` — Expected: version ffmpeg affichée.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Task 1: squelette solution .NET 8 + script ffmpeg"
```

---

### Task 2: Configuration (`ConfigStore` + `QualityPreset`)

**Files:**
- Create: `src/Replayo/Core/AppPaths.cs`, `src/Replayo/Core/QualityPreset.cs`, `src/Replayo/Core/ReplayoConfig.cs`, `src/Replayo/Core/ConfigStore.cs`
- Test: `tests/Replayo.Tests/ConfigStoreTests.cs`

**Interfaces:**
- Produces: `ReplayoConfig` (propriétés : `DureeBufferSecondes:int=300`, `Preset:string="equilibre"`, `FormatSortie:string="mp4"`, `SourcesEcrans:List<int>` vide = écran principal, `AudioSysteme:bool=true`, `AudioMicro:bool=false`, `NommageManuel:bool=false`, `DossierSortie:string=""` vide = Vidéos\Replayo) ; `ConfigStore.Charger()` / `Enregistrer(config)` ; `QualityPreset.DepuisNom(string)` → record `QualityPreset(int? Largeur, int? Hauteur, int Fps, uint DebitBitsParSeconde)` (null = natif) ; `AppPaths.DossierConfig`, `AppPaths.DossierBuffer`, `AppPaths.DossierSortieDefaut`.

- [ ] **Step 1: Écrire le test qui échoue** — `tests/Replayo.Tests/ConfigStoreTests.cs`

```csharp
using Replayo.Core;
using Xunit;

public class ConfigStoreTests
{
    [Fact]
    public void Charger_SansFichier_RendLesDefauts()
    {
        var store = new ConfigStore(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var cfg = store.Charger();
        Assert.Equal(300, cfg.DureeBufferSecondes);
        Assert.Equal("equilibre", cfg.Preset);
        Assert.Equal("mp4", cfg.FormatSortie);
        Assert.True(cfg.AudioSysteme);
        Assert.False(cfg.AudioMicro);
    }

    [Fact]
    public void EnregistrerPuisCharger_ConserveLesValeurs()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var store = new ConfigStore(dir);
        var cfg = store.Charger();
        cfg.DureeBufferSecondes = 1200; // 20 min = borne max
        cfg.FormatSortie = "mkv";
        store.Enregistrer(cfg);
        Assert.Equal(1200, new ConfigStore(dir).Charger().DureeBufferSecondes);
        Assert.Equal("mkv", new ConfigStore(dir).Charger().FormatSortie);
    }

    [Fact]
    public void Charger_BorneLaDureeEntre15Et1200Secondes()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var store = new ConfigStore(dir);
        var cfg = store.Charger();
        cfg.DureeBufferSecondes = 99999;
        store.Enregistrer(cfg);
        Assert.Equal(1200, new ConfigStore(dir).Charger().DureeBufferSecondes);
    }

    [Theory]
    [InlineData("eco", 1920, 1080, 30, 8_000_000u)]
    [InlineData("qualite", null, null, 60, 40_000_000u)]
    public void QualityPreset_DepuisNom(string nom, int? l, int? h, int fps, uint debit)
    {
        var p = QualityPreset.DepuisNom(nom);
        Assert.Equal(l, p.Largeur);
        Assert.Equal(h, p.Hauteur);
        Assert.Equal(fps, p.Fps);
        Assert.Equal(debit, p.DebitBitsParSeconde);
    }
}
```

- [ ] **Step 2: Vérifier l'échec** — Run: `dotnet test` — Expected: FAIL (types inexistants).

- [ ] **Step 3: Implémenter**

`src/Replayo/Core/AppPaths.cs` :

```csharp
namespace Replayo.Core;

/// Chemins de l'application — tout est sous le profil utilisateur, rien en Program Files.
public static class AppPaths
{
    public static string DossierConfig =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Replayo");

    public static string DossierBuffer =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Replayo", "buffer");

    public static string DossierSortieDefaut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Replayo");

    public static string FfmpegExe =>
        Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
}
```

`src/Replayo/Core/QualityPreset.cs` :

```csharp
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
```

`src/Replayo/Core/ReplayoConfig.cs` :

```csharp
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
```

`src/Replayo/Core/ConfigStore.cs` :

```csharp
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
```

- [ ] **Step 4: Vérifier** — Run: `dotnet test` — Expected: PASS (4 tests).

- [ ] **Step 5: Commit** — `git add -A && git commit -m "Task 2: config + presets qualite"`

---

### Task 3: Anneau de segments (`SegmentRing`) — logique pure

**Files:**
- Create: `src/Replayo/Buffer/SegmentRing.cs`
- Test: `tests/Replayo.Tests/SegmentRingTests.cs`

**Interfaces:**
- Produces: `SegmentRing(string dossierBuffer, int dureeMaxSecondes)` ; `PurgerAuDemarrage()` (vide le dossier) ; `string ProchainCheminSegment()` (ex. `seg_000042.mp4`) ; `Ajouter(string chemin, TimeSpan debut, TimeSpan fin)` (enregistre + supprime les segments hors fenêtre) ; `IReadOnlyList<string> SegmentsPourDuree(TimeSpan duree, TimeSpan maintenant)` (chemins ordonnés couvrant `[maintenant-duree, maintenant]`).
- Consumes: rien (logique pure + IO fichiers).

- [ ] **Step 1: Test qui échoue** — `tests/Replayo.Tests/SegmentRingTests.cs`

```csharp
using Replayo.Buffer;
using Xunit;

public class SegmentRingTests
{
    private static SegmentRing NouvelAnneau(int dureeMax = 60)
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return new SegmentRing(dir, dureeMax);
    }

    [Fact]
    public void SegmentsPourDuree_RendLesSegmentsCouvrantLaFenetre()
    {
        var ring = NouvelAnneau(dureeMax: 60);
        // 6 segments de 10 s : [0-10] ... [50-60]
        for (int i = 0; i < 6; i++)
        {
            var chemin = ring.ProchainCheminSegment();
            File.WriteAllText(chemin, "x");
            ring.Ajouter(chemin, TimeSpan.FromSeconds(i * 10), TimeSpan.FromSeconds(i * 10 + 10));
        }
        // Les 25 dernières secondes à t=60 → doit couvrir [35,60] → segments [30-40],[40-50],[50-60]
        var clips = ring.SegmentsPourDuree(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(60));
        Assert.Equal(3, clips.Count);
    }

    [Fact]
    public void Ajouter_SupprimeLesSegmentsHorsFenetre()
    {
        var ring = NouvelAnneau(dureeMax: 30); // fenêtre max 30 s + marge d'1 segment
        var chemins = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            var chemin = ring.ProchainCheminSegment();
            File.WriteAllText(chemin, "x");
            chemins.Add(chemin);
            ring.Ajouter(chemin, TimeSpan.FromSeconds(i * 10), TimeSpan.FromSeconds(i * 10 + 10));
        }
        // à t=60, fenêtre 30 s → [30,60] : les segments [0-10] et [10-20] doivent être supprimés du disque
        Assert.False(File.Exists(chemins[0]));
        Assert.False(File.Exists(chemins[1]));
        Assert.True(File.Exists(chemins[5]));
    }

    [Fact]
    public void PurgerAuDemarrage_VideLeDossier()
    {
        var ring = NouvelAnneau();
        var chemin = ring.ProchainCheminSegment();
        File.WriteAllText(chemin, "x");
        ring.PurgerAuDemarrage();
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(chemin)!));
    }
}
```

- [ ] **Step 2: Vérifier l'échec** — Run: `dotnet test --filter SegmentRingTests` — Expected: FAIL.

- [ ] **Step 3: Implémenter** — `src/Replayo/Buffer/SegmentRing.cs`

```csharp
namespace Replayo.Buffer;

/// Anneau de segments encodés sur disque. Ne connaît ni la capture ni l'encodage :
/// il ne gère que des fichiers + leurs intervalles de temps (horloge de capture).
public sealed class SegmentRing(string dossierBuffer, int dureeMaxSecondes)
{
    private readonly record struct Entree(string Chemin, TimeSpan Debut, TimeSpan Fin);
    private readonly List<Entree> _entrees = new();
    private readonly object _verrou = new();
    private int _compteur;

    public void PurgerAuDemarrage()
    {
        Directory.CreateDirectory(dossierBuffer);
        foreach (var f in Directory.GetFiles(dossierBuffer)) File.Delete(f);
        lock (_verrou) _entrees.Clear();
    }

    public string ProchainCheminSegment()
    {
        Directory.CreateDirectory(dossierBuffer);
        return Path.Combine(dossierBuffer, $"seg_{Interlocked.Increment(ref _compteur):D6}.mp4");
    }

    public void Ajouter(string chemin, TimeSpan debut, TimeSpan fin)
    {
        lock (_verrou)
        {
            _entrees.Add(new(chemin, debut, fin));
            // Supprime tout segment entièrement antérieur à la fenêtre (avec la marge d'un segment,
            // pour que la fenêtre demandée soit toujours entièrement couvrable).
            var limite = fin - TimeSpan.FromSeconds(dureeMaxSecondes) - (fin - debut);
            for (int i = _entrees.Count - 1; i >= 0; i--)
            {
                if (_entrees[i].Fin < limite)
                {
                    try { File.Delete(_entrees[i].Chemin); } catch { /* best effort */ }
                    _entrees.RemoveAt(i);
                }
            }
        }
    }

    public IReadOnlyList<string> SegmentsPourDuree(TimeSpan duree, TimeSpan maintenant)
    {
        var debutFenetre = maintenant - duree;
        lock (_verrou)
            return _entrees.Where(e => e.Fin > debutFenetre)
                           .OrderBy(e => e.Debut)
                           .Select(e => e.Chemin)
                           .ToList();
    }
}
```

- [ ] **Step 4: Vérifier** — Run: `dotnet test --filter SegmentRingTests` — Expected: PASS (3 tests).

- [ ] **Step 5: Commit** — `git add -A && git commit -m "Task 3: anneau de segments"`

---

### Task 4: Énumération des écrans + item de capture WGC

**Files:**
- Create: `src/Replayo/Capture/MonitorInfo.cs`, `src/Replayo/Capture/CaptureItemFactory.cs`, `src/Replayo/Capture/D3DHelper.cs`

**Interfaces:**
- Produces: `MonitorInfo.EnumererEcrans()` → `List<MonitorInfo>` (`IntPtr Handle`, `string Nom`, `int Largeur`, `int Hauteur`, `bool Principal`, `int Index`) ; `CaptureItemFactory.DepuisEcran(IntPtr hmon)` → `GraphicsCaptureItem` ; `D3DHelper.CreerDeviceWinRT()` → `IDirect3DDevice`.
- Consumes: rien.

- [ ] **Step 1: Implémenter (pas de test unitaire possible — dépend du matériel ; vérification par le runner en Task 8)**

`src/Replayo/Capture/MonitorInfo.cs` :

```csharp
using System.Runtime.InteropServices;

namespace Replayo.Capture;

/// Un écran physique détecté (EnumDisplayMonitors).
public sealed record MonitorInfo(IntPtr Handle, string Nom, int Largeur, int Hauteur, bool Principal, int Index)
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX info);

    public static List<MonitorInfo> EnumererEcrans()
    {
        var liste = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT r, IntPtr _) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            GetMonitorInfoW(h, ref mi);
            liste.Add(new(h, mi.szDevice, mi.rcMonitor.R - mi.rcMonitor.L, mi.rcMonitor.B - mi.rcMonitor.T,
                          (mi.dwFlags & 1) != 0, liste.Count));
            return true;
        }, IntPtr.Zero);
        return liste;
    }
}
```

`src/Replayo/Capture/CaptureItemFactory.cs` :

```csharp
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace Replayo.Capture;

/// Crée un GraphicsCaptureItem à partir d'un HMONITOR (interop COM documenté par Microsoft).
public static class CaptureItemFactory
{
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, ref Guid iid);
        IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
    }

    private static Guid _iidItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760"); // IID IGraphicsCaptureItem

    public static GraphicsCaptureItem DepuisEcran(IntPtr hmon)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var abi = interop.CreateForMonitor(hmon, ref _iidItem);
        return GraphicsCaptureItem.FromAbi(abi);
    }
}
```

`src/Replayo/Capture/D3DHelper.cs` :

```csharp
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Replayo.Capture;

/// Crée le device Direct3D11 partagé par la capture et l'encodeur (zéro-copie GPU).
public static class D3DHelper
{
    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr module, uint flags,
        IntPtr featureLevels, uint numLevels, uint sdkVersion, out IntPtr device, out IntPtr featureLevel, out IntPtr context);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static IDirect3DDevice CreerDeviceWinRT()
    {
        // D3D_DRIVER_TYPE_HARDWARE = 1 ; D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20 ; SDK = 7
        int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7,
                                   out var d3dDevice, out _, out var context);
        Marshal.ThrowExceptionForHR(hr);
        Marshal.Release(context);
        hr = CreateDirect3D11DeviceFromDXGIDevice(d3dDevice, out var winrtDevice);
        Marshal.Release(d3dDevice);
        Marshal.ThrowExceptionForHR(hr);
        var device = MarshalInterface<IDirect3DDevice>.FromAbi(winrtDevice);
        Marshal.Release(winrtDevice);
        return device;
    }
}
```

- [ ] **Step 2: Compiler** — Run: `dotnet build` — Expected: `Build succeeded`.

- [ ] **Step 3: Commit** — `git add -A && git commit -m "Task 4: enumeration ecrans + interop WGC/D3D"`

---

### Task 5: Moteur de capture (`CaptureEngine` + `FrameQueue`)

**Files:**
- Create: `src/Replayo/Capture/FrameQueue.cs`, `src/Replayo/Capture/CaptureEngine.cs`

**Interfaces:**
- Consumes: `CaptureItemFactory.DepuisEcran`, `D3DHelper.CreerDeviceWinRT` (Task 4).
- Produces: `FrameQueue` : `bool AjouterOuJeter(FrameCapturee f)` (borne à 90 frames — l'encodeur en retard fait jeter les plus vieilles, jamais de RAM infinie), `Task<FrameCapturee?> PrendreAsync(CancellationToken)` ; `record FrameCapturee(IDirect3DSurface Surface, TimeSpan Horodatage)` ; `CaptureEngine(MonitorInfo ecran)` : `Demarrer()`, `Arreter()`, expose `FrameQueue Frames` et `event Action? CaptureInterrompue` (écran débranché → l'appelant redémarre).

- [ ] **Step 1: Implémenter**

`src/Replayo/Capture/FrameQueue.cs` :

```csharp
using System.Threading.Channels;
using Windows.Graphics.DirectX.Direct3D11;

namespace Replayo.Capture;

public sealed record FrameCapturee(IDirect3DSurface Surface, TimeSpan Horodatage);

/// File bornée entre la capture et l'encodeur. Si l'encodeur ne suit pas,
/// on jette la frame LA PLUS ANCIENNE (le direct prime sur l'historique).
public sealed class FrameQueue
{
    private readonly Channel<FrameCapturee> _canal = Channel.CreateBounded<FrameCapturee>(
        new BoundedChannelOptions(90) { FullMode = BoundedChannelFullMode.DropOldest });

    public bool AjouterOuJeter(FrameCapturee f) => _canal.Writer.TryWrite(f);
    public ValueTask<FrameCapturee> PrendreAsync(CancellationToken ct) => _canal.Reader.ReadAsync(ct);
    public void Terminer() => _canal.Writer.TryComplete();
}
```

`src/Replayo/Capture/CaptureEngine.cs` :

```csharp
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Replayo.Capture;

/// Capture continue d'un écran via Windows Graphics Capture.
/// Les frames restent des surfaces GPU (zéro-copie) jusqu'à l'encodeur.
public sealed class CaptureEngine(MonitorInfo ecran) : IDisposable
{
    private IDirect3DDevice? _device;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private TimeSpan _origine = TimeSpan.MinValue;

    public FrameQueue Frames { get; } = new();
    public SizeInt32 Taille { get; private set; }
    public event Action? CaptureInterrompue;

    public void Demarrer()
    {
        _device = D3DHelper.CreerDeviceWinRT();
        var item = CaptureItemFactory.DepuisEcran(ecran.Handle);
        Taille = item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        _pool.FrameArrived += SurFrame;
        item.Closed += (_, _) => CaptureInterrompue?.Invoke(); // écran débranché
        _session = _pool.CreateCaptureSession(item);
        _session.IsCursorCaptureEnabled = true;
        _session.StartCapture();
    }

    private void SurFrame(Direct3D11CaptureFramePool pool, object? _)
    {
        using var frame = pool.TryGetNextFrame();
        if (frame is null) return;
        if (_origine == TimeSpan.MinValue) _origine = frame.SystemRelativeTime;
        // La surface est référencée par la file ; l'encodeur la consomme puis la libère.
        Frames.AjouterOuJeter(new(frame.Surface, frame.SystemRelativeTime - _origine));
    }

    public void Arreter()
    {
        _session?.Dispose(); _session = null;
        if (_pool is not null) { _pool.FrameArrived -= SurFrame; _pool.Dispose(); _pool = null; }
        Frames.Terminer();
    }

    public void Dispose() => Arreter();
}
```

- [ ] **Step 2: Compiler** — Run: `dotnet build` — Expected: `Build succeeded`.

- [ ] **Step 3: Commit** — `git add -A && git commit -m "Task 5: moteur de capture WGC + file de frames"`

---

### Task 6: Audio (`AudioEngine`) — loopback système + micro, mix PCM

**Files:**
- Create: `src/Replayo/Audio/AudioEngine.cs`

**Interfaces:**
- Consumes: config (`AudioSysteme`, `AudioMicro`).
- Produces: `AudioEngine(bool systeme, bool micro)` : `Demarrer()`, `Arreter()`, `bool Actif` ; `byte[]? LirePcm(TimeSpan jusquA)` — PCM 16 bits stéréo 48 kHz accumulé depuis le dernier appel (l'encodeur tire à son rythme) ; `static AudioEngine? CreerSiActive(ReplayoConfig cfg)`.

- [ ] **Step 1: Implémenter** — `src/Replayo/Audio/AudioEngine.cs`

```csharp
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Replayo.Core;

namespace Replayo.Audio;

/// Capture le son système (WASAPI loopback) et/ou le micro, mixe le tout
/// en PCM 16 bits stéréo 48 kHz consommé par l'encodeur segment par segment.
public sealed class AudioEngine : IDisposable
{
    private static readonly WaveFormat FormatCible = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    private readonly List<IWaveIn> _captures = new();
    private readonly MixingSampleProvider _mixeur = new(FormatCible) { ReadFully = true };
    private readonly object _verrou = new();

    public bool Actif => _captures.Count > 0;

    public static AudioEngine? CreerSiActive(ReplayoConfig cfg)
        => cfg.AudioSysteme || cfg.AudioMicro ? new AudioEngine(cfg.AudioSysteme, cfg.AudioMicro) : null;

    public AudioEngine(bool systeme, bool micro)
    {
        if (systeme) Brancher(new WasapiLoopbackCapture());
        if (micro)
        {
            try { Brancher(new WasapiCapture()); } // périphérique d'entrée par défaut
            catch { /* pas de micro branché : on continue sans, jamais de crash */ }
        }
    }

    private void Brancher(IWaveIn capture)
    {
        var tampon = new BufferedWaveProvider(capture.WaveFormat) { DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromSeconds(2) };
        capture.DataAvailable += (_, e) => tampon.AddSamples(e.Buffer, 0, e.BytesRecorded);
        ISampleProvider source = tampon.ToSampleProvider();
        if (capture.WaveFormat.SampleRate != 48000)
            source = new WdlResamplingSampleProvider(source, 48000);
        if (source.WaveFormat.Channels == 1)
            source = new MonoToStereoSampleProvider(source);
        lock (_verrou) _mixeur.AddMixerInput(source);
        _captures.Add(capture);
    }

    public void Demarrer() { foreach (var c in _captures) c.StartRecording(); }
    public void Arreter() { foreach (var c in _captures) c.StopRecording(); }

    /// Lit tout le PCM disponible, converti en 16 bits. Appelé par l'encodeur.
    public byte[] LirePcmDisponible(int maxOctets = 48000 * 2 * 2) // ~500 ms
    {
        var floats = new float[maxOctets / 2];
        int lus;
        lock (_verrou) lus = _mixeur.Read(floats, 0, floats.Length);
        var pcm = new byte[lus * 2];
        for (int i = 0; i < lus; i++)
        {
            var v = (short)Math.Clamp(floats[i] * 32767f, short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)v; pcm[i * 2 + 1] = (byte)(v >> 8);
        }
        return pcm;
    }

    public void Dispose() { Arreter(); foreach (var c in _captures) c.Dispose(); }
}
```

- [ ] **Step 2: Compiler** — Run: `dotnet build` — Expected: `Build succeeded`.

- [ ] **Step 3: Commit** — `git add -A && git commit -m "Task 6: moteur audio WASAPI (systeme + micro, mix 48 kHz)"`

---

### Task 7: Encodeur par segments (`SegmentEncoder`)

**Files:**
- Create: `src/Replayo/Encoding/SegmentEncoder.cs`

**Interfaces:**
- Consumes: `FrameQueue`/`FrameCapturee` (Task 5), `AudioEngine.LirePcmDisponible` (Task 6), `SegmentRing` (Task 3), `QualityPreset` (Task 2), `CaptureEngine.Taille` (Task 5).
- Produces: `SegmentEncoder(FrameQueue frames, AudioEngine? audio, SegmentRing ring, QualityPreset preset, Windows.Graphics.SizeInt32 tailleEcran)` : `Task BoucleEncodageAsync(CancellationToken ct)` — encode en continu des segments de 10 s et les enregistre dans l'anneau ; `bool EncodageMateriel` (résultat de la 1re session, pour l'avertissement) ; `TimeSpan HorlogeCapture` (horodatage de la dernière frame encodée — sert d'« horloge » au ClipService).

**Principe (à conserver tel quel dans le code) :** une session `MediaStreamSource` + `MediaTranscoder` par segment de 10 s. Les frames arrivent par la `FrameQueue` bornée : pendant le bref redémarrage inter-segment, elles s'y accumulent puis sont drainées — aucune frame perdue, timestamps continus (rebasés à 0 dans chaque fichier). Chaque segment démarre sur une keyframe (nouvelle session), ce qui rend l'assemblage `-c copy` propre.

- [ ] **Step 1: Implémenter** — `src/Replayo/Encoding/SegmentEncoder.cs`

```csharp
using Replayo.Audio;
using Replayo.Buffer;
using Replayo.Capture;
using Replayo.Core;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage.Streams;

namespace Replayo.Encoding;

/// Encode le flux de frames GPU en segments MP4 de ~10 s (H.264 + AAC),
/// avec accélération matérielle (NVENC/AMF/QuickSync via Media Foundation).
public sealed class SegmentEncoder(FrameQueue frames, AudioEngine? audio, SegmentRing ring,
                                   QualityPreset preset, SizeInt32 tailleEcran)
{
    private static readonly TimeSpan DureeSegment = TimeSpan.FromSeconds(10);
    public bool EncodageMateriel { get; private set; } = true;
    public TimeSpan HorlogeCapture { get; private set; }

    public async Task BoucleEncodageAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await EncoderUnSegmentAsync(ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task EncoderUnSegmentAsync(CancellationToken ct)
    {
        int largeur = preset.Largeur ?? tailleEcran.Width;
        int hauteur = preset.Hauteur ?? tailleEcran.Height;

        // --- Descripteurs d'entrée : vidéo BGRA8 non compressée + PCM 16 bits ---
        var propsVideo = VideoEncodingProperties.CreateUncompressed(
            MediaEncodingSubtypes.Bgra8, (uint)tailleEcran.Width, (uint)tailleEcran.Height);
        var descVideo = new VideoStreamDescriptor(propsVideo);

        AudioStreamDescriptor? descAudio = null;
        if (audio is { Actif: true })
            descAudio = new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(48000, 2, 16));

        var mss = descAudio is null ? new MediaStreamSource(descVideo)
                                    : new MediaStreamSource(descVideo, descAudio);
        mss.BufferTime = TimeSpan.Zero; // temps réel, pas de mise en tampon interne

        TimeSpan? origineSegment = null;   // premier timestamp du segment (rebasage à 0)
        TimeSpan originePourRing = default; // timestamp global du début (pour l'anneau)
        TimeSpan horlogeAudio = default;
        bool fini = false;

        mss.SampleRequested += (_, e) =>
        {
            var deferral = e.Request.GetDeferral();
            try
            {
                if (e.Request.StreamDescriptor is VideoStreamDescriptor)
                {
                    if (fini) { e.Request.Sample = null; return; }
                    var frame = frames.PrendreAsync(ct).AsTask().GetAwaiter().GetResult();
                    origineSegment ??= frame.Horodatage;
                    if (origineSegment == frame.Horodatage) originePourRing = frame.Horodatage;
                    var tsLocal = frame.Horodatage - origineSegment.Value;
                    if (tsLocal >= DureeSegment) { fini = true; e.Request.Sample = null; return; }
                    HorlogeCapture = frame.Horodatage;
                    e.Request.Sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, tsLocal);
                }
                else if (audio is not null)
                {
                    if (fini) { e.Request.Sample = null; return; }
                    var pcm = audio.LirePcmDisponible();
                    if (pcm.Length == 0) pcm = new byte[9600]; // 50 ms de silence : ne jamais bloquer le mux
                    var sample = MediaStreamSample.CreateFromBuffer(pcm.AsBuffer(), horlogeAudio);
                    sample.Duration = TimeSpan.FromSeconds(pcm.Length / (48000.0 * 2 * 2));
                    horlogeAudio += sample.Duration;
                    e.Request.Sample = sample;
                }
            }
            catch (OperationCanceledException) { e.Request.Sample = null; }
            finally { deferral.Complete(); }
        };

        // --- Profil de sortie : H.264 + AAC au débit du préréglage ---
        var profil = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profil.Video!.Width = (uint)largeur;
        profil.Video.Height = (uint)hauteur;
        profil.Video.Bitrate = preset.DebitBitsParSeconde;
        profil.Video.FrameRate.Numerator = (uint)preset.Fps;
        profil.Video.FrameRate.Denominator = 1;
        if (descAudio is null) profil.Audio = null;

        var chemin = ring.ProchainCheminSegment();
        using var fichier = new FileStream(chemin, FileMode.Create, FileAccess.ReadWrite);
        using var flux = fichier.AsRandomAccessStream();

        var transcodeur = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prep = await transcodeur.PrepareMediaStreamSourceTranscodeAsync(mss, flux, profil);
        if (!prep.CanTranscode)
        {
            // Repli logiciel : on retente sans accélération matérielle (avertissement runner).
            EncodageMateriel = false;
            transcodeur.HardwareAccelerationEnabled = false;
            prep = await transcodeur.PrepareMediaStreamSourceTranscodeAsync(mss, flux, profil);
            if (!prep.CanTranscode) throw new InvalidOperationException("Aucun encodeur H.264 disponible.");
        }
        await prep.TranscodeAsync().AsTask(ct);

        ring.Ajouter(chemin, originePourRing, HorlogeCapture);
    }
}
```

- [ ] **Step 2: Compiler** — Run: `dotnet build` — Expected: `Build succeeded`.

- [ ] **Step 3: Commit** — `git add -A && git commit -m "Task 7: encodeur par segments (MediaTranscoder materiel)"`

---

### Task 8: Clip (`ClipService` + `ForegroundAppTracker` + son)

**Files:**
- Create: `src/Replayo/Clip/ClipService.cs`, `src/Replayo/Clip/ForegroundAppTracker.cs`, `assets/clip.wav` (petit « pop » discret ≤ 1 s — générer avec ffmpeg, commande au Step 2)
- Test: `tests/Replayo.Tests/ClipNamingTests.cs`

**Interfaces:**
- Consumes: `SegmentRing.SegmentsPourDuree` (Task 3), `SegmentEncoder.HorlogeCapture` (Task 7), `AppPaths.FfmpegExe` (Task 2).
- Produces: `ForegroundAppTracker.NomApplication()` → `string` (nom du process au premier plan, `"Bureau"` si indétectable) ; `ClipService(ReplayoConfig cfg)` : `Task<string?> CreerClipAsync(SegmentRing ring, TimeSpan horloge, string? suffixe = null)` (null si échec ; `suffixe` = `"ecran2"` en multi-écrans) ; static pur `string ConstruireCheminSortie(string racine, string app, DateTime quand, string format, string? suffixe)` (testable).

- [ ] **Step 1: Test qui échoue** — `tests/Replayo.Tests/ClipNamingTests.cs`

```csharp
using Replayo.Clip;
using Xunit;

public class ClipNamingTests
{
    [Fact]
    public void ConstruireCheminSortie_RangeParAppliPuisMois()
    {
        var chemin = ClipService.ConstruireCheminSortie(
            @"C:\Videos\Replayo", "RocketLeague", new DateTime(2026, 7, 15, 18, 32, 5), "mp4", null);
        Assert.Equal(@"C:\Videos\Replayo\RocketLeague\2026-07\Replayo_RocketLeague_2026-07-15_18h32m05.mp4", chemin);
    }

    [Fact]
    public void ConstruireCheminSortie_NettoieLesCaracteresInterdits()
    {
        var chemin = ClipService.ConstruireCheminSortie(
            @"C:\V", @"App: <Test>", new DateTime(2026, 1, 2, 3, 4, 5), "mkv", "ecran2");
        Assert.DoesNotContain(':', Path.GetFileName(chemin));
        Assert.DoesNotContain('<', chemin.Substring(3));
        Assert.EndsWith("_ecran2.mkv", chemin);
    }
}
```

Run: `dotnet test --filter ClipNamingTests` — Expected: FAIL.

- [ ] **Step 2: Implémenter**

`src/Replayo/Clip/ForegroundAppTracker.cs` :

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Replayo.Clip;

/// Nom de l'application au premier plan au moment du clip (pour le rangement).
public static class ForegroundAppTracker
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    public static string NomApplication()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return "Bureau";
            GetWindowThreadProcessId(h, out var pid);
            var nom = Process.GetProcessById((int)pid).ProcessName;
            return string.IsNullOrWhiteSpace(nom) || nom is "explorer" ? "Bureau" : nom;
        }
        catch { return "Bureau"; }
    }
}
```

`src/Replayo/Clip/ClipService.cs` :

```csharp
using System.Diagnostics;
using System.Media;
using Replayo.Buffer;
using Replayo.Core;

namespace Replayo.Clip;

/// Assemble les N dernières secondes en un fichier final SANS ré-encodage (ffmpeg -c copy),
/// range le fichier (Appli/AAAA-MM) et joue le son de confirmation.
public sealed class ClipService(ReplayoConfig cfg)
{
    public static string ConstruireCheminSortie(string racine, string app, DateTime quand, string format, string? suffixe)
    {
        var appPropre = string.Join("", app.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (appPropre.Length == 0) appPropre = "Bureau";
        var nom = $"Replayo_{appPropre}_{quand:yyyy-MM-dd_HH\\hmm\\mss}{(suffixe is null ? "" : "_" + suffixe)}.{format}";
        return Path.Combine(racine, appPropre, $"{quand:yyyy-MM}", nom);
    }

    public async Task<string?> CreerClipAsync(SegmentRing ring, TimeSpan horloge, string? suffixe = null)
    {
        var segments = ring.SegmentsPourDuree(TimeSpan.FromSeconds(cfg.DureeBufferSecondes), horloge);
        if (segments.Count == 0) return null;

        var racine = string.IsNullOrWhiteSpace(cfg.DossierSortie) ? AppPaths.DossierSortieDefaut : cfg.DossierSortie;
        var sortie = ConstruireCheminSortie(racine, ForegroundAppTracker.NomApplication(), DateTime.Now, cfg.FormatSortie, suffixe);
        Directory.CreateDirectory(Path.GetDirectoryName(sortie)!);

        // Liste concat ffmpeg (échappement : apostrophes doublées, syntaxe du démuxeur concat).
        var liste = Path.Combine(Path.GetTempPath(), $"replayo_concat_{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(liste, segments.Select(s => $"file '{s.Replace("'", "'\\''")}'"));

        var psi = new ProcessStartInfo(AppPaths.FfmpegExe,
            $"-hide_banner -loglevel error -f concat -safe 0 -i \"{liste}\" -c copy -y \"{sortie}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };

        using var proc = Process.Start(psi)!;
        var erreurs = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        File.Delete(liste);

        if (proc.ExitCode != 0) { Console.Error.WriteLine($"[clip] ffmpeg: {erreurs}"); return null; }

        JouerSon();
        return sortie;
    }

    private static void JouerSon()
    {
        var wav = Path.Combine(AppContext.BaseDirectory, "assets", "clip.wav");
        if (File.Exists(wav)) { try { new SoundPlayer(wav).Play(); } catch { /* jamais bloquant */ } }
    }
}
```

Générer le son discret (une fois, versionné) :

```powershell
tools\ffmpeg\ffmpeg.exe -f lavfi -i "sine=frequency=880:duration=0.12,afade=t=out:st=0.06:d=0.06" -ar 44100 assets\clip.wav
```

- [ ] **Step 3: Vérifier** — Run: `dotnet test --filter ClipNamingTests` — Expected: PASS (2 tests).

- [ ] **Step 4: Commit** — `git add -A && git commit -m "Task 8: service de clip (ffmpeg -c copy), rangement, son"`

---

### Task 9: Raccourci global (`HotkeyManager`)

**Files:**
- Create: `src/Replayo/Input/HotkeyManager.cs`

**Interfaces:**
- Produces: `HotkeyManager` : `bool Enregistrer(uint modificateurs, uint toucheVk, Action rappel)` (false = conflit), `Desenregistrer()` ; constantes `HotkeyManager.MOD_ALT = 0x1`, `VK_F10 = 0x79`. Fenêtre message-only + boucle sur thread dédié (fonctionne sans UI).

- [ ] **Step 1: Implémenter** — `src/Replayo/Input/HotkeyManager.cs`

```csharp
using System.Runtime.InteropServices;

namespace Replayo.Input;

/// Raccourci clavier global via RegisterHotKey sur un thread à boucle de messages dédié.
public sealed class HotkeyManager : IDisposable
{
    public const uint MOD_ALT = 0x0001;
    public const uint VK_F10 = 0x79;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }

    private const uint WM_HOTKEY = 0x0312, WM_QUIT = 0x0012;
    private Thread? _thread;
    private uint _threadId;
    private volatile bool _ok;

    public bool Enregistrer(uint modificateurs, uint toucheVk, Action rappel)
    {
        using var pret = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _ok = RegisterHotKey(IntPtr.Zero, 1, modificateurs, toucheVk);
            pret.Set();
            if (!_ok) return;
            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
                if (msg.message == WM_HOTKEY) rappel();
            UnregisterHotKey(IntPtr.Zero, 1);
        }) { IsBackground = true, Name = "Replayo-Hotkey" };
        _thread.Start();
        pret.Wait();
        return _ok;
    }

    public void Desenregistrer()
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose() => Desenregistrer();
}
```

- [ ] **Step 2: Compiler** — Run: `dotnet build` — Expected: `Build succeeded`.

- [ ] **Step 3: Commit** — `git add -A && git commit -m "Task 9: raccourci clavier global"`

---

### Task 10: Runner minimal (assemblage) + test de bout en bout

**Files:**
- Modify: `src/Replayo/Program.cs` (remplace le contenu du template)

**Interfaces:**
- Consumes: TOUT ce qui précède. C'est la composition racine du Milestone A.

- [ ] **Step 1: Implémenter** — `src/Replayo/Program.cs`

```csharp
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
var pipelines = new List<(CaptureEngine capture, SegmentEncoder enc, SegmentRing ring, Task boucle, string? suffixe)>();
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
if (pipelines.Any(p => !p.enc.EncodageMateriel))
    Console.WriteLine("⚠ Encodeur matériel indisponible : repli logiciel (CPU accru).");

var clips = new ClipService(config);
using var hotkey = new HotkeyManager();
bool ok = hotkey.Enregistrer(HotkeyManager.MOD_ALT, HotkeyManager.VK_F10, () =>
{
    _ = Task.Run(async () =>
    {
        foreach (var p in pipelines)
        {
            var chemin = await clips.CreerClipAsync(p.ring, p.enc.HorlogeCapture, p.suffixe);
            Console.WriteLine(chemin is null ? "[clip] échec (voir erreurs)" : $"[clip] ✓ {chemin}");
        }
    });
});
Console.WriteLine(ok ? "Alt+F10 → clip. Ctrl+C → quitter." : "⚠ Alt+F10 déjà utilisé par une autre application.");

try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (OperationCanceledException) { }
foreach (var p in pipelines) p.capture.Arreter();
audio?.Dispose();
return 0;
```

- [ ] **Step 2: Test de bout en bout manuel (critère d'acceptation clé)**

```powershell
dotnet run --project src/Replayo
# 1. Laisser tourner 40 s (au moins 3 segments écrits dans %LocalAppData%\Replayo\buffer\ecranN)
# 2. Alt+F10 → vérifier : fichier créé dans Vidéos\Replayo\<App>\2026-07\, son joué
# 3. Lire le clip : vidéo fluide, curseur visible, audio présent, durée ≈ min(buffer, temps écoulé)
# 4. Ctrl+C → au relancement, le buffer est purgé
```

Expected: les 4 points passent. Sinon : corriger AVANT de committer (c'est ici que l'interop se débogue sur la vraie machine).

- [ ] **Step 3: Commit** — `git add -A && git commit -m "Task 10: runner milestone A — capture continue + clip Alt+F10"`

---

### Task 11: Mesure de performance (critère d'acceptation)

**Files:**
- Create: `scripts/mesure-perf.ps1`
- Modify: `README.md` (section « Performance mesurée »)

**Interfaces:**
- Consumes: le runner (Task 10) en cours d'exécution.

- [ ] **Step 1: Écrire `scripts/mesure-perf.ps1`**

```powershell
# Mesure CPU/RAM de Replayo sur 60 s (à lancer pendant une capture active).
$proc = Get-Process Replayo -ErrorAction Stop
$echantillons = @()
for ($i = 0; $i -lt 60; $i++) {
    $avant = $proc.TotalProcessorTime
    Start-Sleep -Seconds 1
    $proc.Refresh()
    $cpuPct = ($proc.TotalProcessorTime - $avant).TotalMilliseconds / 10 / [Environment]::ProcessorCount
    $echantillons += [pscustomobject]@{ CpuPct = [math]::Round($cpuPct, 2); RamMo = [math]::Round($proc.WorkingSet64 / 1MB) }
}
$cpu = ($echantillons | Measure-Object CpuPct -Average).Average
$ram = ($echantillons | Measure-Object RamMo -Maximum).Maximum
"CPU moyen : $([math]::Round($cpu,2)) % — RAM max : $ram Mo"
"Objectifs : CPU < 5 % (matériel), RAM < 200 Mo hors segment courant"
```

- [ ] **Step 2: Mesurer et documenter**

Run: `dotnet run --project src/Replayo -c Release` (autre terminal) puis `powershell scripts/mesure-perf.ps1`.
Expected: CPU < 5 % avec encodeur matériel. Copier les chiffres réels dans `README.md`, avec la machine de test (GPU) et le préréglage.

- [ ] **Step 3: Commit** — `git add -A && git commit -m "Task 11: mesure de perf scriptee + resultats README"`

---

## Self-review (fait à l'écriture du plan)

- **Couverture spec (périmètre Plan A)** : capture continue ✓ (T5/T7), perf matérielle + mesure ✓ (T7/T11), buffer 15 s–20 min ✓ (T2/T3), clip raccourci ✓ (T9/T10), son ✓ (T8), rangement ✓ (T8), multi-écrans un fichier par écran ✓ (T10), MP4/MKV ✓ (T8 via config). — Onboarding, tray, autostart, renommage manuel, licence : **Plans B et C** (voulu).
- **Placeholders** : aucun TBD/TODO ; chaque étape code contient le code.
- **Cohérence des types** : `FrameQueue.PrendreAsync` rend `ValueTask<FrameCapturee>` (T5) — consommé en `.AsTask().GetAwaiter().GetResult()` (T7) ✓ ; `SegmentRing` signatures identiques T3/T7/T8 ✓ ; `HorlogeCapture` produit T7, consommé T10 ✓.
- **Risque connu assumé** : l'interop WGC/MediaTranscoder se valide sur la machine réelle en Task 10 Step 2 — c'est le point de débogage prévu du plan.

