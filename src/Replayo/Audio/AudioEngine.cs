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
    private readonly List<IWaveIn> _captures = new();
    private readonly MixingSampleProvider _mixeur = new(FormatCible) { ReadFully = true };
    private readonly object _verrou = new();

    public bool Actif => _captures.Count > 0;

    public static AudioEngine? CreerSiActive(ReplayoConfig cfg)
        => cfg.AudioSysteme || cfg.AudioMicro ? new AudioEngine(cfg.AudioSysteme, cfg.AudioMicro) : null;

    public AudioEngine(bool systeme, bool micro)
    {
        if (systeme) Brancher(new WasapiLoopbackCapture());
        if (micro)
        {
            try { Brancher(new WasapiCapture()); } // périphérique d'entrée par défaut
            catch { /* pas de micro branché : on continue sans, jamais de crash */ }
        }
    }

    private void Brancher(IWaveIn capture)
    {
        var tampon = new BufferedWaveProvider(capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2),
        };
        capture.DataAvailable += (_, e) => tampon.AddSamples(e.Buffer, 0, e.BytesRecorded);
        ISampleProvider source = tampon.ToSampleProvider();
        if (capture.WaveFormat.SampleRate != 48000)
            source = new WdlResamplingSampleProvider(source, 48000);
        if (source.WaveFormat.Channels == 1)
            source = new MonoToStereoSampleProvider(source);
        lock (_verrou) _mixeur.AddMixerInput(source);
        _captures.Add(capture);
    }

    public void Demarrer() { foreach (var c in _captures) c.StartRecording(); }
    public void Arreter() { foreach (var c in _captures) c.StopRecording(); }

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

    public void Dispose() { Arreter(); foreach (var c in _captures) c.Dispose(); }
}
