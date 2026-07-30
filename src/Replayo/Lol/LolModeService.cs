using System.Diagnostics;
using Replayo.Core;

namespace Replayo.Lol;

/// Machine à états du mode LoL : détecte une ranked solo/duo, étend la fenêtre du
/// buffer, score les événements Live Client, clippe les séquences au fil de l'eau
/// et écrit le manifest en fin de game. Ne doit JAMAIS faire tomber la capture :
/// toute exception de tick est avalée et loggée.
public sealed class LolModeService(RecorderService recorder, Func<ReplayoConfig> cfg) : IDisposable
{
    public const int FenetreLolSecondes = 120;
    public const int QueueSoloDuo = 420;
    public const int SeuilMinSec = 45;
    public const int DureeCibleMinSec = 60;
    public const int DureeCibleMaxSec = 240; // l'utilisateur préfère du contexte à la brièveté
    public static readonly TimeSpan Fusion = TimeSpan.FromSeconds(18); // escarmouches d'un seul tenant (style IrelKing)
    public static readonly TimeSpan FusionContexte = TimeSpan.FromSeconds(8); // morts et kills alliés : rattachés au play seulement de près
    public static readonly TimeSpan ReculCalmeMax = TimeSpan.FromSeconds(15); // recul max pour ouvrir hors combat
    public static readonly TimeSpan Avant = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Apres = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan AvantMajeur = TimeSpan.FromSeconds(10); // gros play : montrer la rotation/l'engagement
    public const int SeuilMajeur = 50;
    public static readonly TimeSpan ResultatAvant = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan ResultatApres = TimeSpan.FromSeconds(6);
    private const string ProcessusJeu = "League of Legends";

    private enum Etat { Idle, EnGame, Cloture, AttenteFinProcessus }

    private System.Threading.Timer? _timer;
    private int _dansTick;
    private Etat _etat = Etat.Idle;

    // État d'une game en cours.
    private LiveClientClient? _live;
    private AudioLolRecorder? _audio;
    private string? _moi;
    private string _dossier = "";
    private bool _victoire;
    private TimeSpan? _tFinDeGame; // horloge de capture du GameEnd (clip du résultat)
    private TimeSpan? _horlogeCloture;
    private DateTime? _clotureDepuis;
    private readonly List<(TimeSpan T, int Score)> _retenus = new();
    private readonly List<(TimeSpan T, double Pv)> _pv = new(); // PV échantillonnés (détection « déjà en combat »)
    private readonly HashSet<int> _idsVus = new();
    private readonly HashSet<TimeSpan> _clippees = new();
    private readonly List<SequenceManifeste> _manifeste = new();
    private int _numSeq;

    public event Action<string>? Notification;

