# Mode LoL — condensés TikTok automatiques — Design

Date : 2026-07-29
Statut : validé (grilling complet avec l'utilisateur, décisions figées)

## Objectif

Quand l'utilisateur joue une **ranked solo/duo** de League of Legends, Replayo
détecte la game, clippe automatiquement les temps forts depuis sa **capture
d'écran brute (sa vraie POV)**, monte un condensé **vertical 9:16 de 1 min à
2 min 30** (style Irelking, sans texte incrusté, sans musique), et l'envoie en
**brouillon TikTok**. Zéro intervention PC : la validation se fait dans l'app
TikTok mobile (son tendance + description + publier).

## Décisions produit (figées — ne pas rediscuter sans l'utilisateur)

- **Pipeline 100 % automatique** : une game = un brouillon. Pas d'étape de
  validation côté PC.
- **Files** : ranked **solo/duo uniquement** (queueId 420). Pas de flex, pas
  de normale, pas d'ARAM, pas de modes rotatifs, pas de customs/bots.
- **POV brute exigée** : la capture écran de l'utilisateur, jamais le replay
  rejoué par le moteur du jeu (option explicitement rejetée : « les gens
  doivent voir comment JE joue »). Écran capturé : l'écran principal (confirmé,
  il y joue).
- **Temps forts scorés, centrés sur le joueur** :
  - kill solo 25 pts ; multikill = bonus (double +15, triple +35, quadra +55,
    penta +75, cumulé au kill → 40/60/80/100) ; first blood +15 ;
    vol de dragon 70 / vol de baron 80 ; moment de victoire 30.
  - **Morts = 0 pt mais fusionnables** : une mort dans l'action prolonge la
    séquence (le 1v3 où il tue 2 et meurt sur le 3ᵉ se montre EN ENTIER, mort
    comprise). Une mort isolée (séquence à 0 pt) n'est jamais retenue.
  - Exclus : assists, tours, dragons/barons non volés, morts seules.
    (Shutdowns : souhaités mais l'API Live Client ne les expose pas — hors
    périmètre tant que Riot ne le fournit pas.)
- **Séquences** : événements distants de **< 18 s** fusionnés (élargi de 12 à
  18 s après analyse d'une vidéo IrelKing : les escarmouches restent d'un seul
  tenant) ; fenêtre de **5 s avant / 5 s après** (réglage utilisateur, après
  itérations 6/4 → 10/7 → 8/5 → 5/5), **sauf gros play (score ≥ 50) : 10 s
  avant** pour montrer la rotation et l'engagement (mise en scène
  proportionnelle, style IrelKing) ; score = somme des événements.
