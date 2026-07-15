# Replayo — Plan C : licence Lemon Squeezy (Implementation Plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rendre Replayo inutilisable sans licence valide : activation par clé Lemon Squeezy au premier lancement, revalidation silencieuse toutes les 24 h, grâce hors-ligne de 72 h, verrouillage si la licence expire/est résiliée/désactivée.

**Architecture:** Un `LicenseService` (machine à états : NonActivée → Active ↔ Grâce → Verrouillée) au-dessus d'un `LicenseApiClient` (API publique de licence Lemon Squeezy, aucun secret embarqué) et d'un `LicenseStore` (clé + instance_id + horodatage chiffrés DPAPI via interop crypt32.dll — pas de dépendance nouvelle). L'app vérifie la licence AVANT de démarrer la capture ; l'écran d'activation bloque tout tant que la licence n'est pas valide.

**Tech Stack:** identique aux Plans A/B. **Aucune dépendance NuGet nouvelle** (DPAPI en P/Invoke, HTTP via `HttpClient` in-box).

## Global Constraints

- Reprend toutes les contraintes des Plans A/B.
- **Aucun secret dans le code** : les endpoints de licence Lemon Squeezy (`/v1/licenses/activate`, `/v1/licenses/validate`, `/v1/licenses/deactivate`) sont publics et ne demandent pas la clé API du vendeur. Rien à embarquer.
- Deux produits Lemon Squeezy (abonnement 2,99 €/mois + lifetime 11,99 €) : la même clé fonctionne pour les deux ; le champ `license_key.status` (`active`/`expired`/`disabled`) et l'expiration gérée côté LS distinguent les cas. L'app ne code pas le prix.
- Revalidation : **24 h**. Grâce hors-ligne : **72 h** depuis la dernière validation réussie. Au-delà sans réseau → verrouillage.
- Clé + `instance_id` + `derniereValidationUtc` + `statut` chiffrés **DPAPI portée utilisateur** dans `%AppData%\Replayo\license.dat`.
- **Dépendance externe = STOP** (aucune prévue ici). Le « serveur de licence » est Lemon Squeezy (validé) — ne PAS coder de serveur maison.
- ⚠️ **Test E2E complet impossible sans le compte Lemon Squeezy de l'utilisateur** : les Tasks 1-3 sont testables hors-ligne (DPAPI, machine à états avec API simulée). La Task 5 (activation réelle) nécessite une vraie clé de test → **point d'arrêt : demander la clé à l'utilisateur** avant de dérouler la recette en ligne.

---

### Task 1: Stockage chiffré de la licence (`LicenseStore` + DPAPI interop) — TDD

**Files:**
- Create: `src/Replayo/Licence/Dpapi.cs`, `src/Replayo/Licence/LicenseRecord.cs`, `src/Replayo/Licence/LicenseStore.cs`
- Test: `tests/Replayo.Tests/LicenseStoreTests.cs`

**Interfaces:**
- Produces: `enum LicenseStatut { NonActivee, Active, Grace, Verrouillee }` ; `record LicenseRecord(string Cle, string InstanceId, DateTime DerniereValidationUtc, string StatutLs)` ; `Dpapi.Proteger(byte[])`/`Deproteger(byte[])` (portée utilisateur) ; `LicenseStore(string? dossier=null)` : `LicenseRecord? Charger()` (null si absent/corrompu), `Enregistrer(LicenseRecord)`, `Effacer()`.

- [ ] **Step 1: Test qui échoue** — `tests/Replayo.Tests/LicenseStoreTests.cs`

