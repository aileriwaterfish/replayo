# Thème sombre — Design

Date : 2026-07-19
Statut : validé

## Objectif

Interface entièrement sombre, en permanence (pas de suivi du thème Windows),
pour toutes les fenêtres WPF de Replayo, barre de titre comprise. Approche
« thème maison en code », zéro dépendance NuGet.

## Architecture

Nouveau fichier `src/Replayo/UI/Theme.cs`, classe statique, deux points
d'entrée :

- `Theme.Appliquer()` — appelé une fois au démarrage (`Program.cs`) : remplit
  `Application.Current.Resources` avec la palette et des styles implicites
  pour `Label`, `TextBox`, `Button`, `ComboBox`, `ComboBoxItem`, `CheckBox`,
  `RadioButton`, `ScrollBar`. Toute fenêtre présente ou future hérite
  automatiquement (l'ActivationWindow de la branche `plan-c-licence` sera
  sombre au merge, sans retouche).
- `Theme.Sombre(Window)` — appelé dans le constructeur de chaque fenêtre
  (`SettingsWindow`, `OnboardingWindow`, `RenameDialog`) : pose le fond sombre
  et active la barre de titre sombre via
  `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE = 20)` (interop
  `dwmapi.dll`, au `SourceInitialized` de la fenêtre). Nécessaire car les
  styles implicites WPF ne s'appliquent pas aux sous-classes de `Window`.

## Palette

| Rôle | Couleur |
|---|---|
| Fond fenêtre | `#1E1E1E` |
| Surface contrôles (TextBox, ComboBox, Button) | `#2D2D2D` |
| Bordures | `#3F3F3F` |
| Hover | `#3A3A3A` |
| Pressé / sélection | `#454545` |
| Texte | `#F0F0F0` |
| Texte secondaire | `#A0A0A0` |
| Accent (focus, sélection combo) | orange Replayo `#F97316` |

## Templates

- `Button` et `ComboBox` (bouton fermé + popup + items) : templates système
  clairs impossibles à assombrir par propriétés → retemplate compact via
  `XamlReader.Parse` (chaînes XAML dans `Theme.cs`, plus lisible que
  `FrameworkElementFactory`).
- `ScrollBar` : retemplate sombre minimal (piste + pouce, sans boutons
  flèches), bien visible dans la fenêtre Réglages.
- `Slider` : template par défaut conservé (correct sur fond sombre).
- `CheckBox` / `RadioButton` : glyphes système conservés (lisibles), seul le
  texte passe en clair.

## Hors périmètre

- Menu du tray (WinForms `ContextMenuStrip`) et dialogue « Parcourir »
  (`FolderBrowserDialog`) : rendu système conservé — les assombrir exigerait
  des renderers WinForms custom pour un gain mineur.
- Aucun réglage utilisateur clair/sombre : sombre pour tout le monde.

## Tests

- Un test headless : la fabrique de ressources (méthode statique renvoyant le
  `ResourceDictionary`, sans exiger d'`Application` vivante) contient bien les
  styles implicites attendus (clés `typeof(Button)`, `typeof(ComboBox)`,
  `typeof(TextBox)`, `typeof(Label)`, `typeof(ScrollBar)`…).
- Validation principale : build + lancement + inspection visuelle des trois
  fenêtres (Réglages, Onboarding, RenameDialog).
