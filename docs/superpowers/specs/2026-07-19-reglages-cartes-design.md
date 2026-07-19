# Réglages en cartes (maquette « Replayo UI » 1a) — Design

Date : 2026-07-19
Statut : validé
Source : projet Claude Design « Logiciel de record et clip », fichier
`Replayo UI.dc.html`, écran **1a** (Réglages, thème sombre). Écrans 1b
(overlay), 1c (tray custom) et 1d (thème clair) explicitement hors périmètre.

## Objectif

Restyler la fenêtre Réglages sur le modèle « cartes Windows 11 » de la
maquette : sections **Capture / Audio / Clips**, une carte par réglage
(titre + sous-titre descriptif à gauche, contrôle à droite), toggles à la
place des cases à cocher pour les booléens, accent **violet**.

## Palette (mise à jour de `Theme`)

| Rôle | Avant | Après |
|---|---|---|
| Accent | orange `#F97316` | violet `#6E6CF3` (≈ oklch 0.62 0.19 278) |
| Accent survolé | — | `#918FF6` |
| Fond fenêtre | `#1E1E1E` | `#202020` |
| Surface carte | `#2D2D2D` | `#2B2B2B` |
| Bordure carte | `#3F3F3F` | `#3A3A3A` |

Le reste (textes, hover, pressé) est inchangé. Toutes les fenêtres héritent
de la palette ; seule la fenêtre Réglages adopte la structure en cartes.

## Nouvelles briques (`Theme` / `Controls`)

- **Interrupteur** : style nommé `"Interrupteur"` pour `CheckBox` — piste
  40×20 arrondie (`#3F3F3F`, violet si coché), pouce blanc 14 px qui glisse,
  opacité 0.45 si désactivé. Template via `XamlReader` comme les autres.
- **`Carte(titre, sousTitre?, droite)`** : `Border` surface/bordure/radius 6,
  padding 16×18 ; à gauche titre 14 px blanc + sous-titre 12 px secondaire,
  à droite le contrôle centré verticalement.
- **`CarteVerticale(titre, sousTitre?, bas)`** : même carte, contenu empilé
  (radios, liste d'écrans).
- **Raccourci en « keycap »** : la TextBox readonly existante, restylée
  (mono, centré, bordure basse épaisse) — le clic + frappe reste le mode de
  capture, le sous-titre l'explique. Pas de lien « Modifier ».
- **`PanneauDuree`** passe en rangée compacte pour tenir dans une carte :
  slider 180 px + champ 52 px + « s » (le label redondant sous le slider
  disparaît, le champ affiche la valeur). Logique `Lire()` inchangée.
- **`PanneauEcrans`** : « Tous les écrans » devient un interrupteur, les
  écrans restent des cases carrées indentées (opacité 0.45 quand « tous »
  est actif). Partagé avec l'onboarding, qui en profite tel quel.

## Structure de la fenêtre

Largeur 640. Sections dans l'ordre de la maquette, plus ce qu'elle omet mais
que l'app doit garder :

- **Capture** : Durée du replay (« Les dernières secondes gardées en
  mémoire ») · Qualité (« Résolution et fluidité de l'enregistrement »,
  combo min 280) · Format de sortie (« MP4 recommandé pour le partage ») ·
  Écrans (carte verticale, toggle « Tous les écrans » — « Capturer chaque
  moniteur connecté »).
- **Audio** : Son du système, Micro — toggles avec libellé d'état
  « Activé »/« Désactivé » qui suit le toggle.
- **Clips** : Raccourci du clip (« Cliquer puis taper la combinaison »,
  keycap) · Nom des clips (radios, exemple horodaté grisé à côté de
  « Automatique ») · Dossier des clips (« Vide = Vidéos\Replayo », champ +
  Parcourir…).
- **Hors maquette, conservés** : carte « Démarrer avec Windows » (toggle) et
  bouton **Enregistrer** en bas à droite (le comportement enregistrer-puis-
  redémarrer-la-capture ne change pas).

## Écarts assumés vs maquette

- Barre de titre : on garde le chrome système (déjà sombre via DWM), pas de
  barre custom avec icône dégradée.
- Slider : template WPF par défaut (pas de piste violette remplie).
- Radios : glyphes système (comme acté pour le thème sombre).

## Tests

- `ThemeTests` étendu : le dictionnaire contient la clé `"Interrupteur"` et
  l'accent vaut `#6E6CF3`.
- Aucune logique nouvelle : `NormaliserDuree`/`Lire` déjà couverts.
- Validation principale : build + capture de la fenêtre via le harnais.
