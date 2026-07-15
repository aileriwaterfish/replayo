# Replayo — Document de design (validé le 15/07/2026)

Logiciel Windows de capture d'écran continue type « Instant Replay » (ShadowPlay) :
un buffer circulaire des N dernières secondes/minutes, clippable rétroactivement par
raccourci clavier. **Priorité absolue : consommation de ressources minimale.**
Produit payant : abonnement 2,99 €/mois ou licence à vie 11,99 €.

## Décisions actées (Phase 1, réponses utilisateur du 15/07/2026)

| Sujet | Décision |
|---|---|
| Nom | **Replayo** |
| Cible | Windows 10 (1903+) / Windows 11 uniquement |
| Stack | **C#/.NET 8 natif** (WPF + tray), un seul processus |
| Capture | **Windows Graphics Capture** (zéro-copie GPU) |
| Encodage | **Matériel obligatoire si disponible** (NVENC / AMF / QuickSync, sélection auto) ; repli logiciel H.264 avec avertissement |
| Codecs | H.264 vidéo + AAC audio |
| Buffer | Circulaire **hybride disque**, réglable **15 s → 20 min**, segments ~10 s |
| Audio | Son système (WASAPI loopback) **et** micro, deux réglages indépendants on/off |
| Raccourci | **Alt+F10** par défaut, reconfigurable |
| Qualité | 3 préréglages : Éco (1080p 30 fps ~8 Mb/s) · Équilibré (natif 60 fps ~20 Mb/s) · Qualité (natif 60 fps ~40 Mb/s) |
| Multi-écrans | Source au choix : n'importe quel écran détecté individuellement, ou tous — en mode « tous » : **un fichier par écran** (généralise le « écran 1 / écran 2 / tous » demandé aux configurations 3 écrans et plus) |
| Format sortie | MP4 ou MKV, choisi à l'onboarding, modifiable dans les réglages |
| Rangement | `Vidéos\Replayo\<Application au premier plan>\<AAAA-MM>\` ; appli indétectable → `Bureau` |
| Nommage | Réglage : nom auto horodaté **ou** boîte de renommage immédiate |
| Licence | **Lemon Squeezy** (2 produits : abonnement + lifetime), aucun serveur maison |
| Dépendances | **ffmpeg.exe embarqué** (assemblage MP4/MKV, LGPL) + **NAudio** (WASAPI, MIT) — approuvées le 15/07/2026 |

## 1. Architecture d'ensemble

Un seul exécutable, un seul processus. Quand l'enregistrement est actif :

```
Écran(s) ──WGC (zéro-copie GPU)──► Encodeur matériel H.264 ──┐
Son système ──WASAPI loopback──► AAC ────────────────────────┤ flux compressés
Micro (option) ──WASAPI──► AAC ──────────────────────────────┤ (jamais décodés)
                                                             ▼
                                     Anneau de segments (~10 s)
                              RAM (segment courant) + disque (buffer)
                                                             │ Alt+F10
                                                             ▼
                              Assemblage ffmpeg « -c copy » (sans ré-encodage)
                                                             ▼
                         Vidéos\Replayo\<App>\<AAAA-MM>\<fichier>.mp4|mkv
                                     + son discret + (option) renommage