```csharp
using Replayo.Licence;
using Xunit;

public class LicenseStoreTests
{
    [Fact]
    public void Dpapi_RoundTrip_RestitueLesOctets()
    {
        var clair = System.Text.Encoding.UTF8.GetBytes("clé-secrète-123");
        var chiffre = Dpapi.Proteger(clair);
        Assert.NotEqual(clair, chiffre); // réellement chiffré
        Assert.Equal(clair, Dpapi.Deproteger(chiffre));
    }

    [Fact]
    public void EnregistrerPuisCharger_ConserveLenregistrement()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var store = new LicenseStore(dir);
        var rec = new LicenseRecord("ABC-123", "inst-1", new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc), "active");
        store.Enregistrer(rec);
        var relu = new LicenseStore(dir).Charger();
        Assert.NotNull(relu);
        Assert.Equal("ABC-123", relu!.Cle);
        Assert.Equal("inst-1", relu.InstanceId);
        Assert.Equal("active", relu.StatutLs);
    }

    [Fact]
    public void Charger_SansFichier_RendNull()
        => Assert.Null(new LicenseStore(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())).Charger());

    [Fact]
    public void Effacer_SupprimeLenregistrement()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var store = new LicenseStore(dir);
        store.Enregistrer(new LicenseRecord("K", "I", DateTime.UtcNow, "active"));
        store.Effacer();
        Assert.Null(store.Charger());
    }
}
```

- [ ] **Step 2: Vérifier l'échec** — `dotnet test --filter LicenseStoreTests` → FAIL.

- [ ] **Step 3: Implémenter**

`src/Replayo/Licence/Dpapi.cs` :

```csharp
using System.Runtime.InteropServices;

namespace Replayo.Licence;

/// Chiffrement DPAPI portée utilisateur via crypt32.dll (aucune dépendance NuGet).
/// Les données ne sont déchiffrables que par le même compte Windows sur la même machine.
public static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB pIn, string? desc, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, ref DATA_BLOB pOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pIn, IntPtr desc, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, ref DATA_BLOB pOut);

    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr h);

    private const uint CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    private static byte[] Transformer(byte[] donnees, bool proteger)
    {
        var entree = new DATA_BLOB();
        var sortie = new DATA_BLOB();
        var pin = GCHandle.Alloc(donnees, GCHandleType.Pinned);
        try
        {
            entree.cbData = donnees.Length;
            entree.pbData = pin.AddrOfPinnedObject();
            bool ok = proteger
                ? CryptProtectData(ref entree, "Replayo", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref sortie)
                : CryptUnprotectData(ref entree, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref sortie);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var res = new byte[sortie.cbData];
            Marshal.Copy(sortie.pbData, res, 0, sortie.cbData);
            return res;
        }
        finally
        {
            if (pin.IsAllocated) pin.Free();
            if (sortie.pbData != IntPtr.Zero) LocalFree(sortie.pbData);
        }
    }

    public static byte[] Proteger(byte[] clair) => Transformer(clair, true);
    public static byte[] Deproteger(byte[] chiffre) => Transformer(chiffre, false);
}
```

`src/Replayo/Licence/LicenseRecord.cs` :

```csharp
namespace Replayo.Licence;

public enum LicenseStatut { NonActivee, Active, Grace, Verrouillee }

/// Licence persistée (chiffrée). StatutLs = statut brut Lemon Squeezy
/// ("active" | "expired" | "disabled").
public sealed record LicenseRecord(string Cle, string InstanceId, DateTime DerniereValidationUtc, string StatutLs);
```

`src/Replayo/Licence/LicenseStore.cs` :

```csharp
using System.Text;
using System.Text.Json;
using Replayo.Core;

namespace Replayo.Licence;

/// Lecture/écriture de la licence chiffrée DPAPI (%AppData%\Replayo\license.dat).
public sealed class LicenseStore(string? dossier = null)
{
    private readonly string _chemin = Path.Combine(dossier ?? AppPaths.DossierConfig, "license.dat");

    public LicenseRecord? Charger()
    {
        if (!File.Exists(_chemin)) return null;
        try
        {
            var clair = Dpapi.Deproteger(File.ReadAllBytes(_chemin));
            return JsonSerializer.Deserialize<LicenseRecord>(Encoding.UTF8.GetString(clair));
        }
        catch { return null; } // corrompu / autre machine → considéré non activé
    }

    public void Enregistrer(LicenseRecord rec)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_chemin)!);
        var clair = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rec));
        File.WriteAllBytes(_chemin, Dpapi.Proteger(clair));
    }

    public void Effacer() { if (File.Exists(_chemin)) File.Delete(_chemin); }
}
```

