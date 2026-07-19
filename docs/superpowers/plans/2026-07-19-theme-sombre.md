# Thème sombre — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Interface Replayo entièrement sombre en permanence (fenêtres WPF + barre de titre), sans dépendance NuGet.

**Architecture:** Une classe statique `Theme` : palette figée + `ResourceDictionary` de styles implicites injecté dans `Application.Resources` au démarrage (toutes les fenêtres héritent), et `Theme.Sombre(Window)` par fenêtre pour le fond + la barre de titre sombre via `DwmSetWindowAttribute`. Button/ComboBox/ScrollBar retemplatés via `XamlReader.Parse`.

**Tech Stack:** C#/.NET 8, WPF construit en code, interop `dwmapi.dll`, xunit 2.5.

**Spec:** `docs/superpowers/specs/2026-07-19-theme-sombre-design.md`

## Global Constraints

- Sombre pour tout le monde, tout le temps — aucun suivi du thème Windows, aucun réglage.
- Palette exacte : fond `#1E1E1E`, surface `#2D2D2D`, bordures `#3F3F3F`, hover `#3A3A3A`, pressé `#454545`, texte `#F0F0F0`, texte secondaire `#A0A0A0`, accent `#F97316`.
- Zéro dépendance NuGet ; interop P/Invoke comme le reste du projet.
- Slider : template par défaut conservé. CheckBox/RadioButton : glyphes système conservés, texte clair seulement.
- Hors périmètre : menu du tray (WinForms), `FolderBrowserDialog`.
- `Controls`/`Theme` sont `internal` : les tests y accèdent via l'`InternalsVisibleTo` déjà présent dans `Replayo.csproj`.
- Commandes depuis `C:\Users\leoba\Projects\replayo`, branche `master`.

---

### Task 1: `Theme.cs` — palette, styles implicites, titlebar sombre

**Files:**
- Create: `src/Replayo/UI/Theme.cs`
- Test: `tests/Replayo.Tests/ThemeTests.cs` (nouveau)

**Interfaces:**
- Consumes: rien (feuille).
- Produces (utilisés par la Task 2) :
  - `internal static ResourceDictionary CreerRessources()`
  - `internal static void Appliquer()` — ajoute `CreerRessources()` aux `MergedDictionaries` de `Application.Current`
  - `internal static void Sombre(Window fenetre)` — fond sombre + titlebar DWM
  - `internal static readonly SolidColorBrush TexteSecondaire` (et les autres pinceaux de la palette)

- [ ] **Step 1: Écrire le test (qui échoue)**

Créer `tests/Replayo.Tests/ThemeTests.cs` :

```csharp
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Replayo.UI;
using Xunit;

public class ThemeTests
{
    [Fact]
    public void CreerRessources_ContientLesStylesImplicitesAttendus()
    {
        var dico = Theme.CreerRessources();
        foreach (var type in new[]
        {
            typeof(Label), typeof(TextBlock), typeof(TextBox), typeof(Button),
            typeof(ComboBox), typeof(ComboBoxItem), typeof(CheckBox),
            typeof(RadioButton), typeof(ScrollBar),
        })
            Assert.True(dico.Contains(type), $"Style implicite manquant pour {type.Name}");
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/Replayo.Tests/Replayo.Tests.csproj --nologo --filter "FullyQualifiedName~ThemeTests"`
Expected: échec de compilation — `The name 'Theme' does not exist`.

- [ ] **Step 3: Implémenter `Theme.cs`**

Créer `src/Replayo/UI/Theme.cs` :

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace Replayo.UI;

/// Thème sombre permanent : palette + styles implicites (Application.Resources)
/// + barre de titre sombre par fenêtre (DWM). Aucun suivi du thème Windows.
internal static class Theme
{
    public static readonly SolidColorBrush FondFenetre = Fige("#1E1E1E");
    public static readonly SolidColorBrush Surface = Fige("#2D2D2D");
    public static readonly SolidColorBrush Bordure = Fige("#3F3F3F");
    public static readonly SolidColorBrush Survol = Fige("#3A3A3A");
    public static readonly SolidColorBrush Presse = Fige("#454545");
    public static readonly SolidColorBrush Texte = Fige("#F0F0F0");
    public static readonly SolidColorBrush TexteSecondaire = Fige("#A0A0A0");
    public static readonly SolidColorBrush Accent = Fige("#F97316");

    private static SolidColorBrush Fige(string hex)
    {
        var pinceau = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        pinceau.Freeze();
        return pinceau;
    }

    public static void Appliquer()
        => Application.Current.Resources.MergedDictionaries.Add(CreerRessources());

