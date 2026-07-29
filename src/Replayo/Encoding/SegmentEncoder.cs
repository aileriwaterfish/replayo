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
        var murSegment = System.Diagnostics.Stopwatch.StartNew(); // rythme l'audio (voir plus bas)
        bool fini = false;

        mss.SampleRequested += (_, e) =>
        {
            MediaStreamSourceSampleRequestDeferral? deferral = e.Request.GetDeferral();
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
                    // Le transcodeur tire l'audio aussi vite qu'il peut (pull) alors que la
                    // vidéo est cadencée par les vraies frames : sans garde-fou, la piste
                    // audio enfle de silence fabriqué (15 s d'audio par segment de 10 s) et
                    // la durée du conteneur devient fausse → gels de 5 s au concat des clips.
                    // On rythme donc l'audio sur l'HORLOGE MURALE du segment, via le deferral
                    // (async, sans bloquer le thread de rappel). Jamais sur l'horloge vidéo :
                    // si le MediaStreamSource sérialise ses demandes, attendre la vidéo depuis
                    // la branche audio est un deadlock (vécu : capture gelée après 2 min).
                    // Le mur avance toujours → l'attente est bornée par construction.
                    var requete = e.Request;
                    var deferralAudio = deferral;
                    deferral = null; // complété par la continuation asynchrone
                    Task.Run(async () =>
                    {
                        try
                        {
                            var avance = TimeSpan.FromMilliseconds(200);
                            while (!fini && horlogeAudio > murSegment.Elapsed + avance)
                                await Task.Delay(20, ct);

                            // Ne lire que le retard réel (borné 20 ms – 500 ms).
                            var manque = murSegment.Elapsed + avance - horlogeAudio;
                            var octets = (int)Math.Clamp(manque.TotalSeconds * 48000 * 2 * 2, 3840, 96000) / 4 * 4;
                            var pcm = audio.LirePcmDisponible(octets);
                            if (pcm.Length == 0) pcm = new byte[3840]; // 20 ms de silence : ne jamais bloquer le mux
                            var sample = MediaStreamSample.CreateFromBuffer(pcm.AsBuffer(), horlogeAudio);
                            sample.Duration = TimeSpan.FromSeconds(pcm.Length / (48000.0 * 2 * 2));
                            horlogeAudio += sample.Duration;
                            requete.Sample = sample;
                        }
                        catch (OperationCanceledException) { requete.Sample = null; }
                        finally { deferralAudio.Complete(); }
                    });
                }
            }
            catch (OperationCanceledException) { e.Request.Sample = null; }
            finally { deferral?.Complete(); }
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
