# Saisie manuelle de la durée du replay — Design

Date : 2026-07-19
Statut : validé

## Objectif

Permettre de saisir au clavier une durée de replay arbitraire (ex. 9 s) dans la
fenêtre Réglages, en plus des paliers du slider. La valeur saisie est celle
enregistrée, même entre deux paliers.

## Comportement

- Dans la section « Durée du replay » : le slider existant reste, un champ
  texte (secondes) est ajouté à sa droite. Le label texte existant est conservé.
- Synchronisation :
  - Bouger le slider → le champ affiche la valeur du palier.
  - Taper une valeur, validée par Entrée ou perte de focus → le slider se place
    au palier le plus proche, mais c'est la valeur tapée qui fait foi.
- Bornes : **5 s – 1200 s**. Le clamp de `ConfigStore` passe de 15–1200 à
  5–1200. Valeur hors bornes → clampée.
- Saisie invalide (vide, non numérique) → retour à la dernière valeur valide.
- Sémantique inchangée : la durée est un minimum garanti ; le clip est arrondi
  aux segments de 10 s par `SegmentsPourDuree` (aucun changement côté
  clip/ffmpeg).

## Implémentation

- `src/Replayo/UI/Controls.cs` :
  - `NormaliserDuree(string texte, int valeurActuelle) → int` : méthode
    statique pure — parse (trim), fallback sur `valeurActuelle` si invalide,
    clamp 5–1200. Toute la logique vit ici (testable headless, pas d'UI).
  - `PanneauDuree(int valeur) → (Panneau, Lire)` : builder sur le modèle de
    `PanneauEcrans`, câble slider + TextBox + label et la synchronisation.
- `src/Replayo/UI/SettingsWindow.cs` : remplace `_duree`/`_dureeLbl` par le
  panneau ; la sauvegarde lit `Lire()`.
- `src/Replayo/Core/ConfigStore.cs` : clamp 5–1200.

## Tests

Unitaires sur `NormaliserDuree` :
- `"9"` → 9 ; `" 9 "` → 9
- `"abc"`, `""`, `null` → valeur actuelle
- `"3"` → 5 ; `"99999"` → 1200

## Hors périmètre

- Onboarding (ne règle pas la durée).
- Format d'affichage `TexteDuree` (inchangé).
- Découpe exacte du clip (re-encodage ou `-ss`) — rejetée : approximatif OK.
