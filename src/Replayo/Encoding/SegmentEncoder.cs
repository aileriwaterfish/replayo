using System.Runtime.InteropServices.WindowsRuntime;
using Replayo.Audio;
using Replayo.Buffer;
using Replayo.Capture;
using Replayo.Core;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;

namespace Replayo.Encoding;

/// Encode le flux de frames GPU en segments MP4 de ~10 s (H.264 + AAC),
/// avec accélération matérielle (NVENC/AMF/QuickSync via Media Foundation).
/// Une session MediaStreamSource+MediaTranscoder par segment : chaque segment
/// démarre sur une keyframe, les frames en attente pendant le bref redémarrage
/// s'accumulent dans la FrameQueue bornée (aucune perte, timestamps continus).
public sealed class SegmentEncoder(FrameQueue frames, AudioEngine? audio, SegmentRing ring,
                                   QualityPreset preset, SizeInt32 tailleEcran)
{
    private static readonly TimeSpan DureeSegment = TimeSpan.FromSeconds(10);
    public bool EncodageMateriel { get; private set; } = true;
    public TimeSpan HorlogeCapture { get; private set; }

    public async Task BoucleEncodageAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await EncoderUnSegmentAsync(ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task EncoderUnSegmentAsync(CancellationToken ct)
    {
        int largeur = preset.Largeur ?? tailleEcran.Width;
        int hauteur = preset.Hauteur ?? tailleEcran.Height;

        // --- Descripteurs d'entrée : vidéo BGRA8 non compressée + PCM 16 bits ---
        var propsVideo = VideoEncodingProperties.CreateUncompressed(
            MediaEncodingSubtypes.Bgra8, (uint)tailleEcran.Width, (uint)tailleEcran.Height);
        var descVideo = new VideoStreamDescriptor(propsVideo);

        AudioStreamDescriptor? descAudio = null;
        if (audio is { Actif: true })
            descAudio = new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(48000, 2, 16));

        var mss = descAudio is null ? new MediaStreamSource(descVideo)
                                    : new MediaStreamSource(descVideo, descAudio);
        mss.BufferTime = TimeSpan.Zero; // temps réel, pas de mise en tampon interne

        TimeSpan? origineSegment = null;    // premier timestamp du segment (rebasage à 0)
        TimeSpan originePourRing = default; // timestamp global du début (pour l'anneau)
        TimeSpan horlogeAudio = default;
        bool fini = false;

        mss.SampleRequested += (_, e) =>
        {
            var deferral = e.Request.GetDeferral();
            try
            {
                if (e.Request.StreamDescriptor is VideoStreamDescriptor)
                {
                    if (fini) { e.Request.Sample = null; return; }
                    var frame = frames.PrendreAsync(ct).AsTask().GetAwaiter().GetResult();
                    if (origineSegment is null) { origineSegment = frame.Horodatage; originePourRing = frame.Horodatage; }
                    var tsLocal = frame.Horodatage - origineSegment.Value;
                    if (tsLocal >= DureeSegment) { fini = true; e.Request.Sample = null; return; }
                    HorlogeCapture = frame.Horodatage;
                    e.Request.Sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, tsLocal);
                }
                else if (audio is not null)
                {
                    if (fini) { e.Request.Sample = null; return; }
                    var pcm = audio.LirePcmDisponible();
                    if (pcm.Length == 0) pcm = new byte[9600]; // 50 ms de silence : ne jamais bloquer le mux
                    var sample = MediaStreamSample.CreateFromBuffer(pcm.AsBuffer(), horlogeAudio);
                    sample.Duration = TimeSpan.FromSeconds(pcm.Length / (48000.0 * 2 * 2));
                    horlogeAudio += sample.Duration;
                    e.Request.Sample = sample;
                }
            }
            catch (OperationCanceledException) { e.Request.Sample = null; }
            finally { deferral.Complete(); }
        };

        // --- Profil de sortie : H.264 + AAC au débit du préréglage ---
        var profil = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profil.Video!.Width = (uint)largeur;
        profil.Video.Height = (uint)hauteur;
        profil.Video.Bitrate = preset.DebitBitsParSeconde;
        profil.Video.FrameRate.Numerator = (uint)preset.Fps;
        profil.Video.FrameRate.Denominator = 1;
        if (descAudio is null) profil.Audio = null;

        var chemin = ring.ProchainCheminSegment();
        using var fichier = new FileStream(chemin, FileMode.Create, FileAccess.ReadWrite);
        using var flux = fichier.AsRandomAccessStream();

        var transcodeur = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prep = await transcodeur.PrepareMediaStreamSourceTranscodeAsync(mss, flux, profil);
        if (!prep.CanTranscode)
        {
            // Repli logiciel : on retente sans accélération matérielle (avertissement au runner).
            EncodageMateriel = false;
            transcodeur.HardwareAccelerationEnabled = false;
            prep = await transcodeur.PrepareMediaStreamSourceTranscodeAsync(mss, flux, profil);
            if (!prep.CanTranscode) throw new InvalidOperationException("Aucun encodeur H.264 disponible.");
        }
        await prep.TranscodeAsync().AsTask(ct);

        ring.Ajouter(chemin, originePourRing, HorlogeCapture);
    }
}
