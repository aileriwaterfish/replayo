using System.Runtime.InteropServices.WindowsRuntime;
using Replayo.Buffer;
using Replayo.Capture;
using Replayo.Core;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;

namespace Replayo.Encoding;

/// Encode le flux de frames GPU en segments MP4 de ~10 s (H.264, VIDÉO SEULE),
/// avec accélération matérielle (NVENC/AMF/QuickSync via Media Foundation).
/// Une session MediaStreamSource+MediaTranscoder par segment : chaque segment
/// démarre sur une keyframe, les frames capturées pendant le redémarrage
/// (~0,28 s, mesuré le 18/08/2026) s'accumulent dans la FrameQueue bornée.
/// « Aucune perte » était écrit ici : c'est faux — la file jette la plus ancienne
/// quand elle sature (FullMode.DropOldest), et la frame qui dépasse DureeSegment
/// est abandonnée sans être encodée.
/// L'audio n'est PAS muxé ici : le transcodeur tire ses flux en pull, et tout
/// couplage audio/vidéo dans ce pipeline s'est avéré ingérable (piste audio qui
/// enfle, ou deadlock, ou frames perdues — vécu). Le mix audio est encodé en
/// continu par AudioMixRecorder et remis au moment du clip (ClipService).
public sealed class SegmentEncoder(FrameQueue frames, SegmentRing ring,
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

        // --- Descripteur d'entrée : vidéo BGRA8 non compressée ---
        var propsVideo = VideoEncodingProperties.CreateUncompressed(
            MediaEncodingSubtypes.Bgra8, (uint)tailleEcran.Width, (uint)tailleEcran.Height);
        var mss = new MediaStreamSource(new VideoStreamDescriptor(propsVideo));
        mss.BufferTime = TimeSpan.Zero; // temps réel, pas de mise en tampon interne

        TimeSpan? origineSegment = null;    // premier timestamp du segment (rebasage à 0)
        TimeSpan originePourRing = default; // timestamp global du début (pour l'anneau)
        bool fini = false;

        mss.SampleRequested += (_, e) =>
        {
            var deferral = e.Request.GetDeferral();
            try
            {
                if (fini) { e.Request.Sample = null; return; }
                var frame = frames.PrendreAsync(ct).AsTask().GetAwaiter().GetResult();
                if (origineSegment is null) { origineSegment = frame.Horodatage; originePourRing = frame.Horodatage; }
                var tsLocal = frame.Horodatage - origineSegment.Value;
                if (tsLocal >= DureeSegment) { fini = true; e.Request.Sample = null; return; }
                HorlogeCapture = frame.Horodatage;
                e.Request.Sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, tsLocal);
            }
            catch (OperationCanceledException) { e.Request.Sample = null; }
            finally { deferral.Complete(); }
        };

        // --- Profil de sortie : H.264 au débit du préréglage, sans piste audio ---
        var profil = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profil.Video!.Width = (uint)largeur;
        profil.Video.Height = (uint)hauteur;
        profil.Video.Bitrate = preset.DebitBitsParSeconde;
        profil.Video.FrameRate.Numerator = (uint)preset.Fps;
        profil.Video.FrameRate.Denominator = 1;
        profil.Audio = null;

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
