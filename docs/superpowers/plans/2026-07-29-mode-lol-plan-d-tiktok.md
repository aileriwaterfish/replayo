# Mode LoL — Plan D : upload en brouillon TikTok

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** en fin de montage, envoyer le condensé sur le compte TikTok **@aileri24** en **brouillon** (Content Posting API, mode inbox) ; l'utilisateur le finalise dans l'app mobile. Échec → la vidéo reste dans `Vidéos\Replayo\TikTok en attente\` + notification.

**Préalable humain (bloquant, à faire par l'utilisateur — voir `docs/tiktok-app-guide.md`) :** créer l'app sur developers.tiktok.com, activer le scope `video.upload` (Content Posting API, mode « Direct Post » non requis : l'inbox suffit et ne demande pas l'audit), récupérer Client Key + Client Secret, puis faire UNE connexion OAuth (le flux ouvre le navigateur).

**Architecture :** `Lol/TikTokClient.cs` — OAuth v2 (authorization code + PKCE, redirection `http://127.0.0.1:{port}/callback` via `HttpListener`), tokens stockés dans `%AppData%\Replayo\tiktok.json` (refresh automatique : l'access token dure 24 h, le refresh token 1 an). Upload : `POST /v2/post/publish/inbox/video/init/` (`source_info.source = FILE_UPLOAD`, taille + chunks) puis PUT des chunks (64 Mo max) puis poll `/v2/post/publish/status/fetch/`. Déclenché à la fin de `MontageService.ExecuterAsync` si des identifiants existent ; sinon message console « upload non configuré ».

**Étapes clés :**

- [ ] **D1 — stockage identifiants + refresh** : `TikTokClient` (Client Key/Secret lus depuis `%AppData%\Replayo\tiktok.json`, jamais commités) ; tests sur la sérialisation et l'expiration.
- [ ] **D2 — flux OAuth one-shot** : `Replayo.exe --tiktok-login` (ouvre le navigateur, capte le code sur 127.0.0.1, échange, stocke). À exécuter une fois avec l'utilisateur.
- [ ] **D3 — upload inbox** : init + chunks + poll ; à brancher en fin de `ExecuterAsync` ; en cas d'échec réseau/quota, garder le fichier en attente + code retour dédié.
- [ ] **D4 — file d'attente** : au démarrage de Replayo, si `TikTok en attente\` contient des vidéos et que les identifiants marchent, proposer (notification) de les envoyer.
- [ ] **NE PAS activer l'upload automatique tant que le Plan C (audio LoL seul) n'est pas livré** — les condensés actuels contiennent le mix complet (musique/Discord).