- [ ] **Step 4: Vérifier** — `dotnet test --filter LicenseStoreTests` → PASS (4 tests). **Step 5: Commit** — `git commit -m "Plan C Task 1: stockage licence chiffre DPAPI"`

---

### Task 2: Client API Lemon Squeezy (`LicenseApiClient` + `ILicenseApi`)

**Files:**
- Create: `src/Replayo/Licence/ILicenseApi.cs`, `src/Replayo/Licence/LicenseApiClient.cs`

**Interfaces:**
- Produces: `record LicenseReponse(bool Ok, string Statut, string? InstanceId, string? Erreur)` ; `interface ILicenseApi { Task<LicenseReponse> ActiverAsync(string cle, string nomInstance); Task<LicenseReponse> ValiderAsync(string cle, string instanceId); }` ; `LicenseApiClient : ILicenseApi` (HttpClient injectable pour les tests).
- Consumes: rien.

Réponse Lemon Squeezy (extraits utiles) : `activate` → `{ "activated": true, "instance": { "id": "…" }, "license_key": { "status": "active" } }` ; `validate` → `{ "valid": true, "license_key": { "status": "active" } }`. Sur clé invalide : HTTP 400 + `{ "error": "…" }`.

- [ ] **Step 1: Implémenter**

`src/Replayo/Licence/ILicenseApi.cs` :

```csharp
namespace Replayo.Licence;

/// Réponse normalisée. Statut = statut Lemon Squeezy ("active"|"expired"|"disabled"|"")
/// ; Erreur non nul = échec réseau/HTTP (distinct d'une licence simplement invalide).
public sealed record LicenseReponse(bool Ok, string Statut, string? InstanceId, string? Erreur);

public interface ILicenseApi
{
    Task<LicenseReponse> ActiverAsync(string cle, string nomInstance);
    Task<LicenseReponse> ValiderAsync(string cle, string instanceId);
}
```

`src/Replayo/Licence/LicenseApiClient.cs` :

```csharp
using System.Text.Json;

namespace Replayo.Licence;

/// Appelle l'API publique de licence Lemon Squeezy (aucune clé vendeur requise).
public sealed class LicenseApiClient(HttpClient? http = null) : ILicenseApi
{
    private const string Base = "https://api.lemonsqueezy.com/v1/licenses";
    private readonly HttpClient _http = http ?? new HttpClient();

    public Task<LicenseReponse> ActiverAsync(string cle, string nomInstance) =>
        AppelAsync($"{Base}/activate", new() { ["license_key"] = cle, ["instance_name"] = nomInstance });

    public Task<LicenseReponse> ValiderAsync(string cle, string instanceId) =>
        AppelAsync($"{Base}/validate", new() { ["license_key"] = cle, ["instance_id"] = instanceId });

    private async Task<LicenseReponse> AppelAsync(string url, Dictionary<string, string> champs)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(champs) };
            req.Headers.Add("Accept", "application/json");
            using var rep = await _http.SendAsync(req);
            var corps = await rep.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(corps);
            var racine = doc.RootElement;

            // Erreur métier LS (clé inconnue, quota d'activations dépassé…) : HTTP 4xx + "error".
            if (racine.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                return new(false, "", null, err.GetString());

            bool ok = (racine.TryGetProperty("activated", out var a) && a.GetBoolean())
                   || (racine.TryGetProperty("valid", out var v) && v.GetBoolean());
            string statut = racine.TryGetProperty("license_key", out var lk) && lk.TryGetProperty("status", out var st)
                ? st.GetString() ?? "" : "";
            string? instId = racine.TryGetProperty("instance", out var inst) && inst.TryGetProperty("id", out var id)
                ? id.GetString() : null;

            return new(ok, statut, instId, null);
        }
        catch (Exception e) { return new(false, "", null, e.Message); } // réseau indisponible
    }
}
```