    /// Fond sombre + barre de titre sombre (attribut DWM 20, Windows 10 1809+).
    public static void Sombre(Window fenetre)
    {
        fenetre.Background = FondFenetre;
        fenetre.SourceInitialized += (_, _) =>
        {
            int actif = 1;
            _ = DwmSetWindowAttribute(new WindowInteropHelper(fenetre).Handle, 20, ref actif, sizeof(int));
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribut, ref int valeur, int taille);

    public static ResourceDictionary CreerRessources()
    {
        var dico = new ResourceDictionary();

        var label = new Style(typeof(Label));
        label.Setters.Add(new Setter(Control.ForegroundProperty, Texte));
        dico.Add(typeof(Label), label);

        // Les styles implicites de TextBlock (non-Control) ne fuient pas dans les templates.
        var bloc = new Style(typeof(TextBlock));
        bloc.Setters.Add(new Setter(TextBlock.ForegroundProperty, Texte));
        dico.Add(typeof(TextBlock), bloc);

        var boite = new Style(typeof(TextBox));
        boite.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
        boite.Setters.Add(new Setter(Control.ForegroundProperty, Texte));
        boite.Setters.Add(new Setter(Control.BorderBrushProperty, Bordure));
        boite.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, Texte));
        boite.Setters.Add(new Setter(TextBoxBase.SelectionBrushProperty, Accent));
        boite.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 4, 2)));
        dico.Add(typeof(TextBox), boite);

        var coche = new Style(typeof(CheckBox));
        coche.Setters.Add(new Setter(Control.ForegroundProperty, Texte));
        dico.Add(typeof(CheckBox), coche);

        var radio = new Style(typeof(RadioButton));
        radio.Setters.Add(new Setter(Control.ForegroundProperty, Texte));
        dico.Add(typeof(RadioButton), radio);

        var bouton = new Style(typeof(Button));
        bouton.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
        bouton.Setters.Add(new Setter(Control.ForegroundProperty, Texte));
        bouton.Setters.Add(new Setter(Control.BorderBrushProperty, Bordure));
        bouton.Setters.Add(new Setter(Control.TemplateProperty, ParseTemplate(GabaritBouton)));
        dico.Add(typeof(Button), bouton);

        var combo = new Style(typeof(ComboBox));
        combo.Setters.Add(new Setter(Control.ForegroundProperty, Texte));
        combo.Setters.Add(new Setter(Control.TemplateProperty, ParseTemplate(GabaritCombo)));
        dico.Add(typeof(ComboBox), combo);

        var element = new Style(typeof(ComboBoxItem));
        element.Setters.Add(new Setter(Control.ForegroundProperty, Texte));
        element.Setters.Add(new Setter(Control.TemplateProperty, ParseTemplate(GabaritComboItem)));
        dico.Add(typeof(ComboBoxItem), element);

        var barre = new Style(typeof(ScrollBar));
        barre.Setters.Add(new Setter(FrameworkElement.WidthProperty, 10.0));
        barre.Setters.Add(new Setter(Control.TemplateProperty, ParseTemplate(GabaritScrollBar)));
        dico.Add(typeof(ScrollBar), barre);

        return dico;
    }

    private static ControlTemplate ParseTemplate(string xaml) => (ControlTemplate)XamlReader.Parse(xaml);

    private const string Ns =
        "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
        "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";

    private const string GabaritBouton = $@"
<ControlTemplate {Ns} TargetType='Button'>
  <Border x:Name='fond' Background='{{TemplateBinding Background}}' BorderBrush='{{TemplateBinding BorderBrush}}'
          BorderThickness='1' CornerRadius='3' Padding='{{TemplateBinding Padding}}'>
    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='fond' Property='Background' Value='#3A3A3A'/></Trigger>
    <Trigger Property='IsPressed' Value='True'><Setter TargetName='fond' Property='Background' Value='#454545'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

    private const string GabaritCombo = $@"
<ControlTemplate {Ns} TargetType='ComboBox'>
  <Grid>
    <ToggleButton Focusable='False' ClickMode='Press'
                  IsChecked='{{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={{RelativeSource TemplatedParent}}}}'>
      <ToggleButton.Template>
        <ControlTemplate TargetType='ToggleButton'>
          <Border x:Name='fond' Background='#2D2D2D' BorderBrush='#3F3F3F' BorderThickness='1' CornerRadius='3'>
            <Path Data='M 0 0 L 4 4 L 8 0 Z' Fill='#F0F0F0' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,8,0'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='fond' Property='Background' Value='#3A3A3A'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </ToggleButton.Template>
    </ToggleButton>
    <ContentPresenter Content='{{TemplateBinding SelectionBoxItem}}' ContentTemplate='{{TemplateBinding SelectionBoxItemTemplate}}'
                      Margin='8,3,24,3' VerticalAlignment='Center' IsHitTestVisible='False'/>
    <Popup IsOpen='{{TemplateBinding IsDropDownOpen}}' Placement='Bottom' AllowsTransparency='True' Focusable='False'>
      <Border Background='#2D2D2D' BorderBrush='#3F3F3F' BorderThickness='1' CornerRadius='3'
              MinWidth='{{TemplateBinding ActualWidth}}' MaxHeight='{{TemplateBinding MaxDropDownHeight}}'>
        <ScrollViewer><ItemsPresenter/></ScrollViewer>
      </Border>
    </Popup>
  </Grid>
</ControlTemplate>";

    private const string GabaritComboItem = $@"
