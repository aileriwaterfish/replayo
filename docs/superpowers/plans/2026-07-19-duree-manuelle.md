# Saisie manuelle de la durée du replay — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter un champ texte (secondes) à côté du slider « Durée du replay » dans les Réglages, synchronisé avec lui ; la valeur tapée fait foi, bornes élargies à 5–1200 s.

**Architecture:** Toute la logique (parse/clamp/palier le plus proche) vit en méthodes statiques pures dans `Controls` (testables headless). Un builder `PanneauDuree` câble slider + TextBox + label sur le modèle de `PanneauEcrans`. `SettingsWindow` consomme le panneau via un `Func<int>`.

**Tech Stack:** C#/.NET 8, WPF construit en code (pas de XAML), xunit 2.5.

**Spec:** `docs/superpowers/specs/2026-07-19-duree-manuelle-design.md`

## Global Constraints

- Bornes durée : **5–1200 s** (clamp `ConfigStore` : 15 → 5).
- La valeur tapée est enregistrée telle quelle, même entre deux paliers du slider.
- Saisie invalide (vide, non numérique) → retour à la dernière valeur valide.
- Aucun changement côté clip/ffmpeg/`SegmentsPourDuree` ni côté onboarding.
- UI mono-fichier par fenêtre, contrôles construits en code, textes en français.
- `Controls` est `internal` : les tests y accèdent via `InternalsVisibleTo`.
- Commandes depuis la racine `C:\Users\leoba\Projects\replayo`, branche `master`.

---

### Task 1: Logique pure `NormaliserDuree` + `IndexPalierLePlusProche`

**Files:**
- Modify: `src/Replayo/Replayo.csproj` (ajout `InternalsVisibleTo`)
- Modify: `src/Replayo/UI/Controls.cs` (après `DureeSelectionnee`, ligne ~66)
- Test: `tests/Replayo.Tests/DureeTests.cs` (nouveau)

**Interfaces:**
- Consumes: `Controls.Durees` (existant : `{ 15, 30, 60, 120, 180, 300, 600, 900, 1200 }`).
- Produces: `internal static int NormaliserDuree(string? texte, int valeurActuelle)` et `internal static int IndexPalierLePlusProche(int valeur)` — utilisés par la Task 3.

- [ ] **Step 1: Autoriser les tests à voir les types internes**

Dans `src/Replayo/Replayo.csproj`, ajouter cet `ItemGroup` avant `</Project>` :

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Replayo.Tests" />
  </ItemGroup>
```

- [ ] **Step 2: Écrire les tests (qui échouent)**

Créer `tests/Replayo.Tests/DureeTests.cs` :

```csharp
using Replayo.UI;
using Xunit;

public class DureeTests
{
    [Theory]
    [InlineData("9", 300, 9)]        // saisie libre sous les paliers
    [InlineData(" 9 ", 300, 9)]      // espaces tolérés
    [InlineData("45", 300, 45)]      // valeur entre deux paliers, gardée telle quelle
    [InlineData("3", 300, 5)]        // sous la borne min → clamp 5
    [InlineData("99999", 300, 1200)] // au-dessus de la borne max → clamp 1200
    [InlineData("abc", 300, 300)]    // invalide → valeur actuelle
    [InlineData("", 300, 300)]       // vide → valeur actuelle
    [InlineData(null, 300, 300)]     // null → valeur actuelle
    [InlineData("-8", 120, 5)]       // négatif : parsé puis clampé à 5
    public void NormaliserDuree_ParseClampeOuRetombe(string? texte, int actuelle, int attendu)
        => Assert.Equal(attendu, Controls.NormaliserDuree(texte, actuelle));

    [Theory]
    [InlineData(9, 0)]     // 9 s → palier 15 s (index 0)
    [InlineData(15, 0)]    // palier exact
    [InlineData(45, 1)]    // équidistant 30/60 → premier palier (30, index 1)
    [InlineData(500, 6)]   // 500 s → 600 s (index 6)
    [InlineData(1200, 8)]  // borne max
    public void IndexPalierLePlusProche_RendLIndexDuPalierLePlusProche(int valeur, int attendu)
        => Assert.Equal(attendu, Controls.IndexPalierLePlusProche(valeur));
}
```

- [ ] **Step 3: Vérifier l'échec**

Run: `dotnet test tests/Replayo.Tests/Replayo.Tests.csproj --nologo --filter "FullyQualifiedName~DureeTests"`
Expected: échec de compilation — `'Controls' does not contain a definition for 'NormaliserDuree'`.

- [ ] **Step 4: Implémenter**

Dans `src/Replayo/UI/Controls.cs`, juste après `DureeSelectionnee` (ligne 66) :

```csharp
    // Saisie manuelle : parse + clamp 5–1200 ; invalide → on garde la valeur actuelle.
    public static int NormaliserDuree(string? texte, int valeurActuelle)
        => int.TryParse(texte?.Trim(), out int s) ? Math.Clamp(s, 5, 1200) : valeurActuelle;

    public static int IndexPalierLePlusProche(int valeur)
    {
        int meilleur = 0;
        for (int i = 1; i < Durees.Length; i++)
            if (Math.Abs(Durees[i] - valeur) < Math.Abs(Durees[meilleur] - valeur)) meilleur = i;
        return meilleur;
    }
