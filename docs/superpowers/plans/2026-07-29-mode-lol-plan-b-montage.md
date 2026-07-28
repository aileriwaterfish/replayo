# Mode LoL — Plan B : worker de montage vertical

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** transformer le dossier d'une game (clips `seq_NN.mp4` + `manifest.json`, Plan A) en un condensé vertical 1080×1920 de 60–150 s (cold open + chronologique, cuts francs, fondus audio 200 ms, zéro texte), déposé dans le dossier de la game **et** dans `Vidéos\Replayo\TikTok en attente\`.

**Architecture:** pas de second exécutable — `Replayo.exe --montage <dossierGame>` (même binaire, bypass du mutex, aucun UI, sortie console). Logique pure (sélection, offsets, arguments ffmpeg) dans `Lol/MontageService.cs`, testée unitairement ; l'encodage est un unique appel ffmpeg (filter_complex : trim précis + crop 608:1080 centré + scale 1080:1920 + concat, h264 + aac).

**Tech Stack:** ffmpeg embarqué (`AppPaths.FfmpegExe`), System.Text.Json, xUnit.

## Global Constraints

- Spec : `docs/superpowers/specs/2026-07-29-mode-lol-design.md`. Durée cible 60–150 s ; cold open ~3,5 s sur la meilleure séquence, coupé ~0,5 s avant son dernier événement ; ordre chronologique ensuite ; crop central ; fondu audio 0,2 s en entrée/sortie de chaque plan ; aucun texte, aucune musique.
- Le clip d'une séquence démarre sur une frontière de segment ≤ `DebutSec` : le manifest doit porter le début réel du fichier (`FichierDebutSec`) pour permettre la coupe précise (`-ss = DebutSec − FichierDebutSec`).

---

### Task B1 : contrat — début réel du clip dans le manifest

**Files:** Modify `src/Replayo/Buffer/SegmentRing.cs`, `src/Replayo/RecorderService.cs`, `src/Replayo/Lol/ManifesteLol.cs`, `src/Replayo/Lol/LolModeService.cs` ; Test `tests/Replayo.Tests/SegmentRingTests.cs`, `ManifesteLolTests.cs`.

**Interfaces:** `SegmentRing.IntervalleAvecDebut(TimeSpan debut, TimeSpan fin) → (IReadOnlyList<string> Segments, TimeSpan DebutPremier)` ; `RecorderService.ClipperIntervalleAsync(...) → Task<(string Chemin, TimeSpan DebutReel)?>` ; `SequenceManifeste` gagne `double FichierDebutSec` (après `FinSec`).

- [ ] Test : `IntervalleAvecDebut` sur segments [0-10][10-20][20-30], intervalle [12,18] → DebutPremier = 10 s.
- [ ] Implémenter ; adapter `LolModeService.ClipperAsync` (stocke `DebutReel.TotalSeconds`) ; adapter le test aller-retour du manifest.
- [ ] `dotnet test` vert → commit `LoL: debut reel du clip dans le manifest`.

### Task B2 : logique de montage pure

**Files:** Create `src/Replayo/Lol/MontageService.cs` ; Test `tests/Replayo.Tests/MontageServiceTests.cs`.

**Interfaces:**
- `record PlanDeCoupe(string Fichier, double DepartSec, double DureeSec)` — un plan de la timeline (chemin relatif au dossier game, départ DANS le fichier, durée).
- `MontageService.Selectionner(List<SequenceManifeste> seqs, int maxSec) → List<SequenceManifeste>` : ordre chronologique conservé ; tant que la somme des durées dépasse `maxSec`, retirer la séquence au score le plus faible (à score égal, la plus longue).
- `MontageService.ColdOpen(SequenceManifeste meilleure) → PlanDeCoupe` : fin du teaser = dernier événement − 0,5 s ; départ = fin − 3,5 s, clampé au début du fichier ; meilleure = score max (à égalité, la première).
- `MontageService.Timeline(ManifesteLol m) → List<PlanDeCoupe>` : cold open (si ≥ 2 séquences retenues, sinon pas de teaser) + chaque séquence sélectionnée (`Depart = DebutSec − FichierDebutSec`, `Duree = FinSec − DebutSec`).
- `MontageService.ArgumentsFfmpeg(List<PlanDeCoupe> plans, string dossier, string sortie) → string` : `-ss/-t/-i` par plan, `filter_complex` par entrée `[i:v]crop=608:1080:656:0,scale=1080:1920,setsar=1[vi]` + `[i:a]afade=t=in:d=0.2,afade=t=out:st={duree-0.2}:d=0.2[ai]`, concat `n={N}:v=1:a=1`, sortie `-c:v libx264 -preset veryfast -crf 21 -c:a aac -b:a 160k -movflags +faststart`.

- [ ] Tests : sélection qui coupe au score (3 séquences 60 s chacune, maxSec 150 → la plus faible retirée, ordre chrono conservé) ; cold open clampé ; timeline sans teaser si 1 seule séquence ; arguments ffmpeg contenant `crop=608:1080:656:0` et `concat=n=3`.
- [ ] Implémenter → vert → commit `LoL: logique de montage pure (selection, cold open, ffmpeg)`.

### Task B3 : exécution `--montage` + lancement par le service

**Files:** Modify `src/Replayo/Program.cs` (avant le mutex : si `args[0] == "--montage"`, exécuter `MontageService.ExecuterAsync(args[1])` en console et sortir), `src/Replayo/Lol/MontageService.cs` (+ `ExecuterAsync(string dossierGame) → Task<int>` : lit le manifest, sort 0 si `!Retenue`, construit la timeline, lance ffmpeg, copie le résultat vers `Vidéos\Replayo\TikTok en attente\Replayo_LoL_<date>.mp4`), `src/Replayo/Lol/LolModeService.cs` (fin de game retenue → `Process.Start(Environment.ProcessPath, ...)` fire-and-forget).

- [ ] `Main` accepte `string[] args` ; chemin `--montage` AVANT le mutex (le worker coexiste avec l'app).
- [ ] `dotnet test` vert + exécution manuelle sur manifest de test → commit `LoL: worker de montage --montage + lancement fin de game`.

### Task B4 : validation synthétique de bout en bout

- [ ] Générer 3 « séquences » synthétiques ffmpeg (`testsrc2=duration=…` + `sine`), un manifest cohérent, lancer `Replayo.exe --montage`, vérifier via ffprobe : sortie 1080×1920, durée = somme des plans (±1 s), audio présent. (Script jetable dans le scratchpad, pas commité.)
