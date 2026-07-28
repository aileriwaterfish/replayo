# Mode LoL — Plan C : piste audio « processus LoL uniquement »

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** pendant une game LoL, capturer en parallèle une piste audio contenant **uniquement le son du jeu** (sans Spotify, Discord ni micro), et faire basculer le montage dessus. **Prérequis pour publier sur TikTok** (l'utilisateur écoute de la musique et est en vocal en jouant). Ses clips Alt+F10 gardent le mix complet actuel.

**Architecture:** capture WASAPI **process loopback** (Windows 10 20H2+) : `ActivateAudioInterfaceAsync` avec `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK` ciblant le PID de `League of Legends.exe` (mode `INCLUDE_TARGET_PROCESS_TREE`). NAudio ne le fournit pas → interop COM à écrire (~200 lignes). La piste est encodée en `.m4a` par game (fichier unique `audio_lol.m4a` démarré à l'activation du mode, horodaté sur l'horloge de capture), pas dans les segments (les segments restent inchangés).

**Étapes clés :**

- [ ] **C1 — interop process loopback** : `Audio/ProcessLoopbackCapture.cs` — `ActivateAudioInterfaceAsync` (P/Invoke `mmdeviceapi`), `AUDIOCLIENT_ACTIVATION_PARAMS` + `PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE`, format float 48 kHz, événements de remplissage → callback d'échantillons. Référence : exemple Microsoft « ApplicationLoopback » (SDK samples GitHub). Test : capturer 3 s du PID d'un lecteur audio de test, vérifier des échantillons non nuls.
- [ ] **C2 — enregistreur de game** : `Lol/AudioLolRecorder.cs` — démarré par `LolModeService` à l'entrée en game (PID du processus jeu), pipe vers ffmpeg (`-f f32le -ar 48000 -ac 2 -i pipe:` → AAC `audio_lol.m4a`), stoppé en clôture. Noter `AudioDebutCaptureSec` (horloge de capture au premier échantillon) dans le manifest (`ManifesteLol.Version` → 2, champ optionnel — rester lisible pour les manifests v1).
- [ ] **C3 — bascule du montage** : `MontageService.Timeline/ArgumentsFfmpeg` — si `audio_lol.m4a` présent : entrée supplémentaire, l'audio de chaque plan est pris dans `audio_lol.m4a` à `DebutSec − AudioDebutCaptureSec` (durée du plan), les pistes des clips vidéo sont ignorées (`-map` vidéo seule + audio du m4a, mêmes fondus 0,2 s). Sinon : comportement actuel (mix complet) + avertissement console.
- [ ] **C4 — validation réelle** : une game avec Spotify + Discord actifs ; vérifier que le condensé ne contient que le jeu et que le clip Alt+F10 contient tout.

**Risques :** interop délicate (HRESULT, formats) ; le process loopback ne capture que si le jeu émet via le périphérique par défaut ; LoL a plusieurs processus (client jeu = « League of Legends.exe », c'est lui qui émet le son du jeu).