- [ ] **Step 2: Compiler** — `dotnet build` → OK. **Step 3: Commit** — `git commit -m "Plan C Task 2: client API licence Lemon Squeezy"`

---

### Task 3: Machine à états (`LicenseService`) — TDD avec API simulée

**Files:**
- Create: `src/Replayo/Licence/LicenseService.cs`
- Test: `tests/Replayo.Tests/LicenseServiceTests.cs`

**Interfaces:**
- Consumes: `ILicenseApi` (T2), `LicenseStore` (T1).
- Produces: `LicenseService(ILicenseApi api, LicenseStore store, Func<DateTime>? horloge=null)` ; `Task<LicenseStatut> EvaluerAuDemarrageAsync()` (charge + revalide si > 24 h) ; `Task<(bool ok, string? erreur)> ActiverAsync(string cle)` ; `Task<LicenseStatut> RevaliderAsync()` ; `LicenseStatut Statut { get; }`. Règles : validation OK → Active (+ horodatage) ; statut LS `expired`/`disabled` → Verrouillée + effacement ; échec réseau → Grâce si < 72 h depuis `DerniereValidationUtc`, sinon Verrouillée.

- [ ] **Step 1: Test qui échoue** — `tests/Replayo.Tests/LicenseServiceTests.cs`

```csharp
using Replayo.Licence;
using Xunit;

public class LicenseServiceTests
{
    private sealed class FakeApi(LicenseReponse activer, LicenseReponse valider) : ILicenseApi
    {
        public Task<LicenseReponse> ActiverAsync(string c, string n) => Task.FromResult(activer);
        public Task<LicenseReponse> ValiderAsync(string c, string i) => Task.FromResult(valider);
    }
    private static LicenseStore StoreTmp() => new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

    [Fact]
    public async Task Activer_CleValide_DevientActive()
    {
        var api = new FakeApi(new(true, "active", "inst-1", null), new(true, "active", null, null));
        var svc = new LicenseService(api, StoreTmp());
        var (ok, _) = await svc.ActiverAsync("BONNE-CLE");
        Assert.True(ok);
        Assert.Equal(LicenseStatut.Active, svc.Statut);
    }

    [Fact]
    public async Task Activer_CleInvalide_RenvoieErreur()
    {
        var api = new FakeApi(new(false, "", null, "license_key not found"), new(false, "", null, null));
        var svc = new LicenseService(api, StoreTmp());
        var (ok, erreur) = await svc.ActiverAsync("MAUVAISE");
        Assert.False(ok);
        Assert.NotNull(erreur);
        Assert.Equal(LicenseStatut.NonActivee, svc.Statut);
    }

    [Fact]
    public async Task Demarrage_SansLicence_EstNonActivee()
    {
        var api = new FakeApi(new(false, "", null, "x"), new(false, "", null, "x"));
        var svc = new LicenseService(api, StoreTmp());
        Assert.Equal(LicenseStatut.NonActivee, await svc.EvaluerAuDemarrageAsync());
    }

    [Fact]
    public async Task Revalidation_Expiree_Verrouille()
    {
        var store = StoreTmp();
        store.Enregistrer(new LicenseRecord("K", "I", DateTime.UtcNow.AddHours(-25), "active"));
        var api = new FakeApi(new(false, "", null, null), new(true, "expired", null, null)); // LS répond "expired"
        var svc = new LicenseService(api, store);
        Assert.Equal(LicenseStatut.Verrouillee, await svc.EvaluerAuDemarrageAsync());
    }

    [Fact]
    public async Task Revalidation_ReseauCoupe_DansLes72h_Grace()
    {
        var store = StoreTmp();
        store.Enregistrer(new LicenseRecord("K", "I", DateTime.UtcNow.AddHours(-40), "active"));
        var api = new FakeApi(new(false, "", null, null), new(false, "", null, "réseau indisponible"));
        var svc = new LicenseService(api, store);
        Assert.Equal(LicenseStatut.Grace, await svc.EvaluerAuDemarrageAsync());
    }

    [Fact]
    public async Task Revalidation_ReseauCoupe_Apres72h_Verrouille()
    {
        var store = StoreTmp();
        store.Enregistrer(new LicenseRecord("K", "I", DateTime.UtcNow.AddHours(-80), "active"));
        var api = new FakeApi(new(false, "", null, null), new(false, "", null, "réseau indisponible"));
        var svc = new LicenseService(api, store);
        Assert.Equal(LicenseStatut.Verrouillee, await svc.EvaluerAuDemarrageAsync());
    }
}
```

