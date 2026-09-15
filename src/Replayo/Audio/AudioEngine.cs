using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Replayo.Core;

namespace Replayo.Audio;

/// Capture le son système (WASAPI loopback) et/ou le micro, mixe le tout
/// en PCM 16 bits stéréo 48 kHz consommé par l'encodeur segment par segment.
public sealed class AudioEngine : IDisposable
{
    private static readonly WaveFormat FormatCible = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    private sealed record Entree(IWaveIn Capture, ISampleProvider Source, string Nom);

    private readonly List<Entree> _entrees = new();
    private readonly MixingSampleProvider _mixeur = new(FormatCible) { ReadFully = true };
    private readonly object _verrou = new();

    public bool Actif { get { lock (_verrou) return _entrees.Count > 0; } }

    public static AudioEngine? CreerSiActive(ReplayoConfig cfg)
    {
        if (!cfg.AudioSysteme && !cfg.AudioMicro) return null;
        var moteur = new AudioEngine(cfg.AudioSysteme, cfg.AudioMicro);
        if (moteur.Actif) return moteur;
        moteur.Dispose();
        return null;
    }

    public AudioEngine(bool systeme, bool micro)
    {
        if (systeme) EssayerBrancher(() => new WasapiLoopbackCapture(), "son système");
        if (micro)
            EssayerBrancher(() => new WasapiCapture(), "micro"); // périphérique d'entrée par défaut
    }

    /// Une source audio est facultative : un périphérique absent ou un format
    /// inattendu ne doit jamais empêcher la capture vidéo de démarrer.
    internal bool EssayerBrancher(Func<IWaveIn> creerCapture, string nom)
    {
        IWaveIn? capture = null;
        try
        {
            capture = creerCapture();
            Brancher(capture, nom);
            return true;
        }
        catch (Exception e)
        {
            try { capture?.Dispose(); } catch { /* périphérique déjà invalide */ }
            Journal.Ecrire($"[audio] {nom} indisponible : {e.Message}");
            return false;
        }
    }

    private void Brancher(IWaveIn capture, string nom)
    {
        var tampon = new BufferedWaveProvider(capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2),
        };
        capture.DataAvailable += (_, e) => tampon.AddSamples(e.Buffer, 0, e.BytesRecorded);
        var source = Normaliser(tampon.ToSampleProvider());
        lock (_verrou)
        {
            _mixeur.AddMixerInput(source);
            _entrees.Add(new(capture, source, nom));
        }
    }

    /// Ramène toute source au contrat exact exigé par MixingSampleProvider :
    /// IEEE float, 48 kHz, stéréo. Pour une source multicanal, les canaux gauche
    /// et droit sont les deux premiers ; les autres sont ignorés.
    internal static ISampleProvider Normaliser(ISampleProvider source)
    {
        source = source.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(source),
            2 => source,
            > 2 => new DeuxPremiersCanauxSampleProvider(source),
            _ => throw new ArgumentException("La source audio ne contient aucun canal."),
        };
        if (source.WaveFormat.SampleRate != FormatCible.SampleRate)
            source = new WdlResamplingSampleProvider(source, FormatCible.SampleRate);
        return source;
    }

    public void Demarrer()
    {
        Entree[] entrees;
        lock (_verrou) entrees = _entrees.ToArray();
        foreach (var entree in entrees)
        {
            try { entree.Capture.StartRecording(); }
            catch (Exception e)
            {
                RetirerEtNettoyer(entree);
                Journal.Ecrire($"[audio] {entree.Nom} non démarré : {e.Message}");
            }
        }
    }

    public void Arreter()
    {
        Entree[] entrees;
        lock (_verrou) entrees = _entrees.ToArray();
        foreach (var entree in entrees)
            try { entree.Capture.StopRecording(); }
            catch (Exception e) { Journal.Ecrire($"[audio] arrêt {entree.Nom} : {e.Message}"); }
    }

    private void RetirerEtNettoyer(Entree entree)
    {
        lock (_verrou)
        {
            _mixeur.RemoveMixerInput(entree.Source);
            _entrees.Remove(entree);
        }
        try { entree.Capture.Dispose(); } catch { /* périphérique déjà invalide */ }
    }

    /// Lit tout le PCM disponible, converti en 16 bits. Appelé par l'encodeur à son rythme.
    public byte[] LirePcmDisponible(int maxOctets = 48000 * 2 * 2) // ~500 ms
    {
        var floats = new float[maxOctets / 2];
        int lus;
        lock (_verrou) lus = _mixeur.Read(floats, 0, floats.Length);
        var pcm = new byte[lus * 2];
        for (int i = 0; i < lus; i++)
        {
            var v = (short)Math.Clamp(floats[i] * 32767f, short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)v; pcm[i * 2 + 1] = (byte)(v >> 8);
        }
        return pcm;
    }

    public void Dispose()
    {
        Arreter();
        Entree[] entrees;
        lock (_verrou)
        {
            entrees = _entrees.ToArray();
            _mixeur.RemoveAllMixerInputs();
            _entrees.Clear();
        }
        foreach (var entree in entrees)
            try { entree.Capture.Dispose(); } catch { /* périphérique déjà invalide */ }
    }

    private sealed class DeuxPremiersCanauxSampleProvider(ISampleProvider source) : ISampleProvider
    {
        private readonly int _canauxEntree = source.WaveFormat.Channels;
        private float[] _tamponEntree = Array.Empty<float>();

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            var tramesDemandees = count / 2;
            var echantillonsDemandes = tramesDemandees * _canauxEntree;
            if (_tamponEntree.Length < echantillonsDemandes)
                _tamponEntree = new float[echantillonsDemandes];

            var lus = source.Read(_tamponEntree, 0, echantillonsDemandes);
            var tramesLues = lus / _canauxEntree;
            for (var trame = 0; trame < tramesLues; trame++)
            {
                var entree = trame * _canauxEntree;
                var sortie = offset + trame * 2;
                buffer[sortie] = _tamponEntree[entree];
                buffer[sortie + 1] = _tamponEntree[entree + 1];
            }
            return tramesLues * 2;
        }
    }
}
