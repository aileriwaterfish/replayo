# Mode LoL — Plan A : détection + séquences + clips

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** après une ranked solo/duo, `%LocalAppData%\Replayo\lol\<horodatage>\` contient les clips des séquences de temps forts + `manifest.json`, sans intervention.

**Architecture:** un `LolModeService` (machine à états Idle→EnGame→Fin) branché sur le `RecorderService` existant ; logique de scoring et de fusion en classes statiques pures (testables headless) ; clips via l'anneau existant (`-c copy`), fenêtre du buffer étendue à 120 s pendant la game.

**Tech Stack:** .NET 8, WPF (app existante), System.Text.Json, System.Management (WMI, ligne de commande LCU), API Riot Live Client (127.0.0.1:2999, cert auto-signé) + LCU (`/lol-gameflow/v1/session`), ffmpeg concat `-c copy`.

## Global Constraints

- Spec : `docs/superpowers/specs/2026-07-29-mode-lol-design.md` (décisions figées).
- File retenue : queueId **420** uniquement. Scores : kill 25, bonus multikill 15/35/55/75, first blood 15, vol dragon 70, vol baron 80, victoire 30, mort 0 (fusable), reste ignoré (−1).
- Séquences : fusion **< 12 s**, fenêtre **6 s avant / 4 s après**, score = somme, séquence à 0 pt jamais émise. Seuil de rétention de la game : **≥ 45 s** cumulées.
- Fenêtre buffer pendant une game : **120 s** ; restaurée à `cfg.DureeBufferSecondes` en fin de game.
- Code et commentaires **en français**, style du dépôt (pas de var inutile, docs `///` courtes).
- Tests : xUnit dans `tests/Replayo.Tests` (projet existant), `dotnet test` doit rester vert à chaque commit.

---

### Task 1 : Événements + scoring (pur)

**Files:**
- Create: `src/Replayo/Lol/EvenementLol.cs`
- Create: `src/Replayo/Lol/ScoreurEvenements.cs`
- Test: `tests/Replayo.Tests/ScoreurEvenementsTests.cs`

**Interfaces:**
- Produces: `record EvenementLol(int Id, string Type, double TempsJeuSec, string? Tueur, string? Victime, string? Beneficiaire, int Serie, bool Vole, string? Resultat)` ; `ScoreurEvenements.Score(EvenementLol, string moi) → int` (−1 = ignoré, 0 = mort fusable, >0 = retenu) ; constante `ScoreurEvenements.NonRetenu = -1`.

- [ ] **Step 1 : test qui échoue** — cas : kill par moi → 25 ; kill de moi (victime) → 0 ; kill entre tiers → −1 ; Multikill série 2/3/4/5 par moi → 15/35/55/75 ; Multikill par un tiers → −1 ; FirstBlood bénéficiaire moi → 15 ; DragonKill par moi volé → 70, non volé → −1 ; BaronKill volé par moi → 80 ; GameEnd Win → 30, Lose → −1.
- [ ] **Step 2 : `dotnet test` → FAIL (types absents)**
- [ ] **Step 3 : implémentation**

```csharp
namespace Replayo.Lol;

/// Événement de l'API Live Client, aplati (seuls les champs utiles au scoring).
public sealed record EvenementLol(
    int Id, string Type, double TempsJeuSec,
    string? Tueur = null, string? Victime = null, string? Beneficiaire = null,
    int Serie = 0, bool Vole = false, string? Resultat = null);

/// Barème des temps forts (spec Mode LoL). Pur, sans état.
public static class ScoreurEvenements
{
    public const int NonRetenu = -1;

    public static int Score(EvenementLol e, string moi) => e.Type switch
    {
        "ChampionKill" when e.Tueur == moi => 25,
        "ChampionKill" when e.Victime == moi => 0, // mort : fusable, ne rapporte rien
        "Multikill" when e.Tueur == moi => e.Serie switch { 2 => 15, 3 => 35, 4 => 55, >= 5 => 75, _ => NonRetenu },
        "FirstBlood" when e.Beneficiaire == moi => 15,
        "DragonKill" when e.Tueur == moi && e.Vole => 70,
        "BaronKill" when e.Tueur == moi && e.Vole => 80,
        "GameEnd" when e.Resultat == "Win" => 30,
        _ => NonRetenu,
    };
}
```