```

(`public` dans une classe `internal` : la visibilité effective reste interne, exposée aux tests par l'`InternalsVisibleTo` du Step 1.)

- [ ] **Step 5: Vérifier le succès**

Run: `dotnet test tests/Replayo.Tests/Replayo.Tests.csproj --nologo --filter "FullyQualifiedName~DureeTests"`
Expected: `Réussi ! ... : 14` (9 + 5 cas), 0 échec.

- [ ] **Step 6: Commit**

```bash
git add src/Replayo/Replayo.csproj src/Replayo/UI/Controls.cs tests/Replayo.Tests/DureeTests.cs
git commit -m "Duree manuelle: logique NormaliserDuree + IndexPalierLePlusProche (14 tests)"
```

---

### Task 2: Clamp `ConfigStore` 15 → 5

**Files:**
- Modify: `src/Replayo/Core/ConfigStore.cs:22`
- Modify: `tests/Replayo.Tests/ConfigStoreTests.cs:31-40` (test `Charger_BorneLaDureeEntre15Et1200Secondes`)

**Interfaces:**
- Consumes: `ConfigStore.Charger()` / `Enregistrer()` (existants).
- Produces: config acceptant `DureeBufferSecondes` ∈ [5, 1200] — la Task 3 s'appuie sur ces bornes.

- [ ] **Step 1: Mettre à jour le test (qui échoue)**

Dans `tests/Replayo.Tests/ConfigStoreTests.cs`, remplacer le test `Charger_BorneLaDureeEntre15Et1200Secondes` par :

```csharp
    [Fact]
    public void Charger_BorneLaDureeEntre5Et1200Secondes()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var store = new ConfigStore(dir);
        var cfg = store.Charger();
        cfg.DureeBufferSecondes = 99999;
        store.Enregistrer(cfg);
        Assert.Equal(1200, new ConfigStore(dir).Charger().DureeBufferSecondes);

        cfg.DureeBufferSecondes = 2;
        store.Enregistrer(cfg);
        Assert.Equal(5, new ConfigStore(dir).Charger().DureeBufferSecondes);

        cfg.DureeBufferSecondes = 9; // sous l'ancien minimum de 15 : doit passer tel quel
        store.Enregistrer(cfg);
        Assert.Equal(9, new ConfigStore(dir).Charger().DureeBufferSecondes);
    }
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/Replayo.Tests/Replayo.Tests.csproj --nologo --filter "FullyQualifiedName~ConfigStoreTests"`
Expected: FAIL — `Assert.Equal() Failure ... Expected: 5 / Actual: 15`.

- [ ] **Step 3: Implémenter**

Dans `src/Replayo/Core/ConfigStore.cs` ligne 22, remplacer :

```csharp
        cfg.DureeBufferSecondes = Math.Clamp(cfg.DureeBufferSecondes, 15, 1200);
```

par :

```csharp
        cfg.DureeBufferSecondes = Math.Clamp(cfg.DureeBufferSecondes, 5, 1200);
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/Replayo.Tests/Replayo.Tests.csproj --nologo --filter "FullyQualifiedName~ConfigStoreTests"`
Expected: tous les tests ConfigStore passent, 0 échec.

- [ ] **Step 5: Commit**

```bash
git add src/Replayo/Core/ConfigStore.cs tests/Replayo.Tests/ConfigStoreTests.cs
git commit -m "Duree manuelle: clamp config 5-1200 s (min 15 -> 5)"
```

---

### Task 3: `PanneauDuree` + branchement dans `SettingsWindow`

**Files:**
- Modify: `src/Replayo/UI/Controls.cs` (remplacer `SliderDuree`, ajouter `PanneauDuree`)
- Modify: `src/Replayo/UI/SettingsWindow.cs:17-18,46-51,122`

**Interfaces:**
- Consumes: `NormaliserDuree` et `IndexPalierLePlusProche` (Task 1), bornes 5–1200 (Task 2), `Durees`, `TexteDuree`, `SliderDuree` (existants).
- Produces: `internal static (StackPanel Panneau, Func<int> Lire) PanneauDuree(int valeur)` — consommé par `SettingsWindow` uniquement (l'onboarding garde son slider seul).

- [ ] **Step 1: Corriger l'init de `SliderDuree` (palier le plus proche)**

Dans `src/Replayo/UI/Controls.cs`, remplacer `SliderDuree` (lignes 54-64) par :

```csharp
    public static Slider SliderDuree(int valeur) => new()
    {
        Minimum = 0, Maximum = Durees.Length - 1,
        Value = IndexPalierLePlusProche(valeur), // valeur libre (ex. 9 s) → palier le plus proche
        IsSnapToTickEnabled = true, TickFrequency = 1,
        Margin = new Thickness(0, 0, 0, 0),
    };
