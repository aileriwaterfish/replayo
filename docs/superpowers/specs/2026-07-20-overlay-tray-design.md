# Overlay « Clip sauvegardé » + menu tray custom (maquette 1b/1c) — Design

Date : 2026-07-20
Statut : validé
Source : maquette Claude Design `Replayo UI.dc.html`, écrans **1b** (overlay)
et **1c** (menu de la zone de notification).

## 1b — Toast « Clip sauvegardé »

Fenêtre WPF sans chrome, transparente, topmost, **jamais activée** (styles
étendus `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` : elle ne vole pas le focus au
jeu, n'apparaît pas dans Alt+Tab). Affichée en **haut à droite** de l'écran
principal (marge 16 px) à chaque clip sauvegardé, fermée après 4 s.

Contenu (fidèle au mock) : pastille 32×32 arrondie violet 18 % avec « ✓ »
violet ; « Clip sauvegardé » 13 px semi-gras blanc ; dessous, en mono 11 px
gris, `NomDuFichier.mp4 · Ns` (N = durée du replay configurée) ; barre de
progression violette de 2 px en bas qui se vide sur les 4 s (animation
ScaleTransform). Fond `#16161A` à 92 %, bordure blanche 10 %, rayon 8.

Branchement : `Program.cs`, après chaque clip (et après le renommage manuel
éventuel — `RenameDialog` expose désormais `CheminFinal`). Les notifications
ballon du tray restent pour les erreurs (conflit raccourci, disque plein).

Limite assumée : invisible au-dessus d'un jeu en plein écran exclusif
(borderless/fenêtré OK). Le badge permanent « replay actif » du mock n'est
PAS repris (overlay permanent trop intrusif).

## 1c — Menu tray custom

Le `ContextMenuStrip` WinForms est remplacé par une fenêtre WPF popup
(`TrayMenuWindow`) ouverte au clic (gauche ou droit) sur l'icône de tray,
ancrée au-dessus du curseur, fermée à la désactivation (clic ailleurs).
Largeur 280, fond `#202020` à 98 %, bordure blanche 10 %, rayon 8, padding 6.

- **En-tête** : tuile 24×24 dégradé violet (#7E8FFA → #8A4FE6) + « Replayo »
  semi-gras + ligne d'état 11 px : violette « ● Replay actif — Ns en
  mémoire » si capture en cours, grise « Replay arrêté » sinon.
- **« Replay activé »** + interrupteur (reflète `EnCapture`, cliquer bascule
  la capture — remplace l'item « Arrêter/Démarrer la capture »).
- **« Sauvegarder le clip »** + raccourci configuré en mono gris à droite
  (déclenche le même flux que le raccourci clavier).
- **« Ouvrir le dossier des clips »**, **« Réglages… »**, séparateur,
  **« Quitter »**.
- Items : padding 9×12, rayon 4, survol blanc 6 %.

Le menu est reconstruit à chaque ouverture (état et raccourci toujours à
jour, pas de synchronisation à maintenir). Le double-clic sur l'icône ouvre
toujours les Réglages. `TrayIcon` reçoit en plus l'action « sauvegarder le
clip » ; `RafraichirEtat` disparaît (plus d'état à pousser).

## Tests

UI pure : pas de nouveau test unitaire ; les 39 existants restent verts.
Validation : harnais scratchpad + captures (toast affiché manuellement,
menu ouvert), puis test réel (Alt+raccourci en jeu → toast).