<ControlTemplate {Ns} TargetType='ComboBoxItem'>
  <Border x:Name='fond' Background='Transparent' Padding='8,4,8,4'>
    <ContentPresenter/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='fond' Property='Background' Value='#3A3A3A'/></Trigger>
    <Trigger Property='IsSelected' Value='True'><Setter TargetName='fond' Property='Background' Value='#454545'/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

    private const string GabaritScrollBar = $@"
<ControlTemplate {Ns} TargetType='ScrollBar'>
  <Grid Background='#1E1E1E'>
    <Track x:Name='PART_Track' IsDirectionReversed='True'>
      <Track.Thumb>
        <Thumb>
          <Thumb.Template>
            <ControlTemplate TargetType='Thumb'>
              <Border Background='#3F3F3F' CornerRadius='4' Margin='2'/>
            </ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
    </Track>
  </Grid>
</ControlTemplate>";
}
```

Notes d'implémentation :
- Les chaînes XAML sont des *raw interpolated strings* C# : les accolades des
  `TemplateBinding`/`Binding` sont doublées (`{{ }}`) pour échapper
  l'interpolation, qui n'injecte que `{Ns}`.
- Les couleurs sont volontairement en dur dans les gabarits (pas de
  `DynamicResource`) : la palette est figée, autant garder les gabarits lisibles.

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/Replayo.Tests/Replayo.Tests.csproj --nologo --filter "FullyQualifiedName~ThemeTests"`
Expected: `Réussi ! ... : 1`, 0 échec. (Si une exception de thread STA apparaît à la création des styles, exécuter le corps du test dans un thread STA : `var t = new Thread(corps); t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();` — mais ce ne devrait pas être nécessaire, `XamlReader.Parse` et la construction de styles n'exigent pas STA.)

- [ ] **Step 5: Commit**

```bash
git add src/Replayo/UI/Theme.cs tests/Replayo.Tests/ThemeTests.cs
git commit -m "Theme sombre: palette + styles implicites + titlebar DWM (1 test)"
```

---

### Task 2: Branchement dans l'app et les trois fenêtres

**Files:**
- Modify: `src/Replayo/Program.cs:17`
- Modify: `src/Replayo/UI/SettingsWindow.cs:42` (fin du bloc d'init de la fenêtre)
- Modify: `src/Replayo/UI/OnboardingWindow.cs:21,29-32`
- Modify: `src/Replayo/UI/RenameDialog.cs:33`

**Interfaces:**
- Consumes: `Theme.Appliquer()`, `Theme.Sombre(Window)`, `Theme.TexteSecondaire` (Task 1).
- Produces: rien (feuille).

- [ ] **Step 1: Appliquer le thème au démarrage**

Dans `src/Replayo/Program.cs`, après la ligne 17 (`var app = new Application …`), ajouter :

```csharp
        Theme.Appliquer();
```

- [ ] **Step 2: Assombrir les trois fenêtres**

Dans `src/Replayo/UI/SettingsWindow.cs`, après `ResizeMode = ResizeMode.CanMinimize;` (ligne 42), ajouter :

```csharp
        Theme.Sombre(this);
```

Dans `src/Replayo/UI/OnboardingWindow.cs`, après `ResizeMode = ResizeMode.NoResize;` (ligne 21), ajouter :

```csharp
        Theme.Sombre(this);
```

et dans le second `TextBlock` du même fichier (lignes 29-32), remplacer :

```csharp
            Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
```

par :

```csharp
            Foreground = Theme.TexteSecondaire, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
```

Dans `src/Replayo/UI/RenameDialog.cs`, après `Topmost = true;` (ligne 33), ajouter :

```csharp
        Theme.Sombre(this);
```

- [ ] **Step 3: Build + suite complète**

Run: `dotnet build Replayo.sln --nologo -v q && dotnet test tests/Replayo.Tests/Replayo.Tests.csproj --nologo`
Expected: 0 erreur ; tous les tests passent, 0 échec.

- [ ] **Step 4: Vérification visuelle**

1. Fermer l'instance en cours (`Stop-Process -Name Replayo`), lancer
   `src\Replayo\bin\Debug\net8.0-windows10.0.22000.0\Replayo.exe`.
2. Ouvrir **Réglages** (tray → Réglages) : fond `#1E1E1E`, barre de titre
   sombre, textes clairs, combos sombres (ouvrir un dropdown : popup sombre,
   survol gris), bouton Enregistrer sombre avec hover, scrollbar sombre.
3. Vérifier l'**onboarding** : supprimer temporairement
   `%APPDATA%\Replayo\config.json` **après l'avoir copié**, relancer l'app,
   constater la fenêtre sombre, fermer, restaurer le fichier copié, relancer.
4. **RenameDialog** : activer « Me demander à chaque clip » dans Réglages,
   Alt+F10, constater le dialogue sombre ; remettre le réglage d'origine.
5. Captures d'écran des fenêtres pour trace.

- [ ] **Step 5: Commit**

```bash
git add src/Replayo/Program.cs src/Replayo/UI/SettingsWindow.cs src/Replayo/UI/OnboardingWindow.cs src/Replayo/UI/RenameDialog.cs
git commit -m "Theme sombre: applique aux trois fenetres + titlebar"
```