    public void Demarrer() => _timer = new(async _ => await TickAsync(), null,
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

    public void Arreter() { _timer?.Dispose(); _timer = null; }
    public void Dispose() { Arreter(); _live?.Dispose(); }

    /// Séquences dont plus aucun événement ne peut étendre la fenêtre (Fin + fusion
    /// dépassée) et pas encore clippées (clé : Debut). Pur, testé unitairement.
    internal static List<SequenceLol> SequencesClippables(
        List<SequenceLol> sequences, TimeSpan horloge, IReadOnlySet<TimeSpan> dejaClippees, TimeSpan fusion)
        => sequences.Where(s => s.Fin + fusion < horloge && !dejaClippees.Contains(s.Debut)).ToList();

    private async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _dansTick, 1) == 1) return; // pas de réentrance
        try
        {
            switch (_etat)
            {
                case Etat.Idle: await TickIdleAsync(); break;
                case Etat.EnGame: await TickEnGameAsync(); break;
                case Etat.Cloture: await TickClotureAsync(); break;
                case Etat.AttenteFinProcessus:
                    if (!ProcessusJeuPresent()) _etat = Etat.Idle;
                    break;
            }
        }
        catch (Exception e) { Console.Error.WriteLine($"[lol] tick : {e.Message}"); }
        finally { Interlocked.Exchange(ref _dansTick, 0); }
    }

    private static bool ProcessusJeuPresent() => Process.GetProcessesByName(ProcessusJeu).Length > 0;

    /// Fichier témoin de test : accepte TOUTES les files (validation réelle du
    /// pipeline sur une normale). Supprimer %AppData%\Replayo\test-toutes-files
    /// pour revenir au comportement normal (ranked solo/duo uniquement).
    private static bool ModeTestToutesFiles => File.Exists(Path.Combine(AppPaths.DossierConfig, "test-toutes-files"));

    private async Task TickIdleAsync()
    {
        if (!cfg().ModeLolActive || !recorder.EnCapture || !ProcessusJeuPresent()) return;

        var queue = await LcuClient.QueueIdAsync();
        if (queue is null) return; // client LCU pas prêt : on retentera
        if (queue != QueueSoloDuo && !ModeTestToutesFiles) { _etat = Etat.AttenteFinProcessus; return; }

        // Entrée en game : état neuf, buffer étendu, dossier de la game.
        _live = new LiveClientClient();
        _moi = null; _victoire = false; _tFinDeGame = null; _horlogeCloture = null; _clotureDepuis = null; _numSeq = 0;
        _retenus.Clear(); _idsVus.Clear(); _clippees.Clear(); _manifeste.Clear(); _pv.Clear();
        _dossier = Path.Combine(AppPaths.DossierLol, $"{DateTime.Now:yyyy-MM-dd_HH\\hmm\\mss}");
        Directory.CreateDirectory(_dossier);
        recorder.FenetreBuffer(FenetreLolSecondes);

        // Piste audio « jeu seul » (condensés sans Spotify/Discord/micro).
        // L'origine DOIT être datée avec l'horloge VIVE : la piste, elle, court en
        // temps réel. L'horloge de l'encodeur retarde du remplissage de la FrameQueue
        // (jusqu'à 90 frames = 1,5 s) et le pic de charge de l'écran de chargement la
        // met justement au plus mal → origine trop tôt, son en avance dans le condensé
        // (vécu le 30/07 : 1,552 s d'avance, mesuré par corrélation).
        var pidJeu = Process.GetProcessesByName(ProcessusJeu).FirstOrDefault()?.Id;
        _audio = pidJeu is { } p ? AudioLolRecorder.Demarrer(p, _dossier, () => recorder.HorlogeCaptureLive) : null;

        // Trace des deux horloges : rend le retard de l'encodeur mesurable après coup.
        File.AppendAllText(Path.Combine(_dossier, "events.log"),
            $"# depart audio : live={recorder.HorlogeCaptureLive?.TotalSeconds:F3} " +
            $"encodeur={recorder.HorlogeCapture?.TotalSeconds:F3}\n");

        _etat = Etat.EnGame;
    }

    private async Task TickEnGameAsync()
    {
        if (!ProcessusJeuPresent()) { _etat = Etat.Cloture; return; } // crash ou fermeture

        _moi ??= await _live!.NomJoueurAsync();
        if (_moi is null) return; // la game charge encore

        var gameTime = await _live!.GameTimeAsync();
        // Deux horloges, deux usages : la VIVE date ce qui vient d'arriver (événements,
        // PV) ; celle de l'ENCODEUR dit jusqu'où le buffer est réellement écrit, donc
        // ce qui est clippable. Les confondre décale le condensé (cf. départ audio).
        var horloge = recorder.HorlogeCaptureLive;
        var horlogeEncodee = recorder.HorlogeCapture;
        if (gameTime is null || horloge is null || horlogeEncodee is null) return;

        if (await _live.PvAsync() is { } pv) _pv.Add((horloge.Value, pv));

        foreach (var e in await _live.EvenementsAsync())
        {
            if (!_idsVus.Add(e.Id)) continue;
            var score = ScoreurEvenements.Score(e, _moi);
            // Journal brut : indispensable pour diagnostiquer les formats réels de Riot.
            File.AppendAllText(Path.Combine(_dossier, "events.log"),
                $"{e.Id}\t{e.Type}\tt={e.TempsJeuSec:F1}\ttueur={e.Tueur}\tvictime={e.Victime}\tbenef={e.Beneficiaire}\tserie={e.Serie}\tvole={e.Vole}\tresultat={e.Resultat}\tmoi={_moi}\tscore={score}\n");
            if (score >= 0)
            {
                var t = horloge.Value - TimeSpan.FromSeconds(gameTime.Value - e.TempsJeuSec);
                if (t < TimeSpan.Zero) t = TimeSpan.Zero;
                _retenus.Add((t, score));
            }
            if (e.Type == "GameEnd")
            {
                _victoire = e.Resultat == "Win";
                var tFin = horloge.Value - TimeSpan.FromSeconds(gameTime.Value - e.TempsJeuSec);
                _tFinDeGame = tFin < TimeSpan.Zero ? TimeSpan.Zero : tFin;
                _etat = Etat.Cloture;
            }
        }

        await ClipperAsync(SequencesClippables(Sequences(), horlogeEncodee.Value, _clippees, Fusion));
    }

    private async Task TickClotureAsync()
    {
        var horloge = recorder.HorlogeCapture;
        var sequences = Sequences();
        var derniereFin = sequences.Count > 0 ? sequences.Max(s => s.Fin) : TimeSpan.Zero;
        if (_tFinDeGame is { } tf && tf + ResultatApres > derniereFin) derniereFin = tf + ResultatApres;

        if (horloge is not null)
        {
            _horlogeCloture ??= horloge;
            _clotureDepuis ??= DateTime.UtcNow;
            // Laisser la capture couvrir la fin de la dernière séquence — borne aussi
            // en temps MURAL : si l'horloge de capture est figée (encodeur en panne),
            // on finalise quand même au lieu de boucler sans fin.
            if (horloge < derniereFin && horloge - _horlogeCloture < TimeSpan.FromSeconds(15)
                && DateTime.UtcNow - _clotureDepuis < TimeSpan.FromSeconds(25)) return;
            await ClipperAsync(sequences.Where(s => !_clippees.Contains(s.Debut)).ToList());

            // Clip du résultat (victoire OU défaite) : il clôt toujours la vidéo.
            // GameEnd raté (client fermé trop vite, vécu sur un surrender) → repli sur
            // les derniers instants capturés avant la mort du processus : l'écran de
            // fin y figure, Replayo filme l'écran en continu.
            var (debutRes, finRes) = _tFinDeGame is { } tFin
                ? (tFin - ResultatAvant, tFin + ResultatApres)
                : (_horlogeCloture!.Value - TimeSpan.FromSeconds(12), _horlogeCloture.Value - TimeSpan.FromSeconds(1));
            if (debutRes < TimeSpan.Zero) debutRes = TimeSpan.Zero;
            var fichierRes = $"seq_{++_numSeq:D2}.mp4";
            var clipRes = await recorder.ClipperIntervalleAsync(debutRes, finRes, Path.Combine(_dossier, fichierRes));
            if (clipRes is null) _numSeq--;
            else _manifeste.Add(new(fichierRes, 0, debutRes.TotalSeconds, finRes.TotalSeconds,
                clipRes.Value.DebutReel.TotalSeconds, [finRes.TotalSeconds], EstResultat: true));
        }

        _audio?.Terminer();
        // Le clip du résultat ne compte pas dans le seuil : une game sans temps forts
        // reste sautée même avec sa fin de game.
        var totalSec = _manifeste.Where(s => !s.EstResultat).Sum(s => s.FinSec - s.DebutSec);
        var retenue = totalSec >= SeuilMinSec;
        new ManifesteLol(2, DateTime.Now, QueueSoloDuo, _victoire, retenue,
            DureeCibleMinSec, DureeCibleMaxSec, SeuilMinSec, new(_manifeste),
            _audio is null ? null : AudioLolRecorder.NomFichier, _audio?.DebutCaptureSec)
            .Ecrire(Path.Combine(_dossier, "manifest.json"));
        _audio = null;

        recorder.FenetreBuffer(cfg().DureeBufferSecondes);
        _live?.Dispose(); _live = null;
        Notification?.Invoke(retenue
            ? $"Mode LoL : {_manifeste.Count} séquence(s) ({totalSec:F0} s) — montage lancé."
            : "Mode LoL : game sautée (pas assez de temps forts). Clips bruts conservés.");

        if (retenue) LancerWorkerMontage(_dossier);
        _etat = Etat.AttenteFinProcessus;
    }

    private static void LancerWorkerMontage(string dossier)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--montage \"{dossier}\"")
            { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception e) { Console.Error.WriteLine($"[lol] worker montage : {e.Message}"); }
    }

    private List<SequenceLol> Sequences()
        => ConstructeurSequences.Construire(_retenus, Fusion, Avant, Apres, AvantMajeur, SeuilMajeur, FusionContexte)
            .Select(s => s with { Debut = DebutCalme(s.Debut, _pv) })
            .ToList();

    /// Recule l'ouverture d'une séquence si le joueur était déjà en combat à ce
    /// moment (PV en baisse) : cherche le dernier instant calme (PV stables sur
    /// ~4 s), au plus ReculCalmeMax avant l'ouverture prévue. Une séquence ne
    /// doit jamais s'ouvrir au milieu d'un fight (l'enchaînement devient indigeste).
    internal static TimeSpan DebutCalme(TimeSpan debutPrevu, IReadOnlyList<(TimeSpan T, double Pv)> pv)
    {
        bool CalmeA(TimeSpan t)
        {
            var fenetre = pv.Where(e => e.T >= t - TimeSpan.FromSeconds(4) && e.T <= t).ToList();
            for (int i = 1; i < fenetre.Count; i++)
                if (fenetre[i].Pv < fenetre[i - 1].Pv - 10) return false; // baisse de PV = combat
            return true; // stable, ou pas assez d'échantillons pour conclure
        }

        var t = debutPrevu;
        var limite = debutPrevu - ReculCalmeMax;
        while (t > limite && !CalmeA(t)) t -= TimeSpan.FromSeconds(2);
        if (t < limite) t = limite;
        return t < TimeSpan.Zero ? TimeSpan.Zero : t;
    }

    private async Task ClipperAsync(List<SequenceLol> aClipper)
    {
        foreach (var s in aClipper.OrderBy(s => s.Debut))
        {
            _clippees.Add(s.Debut); // même en échec : ne pas retenter en boucle
            var fichier = $"seq_{++_numSeq:D2}.mp4";
            var clip = await recorder.ClipperIntervalleAsync(s.Debut, s.Fin, Path.Combine(_dossier, fichier));
            if (clip is null) { _numSeq--; continue; } // segments déjà purgés ou capture arrêtée
            _manifeste.Add(new(fichier, s.Score, s.Debut.TotalSeconds, s.Fin.TotalSeconds,
                clip.Value.DebutReel.TotalSeconds, s.Evenements.Select(t => t.TotalSeconds).ToArray()));
        }
    }
}
