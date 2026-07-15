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

## Lancer (milestone A — runner console)

```powershell
dotnet run --project src/Replayo -c Release
# Alt+F10 → clip des N dernières secondes ; Ctrl+C → quitter
```

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