- [ ] **Step 2: Vérifier l'échec** — `dotnet test --filter LicenseServiceTests` → FAIL.

- [ ] **Step 3: Implémenter** — `src/Replayo/Licence/LicenseService.cs`

```csharp
namespace Replayo.Licence;

/// Machine à états de la licence. Ne bloque jamais l'utilisateur pour une simple
/// coupure réseau (grâce 72 h), mais verrouille dès que Lemon Squeezy confirme
/// que la licence n'est plus valide (expirée / désactivée).
public sealed class LicenseService(ILicenseApi api, LicenseStore store, Func<DateTime>? horloge = null)
{
    private static readonly TimeSpan Revalidation = TimeSpan.FromHours(24);
    private static readonly TimeSpan Grace = TimeSpan.FromHours(72);
    private readonly Func<DateTime> _maintenant = horloge ?? (() => DateTime.UtcNow);

    public LicenseStatut Statut { get; private set; } = LicenseStatut.NonActivee;

    public async Task<LicenseStatut> EvaluerAuDemarrageAsync()
    {
        var rec = store.Charger();
        if (rec is null) return Statut = LicenseStatut.NonActivee;
        if (_maintenant() - rec.DerniereValidationUtc < Revalidation)
            return Statut = LicenseStatut.Active; // validée récemment : on fait confiance
        return await RevaliderAsync();
    }

    public async Task<(bool ok, string? erreur)> ActiverAsync(string cle)
    {
        cle = cle.Trim();
        if (cle.Length == 0) return (false, "Entrez une clé de licence.");
        var rep = await api.ActiverAsync(cle, Environment.MachineName);
        if (!rep.Ok || rep.InstanceId is null)
        {
            Statut = LicenseStatut.NonActivee;
            return (false, rep.Erreur ?? "Clé de licence invalide.");
        }
        store.Enregistrer(new LicenseRecord(cle, rep.InstanceId, _maintenant(), rep.Statut));
        Statut = LicenseStatut.Active;
        return (true, null);
    }

    public async Task<LicenseStatut> RevaliderAsync()
    {
        var rec = store.Charger();
        if (rec is null) return Statut = LicenseStatut.NonActivee;

        var rep = await api.ValiderAsync(rec.Cle, rec.InstanceId);
        if (rep.Erreur is not null)
        {
            // Réseau indisponible : grâce tant qu'on est dans les 72 h.
            var age = _maintenant() - rec.DerniereValidationUtc;
            return Statut = age < Grace ? LicenseStatut.Grace : LicenseStatut.Verrouillee;
        }
        if (rep.Ok && rep.Statut == "active")
        {
            store.Enregistrer(rec with { DerniereValidationUtc = _maintenant(), StatutLs = "active" });
            return Statut = LicenseStatut.Active;
        }
        // LS a répondu : licence expirée / désactivée → verrouillage définitif.
        store.Effacer();
        return Statut = LicenseStatut.Verrouillee;
    }
}
```

- [ ] **Step 4: Vérifier** — `dotnet test --filter LicenseServiceTests` → PASS (6 tests). **Step 5: Commit**.

---

### Task 4: Fenêtre d'activation (`ActivationWindow`)

**Files:**
- Create: `src/Replayo/UI/ActivationWindow.cs`

