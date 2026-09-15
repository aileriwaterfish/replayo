using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Replayo.Audio;

[Collection("Journal")]
public class AudioEngineTests
{
    [Fact]
    public void Normaliser_Mono48k_DupliqueChaqueEchantillon()
    {
        var source = new TableauSampleProvider(48000, 1, [0.25f, -0.5f]);

        var normalisee = AudioEngine.Normaliser(source);
        var sortie = new float[4];
        var lus = normalisee.Read(sortie, 0, sortie.Length);

        Assert.Equal(4, lus);
        Assert.Equal([0.25f, 0.25f, -0.5f, -0.5f], sortie);
        AssertFormatCible(normalisee.WaveFormat);
    }

    [Fact]
    public void Normaliser_Multicanal_GardeLesDeuxPremiersCanaux()
    {
        var source = new TableauSampleProvider(48000, 6,
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);

        var normalisee = AudioEngine.Normaliser(source);
        var sortie = new float[4];
        var lus = normalisee.Read(sortie, 0, sortie.Length);

        Assert.Equal(4, lus);
        Assert.Equal([1f, 2f, 7f, 8f], sortie);
        AssertFormatCible(normalisee.WaveFormat);
    }

    [Fact]
    public void Normaliser_FrequenceDifferente_ProduitLeFormatExactDuMixeur()
    {
        var mono44100 = AudioEngine.Normaliser(new TableauSampleProvider(44100, 1, [0, 0]));
        var stereo44100 = AudioEngine.Normaliser(new TableauSampleProvider(44100, 2, [0, 0]));
        var multicanal44100 = AudioEngine.Normaliser(new TableauSampleProvider(44100, 6, [0, 0, 0, 0, 0, 0]));
        var mixeur = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));

        mixeur.AddMixerInput(mono44100);
        mixeur.AddMixerInput(stereo44100);
        mixeur.AddMixerInput(multicanal44100);

        AssertFormatCible(mono44100.WaveFormat);
        AssertFormatCible(stereo44100.WaveFormat);
        AssertFormatCible(multicanal44100.WaveFormat);
    }

    [Fact]
    public void Demarrer_EntreeQuiEchoue_EstRetireeEtNettoyee()
    {
        var capture = new FausseCapture(echecAuDemarrage: true);
        using var moteur = new AudioEngine(systeme: false, micro: false);
        Assert.True(moteur.EssayerBrancher(() => capture, "test"));

        moteur.Demarrer();

        Assert.False(moteur.Actif);
        Assert.True(capture.Disposee);
    }

    [Fact]
    public void Brancher_EntreeInvalide_EstNettoyeeSansException()
    {
        var capture = new FausseCapture(formatInvalide: true);
        using var moteur = new AudioEngine(systeme: false, micro: false);

        var resultat = moteur.EssayerBrancher(() => capture, "test");

        Assert.False(resultat);
        Assert.False(moteur.Actif);
        Assert.True(capture.Disposee);
    }

    private static void AssertFormatCible(WaveFormat format)
    {
        Assert.Equal(WaveFormatEncoding.IeeeFloat, format.Encoding);
        Assert.Equal(48000, format.SampleRate);
        Assert.Equal(2, format.Channels);
        Assert.Equal(32, format.BitsPerSample);
    }

    private sealed class TableauSampleProvider(int frequence, int canaux, float[] echantillons) : ISampleProvider
    {
        private int _position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(frequence, canaux);

        public int Read(float[] buffer, int offset, int count)
        {
            var disponibles = Math.Min(count, echantillons.Length - _position);
            Array.Copy(echantillons, _position, buffer, offset, disponibles);
            _position += disponibles;
            return disponibles;
        }
    }

    private sealed class FausseCapture(bool echecAuDemarrage = false, bool formatInvalide = false) : IWaveIn
    {
        private WaveFormat _format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public bool Disposee { get; private set; }
        public WaveFormat WaveFormat
        {
            get => formatInvalide ? throw new InvalidOperationException("format indisponible") : _format;
            set => _format = value;
        }

#pragma warning disable CS0067
        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;
#pragma warning restore CS0067

        public void StartRecording()
        {
            if (echecAuDemarrage) throw new InvalidOperationException("périphérique indisponible");
        }

        public void StopRecording() { }
        public void Dispose() => Disposee = true;
    }
}