- [ ] **Step 4 : `dotnet test` → PASS**
- [ ] **Step 5 : commit** `LoL: evenements + bareme de scoring`

### Task 2 : Constructeur de séquences (pur)

**Files:**
- Create: `src/Replayo/Lol/ConstructeurSequences.cs`
- Test: `tests/Replayo.Tests/ConstructeurSequencesTests.cs`

**Interfaces:**
- Consumes: rien (pur).
- Produces: `record SequenceLol(TimeSpan Debut, TimeSpan Fin, int Score, IReadOnlyList<TimeSpan> Evenements)` ; `ConstructeurSequences.Construire(IEnumerable<(TimeSpan T, int Score)>, TimeSpan fusion, TimeSpan avant, TimeSpan apres) → List<SequenceLol>` (triées par Debut).

- [ ] **Step 1 : test qui échoue** — cas : deux kills à 8 s d'écart → une séquence [k1−6s, k2+4s] score 50 ; deux kills à 20 s → deux séquences ; kill+mort à 5 s → une séquence score 25 incluant la mort (Fin = mort+4s) ; mort isolée → aucune séquence ; kill à t=2 s → Debut clampé à 0 ; événements non triés en entrée → résultat identique.
- [ ] **Step 2 : FAIL**
- [ ] **Step 3 : implémentation**

```csharp
namespace Replayo.Lol;

/// Séquence de temps forts : fenêtre à clipper (horloge de capture) + score.
public sealed record SequenceLol(TimeSpan Debut, TimeSpan Fin, int Score, IReadOnlyList<TimeSpan> Evenements);

/// Fusionne les événements proches en séquences (spec : < fusion entre événements,
/// fenêtre avant/après, séquence à 0 pt jamais émise — la mort isolée disparaît).
public static class ConstructeurSequences
{
    public static List<SequenceLol> Construire(
        IEnumerable<(TimeSpan T, int Score)> evenements, TimeSpan fusion, TimeSpan avant, TimeSpan apres)
    {
        var tri = evenements.OrderBy(e => e.T).ToList();
        var sequences = new List<SequenceLol>();
        var groupe = new List<(TimeSpan T, int Score)>();

        void Emettre()
        {
            if (groupe.Count == 0) return;
            var score = groupe.Sum(g => g.Score);
            if (score > 0)
            {
                var debut = groupe[0].T - avant;
                if (debut < TimeSpan.Zero) debut = TimeSpan.Zero;
                sequences.Add(new(debut, groupe[^1].T + apres, score, groupe.Select(g => g.T).ToList()));
            }
            groupe.Clear();
        }

        foreach (var e in tri)
        {
            if (groupe.Count > 0 && e.T - groupe[^1].T > fusion) Emettre();
            groupe.Add(e);
        }
        Emettre();
        return sequences;
    }
}
```

- [ ] **Step 4 : PASS** — [ ] **Step 5 : commit** `LoL: fusion des evenements en sequences`

### Task 3 : Anneau — intervalle + fenêtre mutable

**Files:**
- Modify: `src/Replayo/Buffer/SegmentRing.cs`
- Test: `tests/Replayo.Tests/SegmentRingTests.cs` (compléter l'existant s'il y en a un, sinon créer)