**Interfaces:**
- Consumes: `LicenseService.ActiverAsync` (T3).
- Produces: `ActivationWindow(LicenseService licence, bool verrouille)` : `Window`, `ShowDialog()==true` si activation réussie. Champ clé + bouton Activer (désactivé pendant l'appel), message d'erreur inline, lien « Acheter une licence » (ouvre la page LS dans le navigateur), texte différent si `verrouille` (« votre licence n'est plus valide »).

- [ ] **Step 1: Implémenter** — `src/Replayo/UI/ActivationWindow.cs`

```csharp
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Replayo.Licence;

namespace Replayo.UI;

/// Écran d'activation : bloque l'accès à Replayo tant que la licence n'est pas valide.
public sealed class ActivationWindow : Window
{
    // À remplacer par l'URL réelle du produit Lemon Squeezy de l'utilisateur (Task 6).
    private const string UrlAchat = "https://replayo.lemonsqueezy.com";

    public ActivationWindow(LicenseService licence, bool verrouille)
    {
        Title = "Activation de Replayo";
        Width = 440; Height = 280;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var pile = new StackPanel { Margin = new Thickness(24) };
        pile.Children.Add(new TextBlock
        {
            Text = verrouille ? "Votre licence n'est plus valide" : "Activez Replayo",
            FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6),
        });
        pile.Children.Add(new TextBlock
        {
            Text = verrouille
                ? "Réactivez avec une clé valide pour continuer à utiliser Replayo."
                : "Entrez la clé de licence reçue par e-mail après votre achat.",
            TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 12),
        });

        var champ = new TextBox { FontSize = 14, Padding = new Thickness(6) };
        pile.Children.Add(champ);
        var erreur = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        pile.Children.Add(erreur);

        var bouton = new Button { Content = "Activer", Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        bouton.Click += async (_, _) =>
        {
            bouton.IsEnabled = false; erreur.Text = "";
            var (ok, msg) = await licence.ActiverAsync(champ.Text);
            if (ok) { DialogResult = true; Close(); }
            else { erreur.Text = msg; bouton.IsEnabled = true; }
        };
        pile.Children.Add(bouton);

        var lien = new TextBlock { Margin = new Thickness(0, 14, 0, 0) };
        var h = new Hyperlink(new Run("Acheter une licence")) { NavigateUri = new Uri(UrlAchat) };
        h.RequestNavigate += (_, e) => { Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true }); };
        lien.Inlines.Add(h);
        pile.Children.Add(lien);

        Content = pile;
    }
}
```

- [ ] **Step 2: Compiler** — `dotnet build` → OK. **Step 3: Commit**.

---

### Task 5: Intégration (garde au démarrage + revalidation 24 h + verrouillage)

**Files:**
- Modify: `src/Replayo/Program.cs`

**Interfaces:**
- Consumes: `LicenseService`, `LicenseApiClient`, `LicenseStore`, `ActivationWindow`.

Insérer AVANT le démarrage de la capture (juste après la création du `recorder`, avant `recorder.Demarrer`), et brancher un timer de revalidation 24 h qui verrouille à chaud.

- [ ] **Step 1: Modifier `Program.cs`** — ajouter en tête (après `using var recorder = …`) :

```csharp
        var licence = new LicenseService(new LicenseApiClient(), new LicenseStore());

        // Garde licence : bloque tout tant que l'app n'est pas activée / est verrouillée.
        bool ExigerLicence()
        {
            var statut = licence.EvaluerAuDemarrageAsync().GetAwaiter().GetResult();
            while (statut is LicenseStatut.NonActivee or LicenseStatut.Verrouillee)
            {
                var fenetre = new ActivationWindow(licence, verrouille: statut == LicenseStatut.Verrouillee);
                if (fenetre.ShowDialog() != true) { app.Shutdown(); return false; } // fermé sans activer → quitter
                statut = licence.Statut;
            }
            return true; // Active ou Grace
        }

        if (!ExigerLicence()) return;
```

Puis, APRÈS `recorder.Demarrer(cfg)` et le branchement du raccourci, ajouter la revalidation périodique :

```csharp
        // Revalidation silencieuse toutes les 24 h ; verrouillage à chaud si la licence tombe.
        using var revalid = new System.Threading.Timer(_ =>
        {
            var s = licence.RevaliderAsync().GetAwaiter().GetResult();
            if (s == LicenseStatut.Verrouillee)
                app.Dispatcher.Invoke(() =>
                {
                    recorder.Arreter();
                    tray.RafraichirEtat();
                    if (new ActivationWindow(licence, verrouille: true).ShowDialog() == true)
                        recorder.Demarrer(store.Charger());
                    else app.Shutdown();
                });
        }, null, TimeSpan.FromHours(24), TimeSpan.FromHours(24));
```

NB : l'onboarding (Plan B) reste APRÈS la garde licence → l'ordre au tout premier lancement devient **activation → onboarding → capture**, conforme au spec (« clé de licence à la première utilisation »).

- [ ] **Step 2: Compiler** — `dotnet build` → OK.
- [ ] **Step 3: Test hors-ligne** : supprimer `%AppData%\Replayo\license.dat` → lancer → l'écran d'activation s'affiche et bloque ; fermer sans activer → l'app quitte (pas de capture). Vérifier via UI Automation que la fenêtre « Activation de Replayo » est présente et que Replayo ne crée aucun segment.
- [ ] **Step 4: Commit**.

---

### Task 6: Compte Lemon Squeezy + recette en ligne + README

> ⚠️ **POINT D'ARRÊT — nécessite l'utilisateur.** Cette task ne peut pas être déroulée sans : (a) le compte Lemon Squeezy créé, (b) les 2 produits (abonnement 2,99 €/mois, lifetime 11,99 €) avec « licence keys » activées, (c) l'URL du store et (d) une **clé de licence de test**. **Demander ces éléments avant de commencer.**

**Files:**
- Modify: `src/Replayo/UI/ActivationWindow.cs` (constante `UrlAchat` = vraie URL), `README.md`

- [ ] **Step 1: Renseigner l'URL réelle du store** dans `ActivationWindow.UrlAchat`.
- [ ] **Step 2: Recette en ligne** (avec la clé de test fournie) :
  1. License.dat supprimé → lancer → activation → coller la clé → « Activer » → capture démarre
  2. Vérifier dans le dashboard Lemon Squeezy que l'instance apparaît (activation enregistrée)
  3. Relancer l'app → pas de ré-activation demandée (licence en cache < 24 h)
  4. Simuler l'expiration : révoquer/désactiver la clé côté LS → forcer une revalidation (avancer l'horodatage : supprimer license.dat et réactiver, ou attendre) → l'app se verrouille
  5. Couper le réseau avec une licence valide en cache < 72 h → l'app continue (grâce) ; > 72 h → verrouillage
- [ ] **Step 3: README** — section « Licence » : où acheter, comment activer, comportement hors-ligne (grâce 72 h), que faire si « licence plus valide ».
- [ ] **Step 4: Commit final + merge sur master.**

## Self-review

- **Couverture spec (périmètre C)** : activation 1re utilisation ✓ (T4/T5), inutilisable sans licence ✓ (T5 garde), revalidation 24 h ✓ (T5 timer), grâce 72 h ✓ (T3), verrouillage expiration/résiliation ✓ (T3/T5), clé chiffrée DPAPI ✓ (T1), aucun secret embarqué ✓ (API publique LS), onboarding préfixé par l'activation ✓ (T5 ordre).
- **Placeholders** : `UrlAchat` est une constante explicitement marquée « à remplacer en Task 6 » (dépend du compte utilisateur) — pas un TBD de code, une donnée externe attendue.
- **Types** : `LicenseStatut`/`LicenseRecord` définis T1, consommés T3/T5 ✓ ; `ILicenseApi` défini T2, injecté T3 (réel) et simulé (tests) ✓ ; `LicenseReponse` champs identiques T2/T3 ✓.
- **Dépendances** : aucune nouvelle (DPAPI interop, HttpClient in-box) — conforme aux conditions d'arrêt.