- **Style de montage validé sur référence** (vidéo IrelKing analysée le 29/07 :
  4 cuts en 25 min, aucun zoom/slow-mo/transition, fights toujours entiers,
  fin de game en clôture) : cuts secs, zéro artifice, continuité des fights.
  Ses sous-titres de commentaire et sa capture spectateur/replay ne sont PAS
  repris (exclus par l'utilisateur : pas de texte, POV brute).
- **Le résultat de la game clôt TOUJOURS la vidéo** (révision du 29/07 au
  soir) : clip dédié de la fin de game (~6 s avant / 6 s après le GameEnd),
  victoire **ou défaite**, jamais éjecté par le tri (son temps est déduit du
  budget des autres séquences), jamais utilisé comme cold open, et exclu du
  seuil des 45 s.
- **Montage** : cold open de 3-4 s sur la meilleure séquence **coupée avant sa
  résolution**, puis les séquences dans l'ordre chronologique. Cuts francs,
  fondu audio ~200 ms entre séquences, **aucun texte incrusté**, aucune
  musique (le son tendance est ajouté par l'utilisateur dans TikTok).
  **Cadrage « zoom réduit » (validé par l'utilisateur le 29/07 au soir)** :
  carré central 1080×1080 (~56 % de la largeur — le champion sort rarement du
  cadre en caméra libre) affiché pleine largeur du 9:16, bandes du même
  gameplay flouté en haut/bas. Le crop serré 608×1080 (zoom fort) a été
  comparé et écarté ; le tracking du champion par vision reste une V2
  possible si besoin.
- **Durée** : 1 min à **4 min** (portée de 2:30 à 4:00 le 29/07 au soir —
  l'utilisateur préfère le contexte à la brièveté). Game trop riche → tri par
  score jusqu'à 4 min (remises en ordre chronologique). Game trop pauvre
  (< **45 s** de séquences cumulées, résultat non compté) → **sautée** : pas de
  vidéo, notification discrète, clips bruts conservés en local.
- **Audio du condensé : le son du jeu uniquement** — piste dédiée capturant le
  **processus LoL seul** (process loopback WASAPI). L'utilisateur écoute
  Spotify et est en vocal Discord en jouant : le mix système intégral est donc
  interdit dans le condensé (copyright + voix privées). Micro exclu aussi.
  **Les clips Alt+F10 personnels gardent le mix complet actuel** (système +
  micro) — deux pistes encodées en parallèle pendant une game LoL.
- **Livraison : manuelle, par dossiers (décision révisée le 29/07/2026).**
  L'API TikTok (Content Posting) a été abandonnée par l'utilisateur — la
  bureaucratie de l'app développeur ne valait pas le gain. Chaque condensé est
  copié dans `Vidéos\Replayo\TikTok en attente\` et, si OneDrive est présent,
  dans `OneDrive\Replayo TikTok\` (synchro iPhone → app OneDrive → Photos →
  upload TikTok depuis l'app). Jamais d'automatisation navigateur.

## Architecture

```
Replayo (processus principal, capture)
├── LolModeService (nouveau) : machine à états
│   Idle → détecte le client de jeu LoL → vérifie queueId 420 via LCU
│        → EnGame : étend la fenêtre du buffer à ~120 s,
│                   poll l'API Live Client (127.0.0.1:2999) chaque seconde,
│                   score les événements, construit les séquences,
│                   clippe chaque séquence close depuis l'anneau (-c copy)
│        → FinDeGame : manifest.json + retour buffer normal
│                      + lance le worker de montage
└── (existant) CaptureEngine → SegmentEncoder → SegmentRing → ClipService

Replayo.Montage (worker séparé, lancé en fin de game)
└── manifest.json → sélection par score → cold open → crop 9:16 →
    concat ré-encodé → condensé final → upload TikTok (ou dossier d'attente)
```

- **Détection d'événements** : API Riot **Live Client Data**
  (`https://127.0.0.1:2999/liveclientdata/…`, certificat auto-signé accepté) —
  `activeplayername`, `gamestats` (gameTime), `eventdata` (ChampionKill,
  Multikill, FirstBlood, DragonKill/BaronKill avec `Stolen`, GameEnd).
- **File ranked** : API **LCU** du client League (lockfile via la ligne de
  commande du processus LeagueClientUx : port + token, basic auth `riot:token`)
  → `/lol-gameflow/v1/session` → `gameData.queue.id == 420`.
- **Synchronisation des horloges** : à chaque poll,
  `tCapture(événement) = HorlogeCapture(maintenant) − (gameTime(maintenant) − eventTime)`.
  Aucune horloge murale nécessaire.
- **Clips de séquences** : réutilisation de l'anneau existant —
  `SegmentsPourIntervalle(début, fin)` + concat ffmpeg `-c copy` (comme
  Alt+F10). Précision au segment de 10 s : marge acceptée au clip, la coupe
  précise est faite par le worker de montage (qui ré-encode de toute façon).
- **Sortie par game** : `%LocalAppData%\Replayo\lol\<AAAA-MM-JJ_HHhMMmSS>\`
  contenant `seq_NN.mp4` + `manifest.json` (version, file, victoire, séquences
  avec scores/temps/offsets, seuils). Le manifest est **le contrat** entre
  Replayo et le worker.

## Phasage (plans séparés, chacun livrable seul)

- **Plan A — noyau détection + séquences + clips** : après une ranked, le
  dossier de la game contient les clips de séquences + manifest. Pas encore de
  montage. Notification « X séquences prêtes ». (Sans plan C, les clips ont le
  mix audio complet — le worker ne publie rien tant que C n'est pas là, donc
  aucun risque de fuite Spotify/Discord sur TikTok.)
- **Plan B — worker de montage** (`src/Replayo.Montage`, console) :
  manifest → condensé vertical fini. Testable sur n'importe quel manifest.
- **Plan C — piste audio « LoL seul »** : process loopback WASAPI
  (AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK, interop — NAudio ne le fournit
  pas), deuxième piste AAC dans les segments ou fichier .m4a parallèle ; le
  montage bascule dessus. Prérequis pour publier.
- **Plan D — upload TikTok** : OAuth + upload inbox/brouillon + dossier
  d'attente + notification. Nécessite la création de l'app développeur par
  l'utilisateur (à lancer tôt : validation TikTok en jours).

Ordre : A → B → C → D. B/C/D indépendants entre eux une fois A livré.

## Réglages

- `ModeLolActive` (bool, défaut **true**) dans `ReplayoConfig` + case dans les
  Réglages. Le mode ne s'active que si la capture tourne et couvre l'écran
  principal.

## Hors périmètre

- Autres jeux que LoL. Autres files que la 420. ARAM/URF/Arena.
- Ré-enregistrement via replay du moteur (rejeté définitivement).
- Texte incrusté, musique incrustée, sous-titres, watermark.
- Publication directe (toujours brouillon).
- Tracking du champion par vision (crop central assumé, caméra libre).
