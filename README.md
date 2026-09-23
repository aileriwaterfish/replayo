# Replayo

Capture d'écran continue type « Instant Replay » pour Windows 10/11 : un buffer
circulaire des dernières secondes/minutes, clippable rétroactivement avec **Alt+F10**.
Priorité absolue : consommation de ressources minimale (capture GPU zéro-copie +
encodage matériel).

- Design : `docs/superpowers/specs/2026-07-15-replayo-design.md`
- Plan A (noyau, en cours) : `docs/superpowers/plans/2026-07-15-replayo-plan-a-noyau.md`

## Prérequis

- Windows 10 (1903+) / Windows 11, x64
- SDK .NET 8
- ffmpeg local : `powershell -ExecutionPolicy Bypass -File scripts/installer-ffmpeg.ps1` (une fois)

## Lancer

```powershell
dotnet run --project src/Replayo -c Release
```

Replayo est une **application de fond** : au premier lancement, un assistant demande
format, qualité, écran(s) et durée du replay, puis la capture démarre et l'app se loge
dans la **zone de notification** (system tray).

- **Alt+F10** (reconfigurable) → clip des N dernières secondes, rangé + son de confirmation
- **Icône tray** → démarrer/arrêter la capture, ouvrir le dossier des clips, Réglages, Quitter
- **Démarrer un REC** dans le menu de l'icône → enregistrement continu ; **Arrêter et sauvegarder le REC** crée un MP4/MKV dans `Vidéos\Replayo\Enregistrements\AAAA-MM\` (ou le dossier de sortie choisi). Le replay et ses clips restent disponibles pendant le REC. Un arrêt ou un redémarrage de la capture sauvegarde aussi le REC en cours.
- **Fermer une fenêtre** = retour au tray (la capture continue) ; **Quitter** = uniquement via le tray
- **Réglages** : durée, qualité, format, écran(s), audio (système/micro), raccourci,
  nom auto ou manuel, dossier de sortie, démarrage avec Windows
- **Démarrage avec Windows** : case dans les Réglages (clé `HKCU\...\Run`)

## Architecture (résumé)

Windows Graphics Capture (frames GPU) → encodeur matériel H.264 (NVENC/AMF/QuickSync,
repli logiciel signalé) → segments MP4 de ~10 s en anneau sur disque
(`%LocalAppData%\Replayo\buffer`) → clip = assemblage ffmpeg `-c copy` sans ré-encodage
→ `Vidéos\Replayo\<Application>\<AAAA-MM>\`. Audio : WASAPI loopback + micro (option),
mix 48 kHz, AAC.

Granularité v1 : le clip démarre sur un début de segment → durée réelle entre N et
N+10 s.

## Performance mesurée

Mesure du 15/07/2026 (script `scripts/mesure-perf.ps1`, 60 s de capture active,
préréglage Équilibré 1080p 60 fps, encodeur matériel actif, 1 écran) :

| Métrique | Mesuré | Objectif |
|---|---|---|
| CPU moyen | **0,93 %** | < 5 % |
| RAM max | **194 Mo** | < 200 Mo |

Clip de contrôle : H.264 Main 1920×1080 + AAC 48 kHz stéréo, assemblage sans
ré-encodage (< 1 s). Reproduire : lancer l'app, puis
`powershell -ExecutionPolicy Bypass -File scripts/mesure-perf.ps1`.