```

**Principe de performance** : la vidéo est encodée une seule fois, en continu, par le
GPU. Le CPU ne touche jamais aux pixels. Le clip est un ré-emballage de segments déjà
compressés — coût quasi nul, résultat instantané.

## 2. Composants (une responsabilité chacun)

| Composant | Rôle | Dépend de |
|---|---|---|
| `CaptureEngine` | Un par écran sélectionné : session WGC → frames GPU → encodeur matériel (Media Foundation), sortie = échantillons H.264 compressés | Windows (WGC, MF) |
| `AudioEngine` | Loopback système + micro (NAudio/WASAPI), mix simple, sortie AAC | NAudio |
| `SegmentRing` | Anneau de segments ~10 s : segment courant en RAM, spill sur disque (`%LocalAppData%\Replayo\buffer`), suppression des plus vieux, index horodaté ; purge du dossier au démarrage (résilience crash) | — |
| `ClipService` | À la demande : sélectionne les segments couvrant les N dernières secondes, assemble via ffmpeg `-c copy` vers MP4/MKV, coupe précise au début, nomme et range le fichier, joue le son | SegmentRing, ffmpeg |
| `ForegroundAppTracker` | Nom de l'appli au premier plan au moment du clip (pour le dossier de rangement) | Windows (Win32) |
| `HotkeyManager` | Raccourci global (RegisterHotKey), Alt+F10 par défaut, détection de conflit | Windows (Win32) |
| `LicenseService` | Activation clé Lemon Squeezy (API publique, aucun secret embarqué), revalidation toutes les 24 h, grâce hors-ligne 72 h, verrouillage si invalide/résilié ; clé chiffrée DPAPI dans `%AppData%\Replayo` | API Lemon Squeezy |
| `ConfigStore` | `%AppData%\Replayo\config.json` : tous les réglages utilisateur | — |
| `AutostartManager` | Clé `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, on/off | Windows (registre) |
| UI WPF | Onboarding (1er lancement), fenêtre réglages, icône tray (démarrer/arrêter, ouvrir dossier clips, réglages, quitter) ; fermer la fenêtre = réduction dans le tray | — |

## 3. Flux principaux

**Premier lancement (onboarding)** : clé de licence (activation en ligne obligatoire)
→ format MP4/MKV → préréglage qualité → source (liste des écrans détectés, ou tous) → durée du replay →
capture démarrée. Sans licence valide, l'application est inutilisable (seul l'écran
d'activation est accessible).

**Capture active** : pipelines capture+encodage par écran, segments dans l'anneau,
espace disque borné et affiché dans les réglages (ex. « Équilibré + 5 min ≈ 750 Mo »).

**Clip (Alt+F10)** : gel de l'index → segments des N dernières secondes → ffmpeg
`-c copy` → fichier rangé → son discret → selon réglage : nom auto
(`Replayo_<app>_<AAAA-MM-JJ_HHhMM>`) ou boîte de renommage. En mode « tous les
écrans » : un fichier par écran, même horodatage.

**Arrière-plan** : fermeture de fenêtre = tray, la capture continue. Quitter =
uniquement via le menu du tray.

## 4. Gestion d'erreurs (dégradation propre)

| Situation | Comportement |
|---|---|
| Pas d'encodeur matériel | Repli logiciel H.264 + avertissement « consommation CPU accrue » |
| Écran débranché / pilote GPU réinitialisé | Redémarrage automatique du pipeline concerné |
| Disque presque plein (< 2 Go libres) | Pause capture + notification tray |
| API Lemon Squeezy injoignable | Grâce hors-ligne 72 h, puis verrouillage |
| Abonnement résilié/impayé | Verrouillage sur l'écran d'activation |
| Raccourci déjà utilisé par une autre app | Alerte + proposition d'en choisir un autre |
| Crash/coupure pendant la capture | Buffer purgé au démarrage suivant, aucun fichier corrompu conservé |

## 5. Performance : objectifs mesurables (critère d'acceptation)

- CPU **< 5 %** en capture 1080p60 préréglage Équilibré avec encodeur matériel (< 2 % attendu)
- RAM **< 200 Mo** hors segment courant
- Écritures disque = débit des segments uniquement (aucune autre écriture continue)
- Mesure reproductible : script PowerShell fourni (compteurs de perf sur 60 s), résultats documentés dans le README

## 6. Sécurité et données

- Aucun secret dans le code : l'API de licence Lemon Squeezy côté client est publique (activation/validation par clé) ; la clé utilisateur est chiffrée DPAPI, stockée hors dépôt
- Aucune télémétrie ; rien ne quitte la machine sauf les appels de licence
- Config et buffer exclus du versionnage

## 7. Tests

- **Unitaires** : logique pure — calculs d'anneau (sélection des segments pour N secondes), nommage/arborescence, machine à états de la licence (grâce, expiration)
- **Checklist manuelle de recette**, alignée sur les critères d'acceptation : clip exact de N secondes pendant capture active, CPU mesuré, onboarding complet, choix de source effectif, tray + autostart, son + rangement à chaque clip, blocage sans licence

## 8. Hors périmètre v1 (assumé)

Capture par fenêtre individuelle, overlay en jeu, éditeur vidéo intégré, mise à jour
automatique, localisation autre que français.