```

(Comportement inchangé pour les valeurs palier ; l'ancien `if (idx < 0) idx = 5;` était du code mort après `Math.Max`. L'onboarding, qui appelle aussi `SliderDuree`, y gagne le même arrondi correct.)

- [ ] **Step 2: Ajouter `PanneauDuree`**

Dans `src/Replayo/UI/Controls.cs`, après `IndexPalierLePlusProche` :

```csharp
    /// Slider + champ texte synchronisés : le slider donne les paliers rapides,
    /// le champ accepte une valeur libre (5–1200 s) qui fait foi à l'enregistrement.
    public static (StackPanel Panneau, Func<int> Lire) PanneauDuree(int valeur)
    {
        int courante = valeur;
        bool majInterne = false; // vrai quand on déplace le slider par code (ne pas écraser la saisie)

        var slider = SliderDuree(valeur);
        var champ = new TextBox
        {
            Text = valeur.ToString(), Width = 56,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
        };
        var lbl = new Label { Content = TexteDuree(valeur) };

        slider.ValueChanged += (_, _) =>
        {
            if (majInterne) return;
            courante = DureeSelectionnee(slider);
            champ.Text = courante.ToString();
            lbl.Content = TexteDuree(courante);
        };

        void Valider()
        {
            courante = NormaliserDuree(champ.Text, courante);
            champ.Text = courante.ToString();
            lbl.Content = TexteDuree(courante);
            majInterne = true;
            slider.Value = IndexPalierLePlusProche(courante);
            majInterne = false;
        }
        champ.LostFocus += (_, _) => Valider();
        champ.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Valider(); e.Handled = true; } };

        var ligne = new DockPanel();
        DockPanel.SetDock(champ, Dock.Right);
        var unite = new Label { Content = "s", VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(unite, Dock.Right);
        ligne.Children.Add(unite);
        ligne.Children.Add(champ);
        ligne.Children.Add(slider); // remplit l'espace restant
        slider.VerticalAlignment = VerticalAlignment.Center;

        var panneau = new StackPanel();
        panneau.Children.Add(ligne);
        panneau.Children.Add(lbl);
        return (panneau, () => courante);
    }
```

- [ ] **Step 3: Brancher dans `SettingsWindow`**

Dans `src/Replayo/UI/SettingsWindow.cs` :

Remplacer les champs (lignes 17-18) :

```csharp
    private readonly Slider _duree;
    private readonly Label _dureeLbl;
```

par :

```csharp
    private readonly Func<int> _lireDuree;
```

Remplacer le bloc « Durée du replay » (lignes 46-51) :

```csharp
        pile.Children.Add(Controls.Titre("Durée du replay"));
        _dureeLbl = new Label { Content = Controls.TexteDuree(cfg.DureeBufferSecondes) };
        _duree = Controls.SliderDuree(cfg.DureeBufferSecondes);
        _duree.ValueChanged += (_, _) => _dureeLbl.Content = Controls.TexteDuree(Controls.DureeSelectionnee(_duree));
        pile.Children.Add(_duree);
        pile.Children.Add(_dureeLbl);
```

par :

```csharp
        pile.Children.Add(Controls.Titre("Durée du replay"));
        var (panneauDuree, lireDuree) = Controls.PanneauDuree(cfg.DureeBufferSecondes);
        _lireDuree = lireDuree;
        pile.Children.Add(panneauDuree);
```

Dans `Enregistrer()` (ligne 122), remplacer :

```csharp
        cfg.DureeBufferSecondes = Controls.DureeSelectionnee(_duree);
```

par :

```csharp
        cfg.DureeBufferSecondes = _lireDuree();
```

- [ ] **Step 4: Build + suite complète**

Run: `dotnet build Replayo.sln --nologo -v q && dotnet test tests/Replayo.Tests/Replayo.Tests.csproj --nologo`
Expected: 0 erreur de build ; tous les tests passent (17 existants + 14 de la Task 1 + ConfigStore mis à jour), 0 échec.

- [ ] **Step 5: Vérification manuelle (lancer l'app)**

1. Lancer `src\Replayo\bin\Debug\net8.0-windows10.0.19041.0\Replayo.exe`.
2. Tray → Réglages : le champ affiche la durée courante ; taper `9` puis Entrée → le slider se cale sur 15 s (palier le plus proche), le label affiche « 9 s ».
3. Taper `abc` puis Tab → le champ revient à `9`. Taper `3` → `5`. Taper `99999` → `1200`.
4. Enregistrer, rouvrir Réglages → le champ affiche `9`, et `%APPDATA%\Replayo\config.json` contient `"DureeBufferSecondes": 9`.
5. Alt+F10 → le clip produit couvre ~10 s (arrondi au segment supérieur), pas 9 s pile : attendu.

- [ ] **Step 6: Commit**

```bash
git add src/Replayo/UI/Controls.cs src/Replayo/UI/SettingsWindow.cs
git commit -m "Duree manuelle: champ texte synchronise au slider dans les Reglages"
```