**Interfaces:**
- Produces: `SegmentRing.DureeMaxSecondes { get; set; }` (remplace l'usage figé du paramètre du constructeur dans `Ajouter`) ; `SegmentsPourIntervalle(TimeSpan debut, TimeSpan fin) → IReadOnlyList<string>` (segments chevauchant [debut, fin], triés).

- [ ] **Step 1 : test qui échoue** — `SegmentsPourIntervalle` : segments [0-10][10-20][20-30], intervalle [12,18] → seg2 seul ; [8,22] → les trois ; [35,40] → vide. `DureeMaxSecondes` : passer de 20 à 120 → `Ajouter` ne purge plus les segments < 120 s ; revenir à 20 → purge au prochain `Ajouter`.
- [ ] **Step 2 : FAIL** — [ ] **Step 3 : implémentation**

```csharp
/// Fenêtre de rétention, modifiable à chaud (mode LoL : 120 s le temps d'une game).
public int DureeMaxSecondes { get; set; } = dureeMaxSecondes;
// … dans Ajouter : var limite = fin - TimeSpan.FromSeconds(DureeMaxSecondes);

public IReadOnlyList<string> SegmentsPourIntervalle(TimeSpan debut, TimeSpan fin)
{
    lock (_verrou)
        return _entrees.Where(e => e.Fin > debut && e.Debut < fin)
                       .OrderBy(e => e.Debut).Select(e => e.Chemin).ToList();
}
```

- [ ] **Step 4 : PASS** — [ ] **Step 5 : commit** `SegmentRing: intervalle arbitraire + fenetre mutable`

### Task 4 : Clip par intervalle (ClipService + RecorderService)

**Files:**
- Modify: `src/Replayo/Clip/ClipService.cs` (extraire l'assemblage concat en méthode réutilisable)
- Modify: `src/Replayo/RecorderService.cs`

**Interfaces:**
- Consumes: `SegmentsPourIntervalle` (Task 3).
- Produces: `ClipService.AssemblerAsync(IReadOnlyList<string> segments, string sortie) → Task<bool>` (statique, concat `-c copy`, crée le dossier) ; `RecorderService.HorlogeCapture → TimeSpan?` (pipeline principal, null si capture arrêtée) ; `RecorderService.FenetreBuffer(int secondes)` ; `RecorderService.ClipperIntervalleAsync(TimeSpan debut, TimeSpan fin, string sortie) → Task<string?>`.

- [ ] **Step 1 :** extraire de `CreerClipAsync` le bloc « liste concat + ffmpeg + exit code » en `AssemblerAsync(segments, sortie)` statique ; `CreerClipAsync` l'appelle. Comportement identique.
- [ ] **Step 2 :** ajouter à `RecorderService` :

```csharp
/// Horloge de capture du pipeline principal (null si capture arrêtée).
public TimeSpan? HorlogeCapture => _pipelines.Count > 0 ? _pipelines[0].Enc.HorlogeCapture : null;

/// Fenêtre de rétention de tous les anneaux (mode LoL : étendue à 120 s en game).
public void FenetreBuffer(int secondes) { foreach (var p in _pipelines) p.Ring.DureeMaxSecondes = secondes; }

/// Clip d'un intervalle de l'horloge de capture, sans ré-encodage, vers un chemin imposé.
public async Task<string?> ClipperIntervalleAsync(TimeSpan debut, TimeSpan fin, string sortie)
{
    if (!EnCapture || _pipelines.Count == 0) return null;
    var segments = _pipelines[0].Ring.SegmentsPourIntervalle(debut, fin);
    if (segments.Count == 0) return null;
    return await ClipService.AssemblerAsync(segments, sortie) ? sortie : null;
}
```

- [ ] **Step 3 : `dotnet build` + `dotnet test` → PASS** (pas de test unitaire nouveau : ffmpeg/pipelines = intégration ; la logique découpée est testée via Task 3)
- [ ] **Step 4 : commit** `Recorder: clip par intervalle + fenetre buffer pilotable`

### Task 5 : Clients HTTP Riot (Live Client + LCU), parsing pur testé

**Files:**
- Create: `src/Replayo/Lol/LiveClientClient.cs`
- Create: `src/Replayo/Lol/LcuClient.cs`
- Modify: `src/Replayo/Replayo.csproj` (PackageReference `System.Management` 8.0.0)
- Test: `tests/Replayo.Tests/LolParsingTests.cs`

**Interfaces:**
- Produces (parsing pur, statique) : `LiveClientClient.ParserNomJoueur(string json) → string?` (la route renvoie une chaîne JSON littérale, ex. `"Léo#EUW"`) ; `ParserGameTime(string json) → double?` (champ `gameTime` de gamestats) ; `ParserEvenements(string json) → List<EvenementLol>` (champ `Events`, mapping : `EventID`→Id, `EventName`→Type, `EventTime`→TempsJeuSec, `KillerName`→Tueur, `VictimName`→Victime, `Recipient`→Beneficiaire, `KillStreak`→Serie, `Stolen` **chaîne** `"True"`/`"False"`→Vole, `Result`→Resultat) ; `LcuClient.ParserLigneCommande(string cmd) → (int Port, string Token)?` (regex `--app-port=(\d+)` et `--remoting-auth-token=([\w-]+)`).
- Produces (instance, non testé unitairement) : `LiveClientClient.NomJoueurAsync() / GameTimeAsync() / EvenementsAsync()` — GET `https://127.0.0.1:2999/liveclientdata/{activeplayername|gamestats|eventdata}`, `HttpClientHandler.ServerCertificateCustomValidationCallback = (_,_,_,_) => true`, timeout 2 s, null/liste vide si échec ; `LcuClient.QueueIdAsync() → Task<int?>` — WMI `SELECT CommandLine FROM Win32_Process WHERE Name='LeagueClientUx.exe'`, puis GET `https://127.0.0.1:{port}/lol-gameflow/v1/session` (basic auth `riot:{token}`, même bypass cert) → `gameData.queue.id`.

- [ ] **Step 1 : tests qui échouent** sur les 4 parseurs, avec de vrais extraits JSON en constantes (un eventdata contenant ChampionKill + Multikill + DragonKill `"Stolen": "True"` + GameEnd ; une ligne de commande LeagueClientUx réaliste ; gamestats `{"gameMode":"CLASSIC","gameTime":845.2}` ; activeplayername `"\"Léo#EUW\""`).
- [ ] **Step 2 : FAIL** — [ ] **Step 3 : implémentation** (System.Text.Json `JsonDocument`, tolérante : champ absent → valeur par défaut, jamais d'exception sur JSON inattendu — retourner null/vide).
- [ ] **Step 4 : PASS** — [ ] **Step 5 : commit** `LoL: clients Live Client + LCU (parsing pur teste)`

### Task 6 : Manifest de game

**Files:**
- Create: `src/Replayo/Lol/ManifesteLol.cs`
- Test: `tests/Replayo.Tests/ManifesteLolTests.cs`

**Interfaces:**
- Produces: `record SequenceManifeste(string Fichier, int Score, double DebutSec, double FinSec, double[] EvenementsSec)` ; `record ManifesteLol(int Version, DateTime Date, int Queue, bool Victoire, bool Retenue, int DureeCibleMinSec, int DureeCibleMaxSec, int SeuilMinSec, List<SequenceManifeste> Sequences)` avec `Ecrire(string chemin)` et `static Lire(string chemin) → ManifesteLol?` (System.Text.Json indenté). C'est **le contrat consommé par le Plan B** — ne pas renommer les propriétés ensuite.

- [ ] **Step 1 : test aller-retour** Ecrire → Lire → égalité ; Lire sur fichier absent/corrompu → null.
- [ ] **Step 2 : FAIL** — [ ] **Step 3 : implémentation** — [ ] **Step 4 : PASS**
- [ ] **Step 5 : commit** `LoL: manifest de game (contrat du worker de montage)`

### Task 7 : LolModeService + réglage + câblage

**Files:**
- Create: `src/Replayo/Lol/LolModeService.cs`
- Modify: `src/Replayo/Core/ReplayoConfig.cs` (+ `public bool ModeLolActive { get; set; } = true;`)
- Modify: `src/Replayo/UI/SettingsWindow.cs` (case « Mode LoL : condensés automatiques (ranked solo/duo) », sur le modèle des cases audio existantes)
- Modify: `src/Replayo/Program.cs` (instancier/démarrer le service, relayer `Notification` au tray)
- Test: `tests/Replayo.Tests/LolModeServiceTests.cs` (logique extraite pure uniquement)

**Interfaces:**
- Consumes: tout ce qui précède.
- Produces: `LolModeService(RecorderService recorder, Func<ReplayoConfig> cfg)` : `Demarrer()`, `Arreter()`, `event Action<string>? Notification`. Constantes : `FenetreLolSecondes = 120`, `FusionSec = 12`, `AvantSec = 6`, `ApresSec = 4`, `SeuilMinSec = 45`, `DureeCibleMinSec = 60`, `DureeCibleMaxSec = 150`, `QueueSoloDuo = 420`.

Machine à états (timer : 5 s en Idle, 1 s en game) :
1. **Idle** → processus `League of Legends` présent (`Process.GetProcessesByName("League of Legends")`) et `cfg().ModeLolActive` et `recorder.EnCapture` → interroger `LcuClient.QueueIdAsync()` : 420 → **EnGame** (sinon **GameIgnoree** jusqu'à disparition du processus).
2. **Entrée EnGame** : `recorder.FenetreBuffer(120)` ; dossier `%LocalAppData%\Replayo\lol\{DateTime.Now:yyyy-MM-dd_HH\hmm\mss}` ; `moi = NomJoueurAsync()` (retenter tant que null, la game charge).
3. **Poll EnGame** : `EvenementsAsync()` + `GameTimeAsync()` + `recorder.HorlogeCapture`. Pour chaque événement d'`Id` nouveau : `score = ScoreurEvenements.Score(e, moi)` ; si ≥ 0, mémoriser `(tCapture, score)` avec **`tCapture = horlogeCapture − (gameTimeMaintenant − e.TempsJeuSec)` secondes**. `GameEnd` → mémoriser victoire et passer en **Cloture**.
4. **À chaque poll** : `ConstructeurSequences.Construire(mémorisés, 12 s, 6 s, 4 s)` ; toute séquence dont `Fin + fusion < horlogeCapture` et pas encore clippée (clé = Debut) → `recorder.ClipperIntervalleAsync(Debut, Fin, dossier\seq_NN.mp4)` (NN = ordre chronologique), mémoriser le `SequenceManifeste`.
5. **Cloture** (GameEnd reçu, ou processus disparu) : attendre `horlogeCapture > Fin` de la dernière séquence (max 15 s), clipper les séquences restantes, écrire `manifest.json` (`Retenue = somme(Fin−Debut) ≥ 45 s`), `recorder.FenetreBuffer(cfg().DureeBufferSecondes)`, `Notification` (« Condensé LoL : N séquences (M s) prêtes » ou « game sautée : pas assez de temps forts »), retour **Idle** quand le processus a disparu.
- Toute exception dans le poll : loggée `Console.Error`, jamais fatale (le mode LoL ne doit JAMAIS faire tomber la capture).

- [ ] **Step 1 : test qui échoue** sur la seule logique pure extraite : `LolModeService.SequencesClippables(List<SequenceLol>, TimeSpan horloge, IReadOnlySet<TimeSpan> dejaClippees, TimeSpan fusion) → List<SequenceLol>` (statique interne, `InternalsVisibleTo` déjà en place) : séquence finie depuis > fusion → clippable ; déjà clippée → non ; encore ouverte (Fin+fusion ≥ horloge) → non.
- [ ] **Step 2 : FAIL** — [ ] **Step 3 : implémenter la statique, PASS**
- [ ] **Step 4 : implémenter le service complet + config + case Réglages + câblage Program.cs** (après `BrancherRaccourci(cfgCourante)` : `var lol = new LolModeService(recorder, () => cfgCourante); lol.Notification += tray.Notifier; lol.Demarrer();` et `lol.Arreter()` avant `recorder.Arreter()` final).
- [ ] **Step 5 : `dotnet build` + `dotnet test` → PASS ; lancer l'app, vérifier tray OK sans LoL installé/lancé (le service reste en Idle sans erreur).**
- [ ] **Step 6 : commit** `LoL: machine a etats du mode LoL + reglage + cablage`

### Task 8 : validation réelle (manuelle, avec l'utilisateur)

- [ ] Jouer une ranked solo/duo avec Replayo actif ; vérifier : dossier de game créé, clips `seq_NN.mp4` lisibles, `manifest.json` cohérent (scores, victoire), notification en fin de game, fenêtre buffer revenue à la normale (clips Alt+F10 toujours à 20 s).
- [ ] Ajuster ce qui ne colle pas (noms d'événements/format Riot ID réels notamment : le mapping `KillerName` vs `activeplayername` doit être vérifié sur une vraie game — c'est le risque n° 1 du plan).
