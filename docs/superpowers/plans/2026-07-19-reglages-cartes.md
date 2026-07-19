# Réglages en cartes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyler la fenêtre Réglages en cartes Windows 11 (sections Capture/Audio/Clips, toggles, accent violet) selon l'écran 1a de la maquette Claude Design.

**Architecture:** La palette et le style « Interrupteur » vivent dans `Theme` ; les builders `Carte`/`CarteVerticale` et les panneaux compacts dans `Controls` ; `SettingsWindow` est réécrite section par section. Onboarding/RenameDialog inchangés (ils héritent seulement de la palette).

**Tech Stack:** C#/.NET 8, WPF en code, xunit.

**Spec:** `docs/superpowers/specs/2026-07-19-reglages-cartes-design.md`

## Global Constraints

- Accent `#6E6CF3`, accent survolé `#918FF6`, fond `#202020`, carte `#2B2B2B`, bordure `#3A3A3A`.
- Comportements inchangés : capture du raccourci par frappe, Enregistrer → `Redemarrer` + `ConfigChangee`, lecture/écriture config identiques.
- Chrome de fenêtre système conservé ; slider et radios en template par défaut.
- Branche `reglages-cartes` depuis master ; commandes depuis la racine du dépôt.

---

### Task 1: Palette violette + style « Interrupteur » + builders de cartes

**Files:**
- Modify: `src/Replayo/UI/Theme.cs` (pinceaux + style nommé "Interrupteur")
- Modify: `src/Replayo/UI/Controls.cs` (Carte, CarteVerticale, Interrupteur, PanneauDuree compact, PanneauEcrans avec toggle)
- Test: `tests/Replayo.Tests/ThemeTests.cs` (étendu)

**Interfaces:**
- Produces (consommés par la Task 2) :
  - `Theme.Accent` = `#6E6CF3`, `Theme.AccentSurvol` = `#918FF6`
  - clé de ressource `"Interrupteur"` (Style pour CheckBox)
  - `Controls.Interrupteur(bool coche)` → CheckBox stylée toggle
  - `Controls.Carte(string titre, string? sousTitre, FrameworkElement droite)` → Border
  - `Controls.CarteVerticale(string titre, string? sousTitre, FrameworkElement bas)` → Border
  - `Controls.PanneauDuree(int valeur)` → `(StackPanel, Func<int>)` (rangée compacte)
  - `Controls.PanneauEcrans(List<int>)` → `(StackPanel, Func<List<int>>)` (toggle + cases)

- [ ] **Step 1: étendre ThemeTests (échec attendu)** — ajouter :

```csharp
    [Fact]
    public void CreerRessources_ContientLeStyleInterrupteur()
        => Assert.True(Theme.CreerRessources().Contains("Interrupteur"));

    [Fact]
    public void Accent_EstLeVioletDeLaMaquette()
        => Assert.Equal("#FF6E6CF3", Theme.Accent.Color.ToString());
```

- [ ] **Step 2: vérifier l'échec** (`dotnet test --filter ThemeTests`)
- [ ] **Step 3: implémenter** — palette (`Accent`, `AccentSurvol`, `FondFenetre #202020`, `Surface #2B2B2B`, `Bordure #3A3A3A`), gabarit XAML `GabaritInterrupteur` + style nommé, builders dans Controls (code complet dans la spec/commit).
- [ ] **Step 4: vérifier le succès** (ThemeTests + DureeTests verts)
- [ ] **Step 5: commit**

### Task 2: Réécriture de `SettingsWindow` en cartes

**Files:**
- Modify: `src/Replayo/UI/SettingsWindow.cs`

**Interfaces:**
- Consumes: tout ce que produit la Task 1 ; `ConfigStore`, `RecorderService`, `AutostartManager` inchangés.

- [ ] **Step 1: réécrire le corps du constructeur** — largeur 640, sections Capture/Audio/Clips en cartes (ordre et sous-titres de la spec), toggles Audio avec libellé d'état, keycap raccourci, carte autostart, bouton Enregistrer.
- [ ] **Step 2: build + suite complète** (0 erreur, 39 tests verts)
- [ ] **Step 3: vérification visuelle** — harnais scratchpad (`AssemblyName=Replayo.Tests`) + PrintWindow, comparer à la maquette.
- [ ] **Step 4: commit, merge sur master, relancer l'app**
