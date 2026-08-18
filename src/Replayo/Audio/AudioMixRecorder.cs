using System.Diagnostics;
using Replayo.Core;

namespace Replayo.Audio;

/// Encode le mix audio (système + micro) en continu dans un fichier AAC/ADTS à
/// côté du buffer vidéo, cadencé par l'horloge murale : chaque ~100 ms, on lit
/// exactement le temps écoulé dans le mixeur (qui complète en silence si besoin)
/// → la durée de la piste suit le temps réel par construction. Le format ADTS
/// (.aac) est lisible pendant l'écriture, contrairement au m4a (moov à la fin) :
/// les clips peuvent y découper leur audio en pleine session.
public sealed class AudioMixRecorder : IDisposable
{
    private const int OctetsParSeconde = 48000 * 2 * 2; // PCM16 stéréo 48 kHz

    private readonly AudioEngine _audio;
    private readonly Process _ffmpeg;
    private readonly Thread _boucle;
    private volatile bool _actif = true;

    public string Chemin { get; }

    private AudioMixRecorder(AudioEngine audio, Process ffmpeg, string chemin)
    {
        _audio = audio;
        _ffmpeg = ffmpeg;
        Chemin = chemin;
        _boucle = new Thread(Boucle) { IsBackground = true, Name = "replayo-audio-mix" };
        _boucle.Start();
    }

    /// Null si ffmpeg indisponible : la capture continue simplement sans audio.
    public static AudioMixRecorder? Demarrer(AudioEngine audio, string chemin)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
            var psi = new ProcessStartInfo(AppPaths.FfmpegExe,
                $"-hide_banner -loglevel error -f s16le -ar 48000 -ac 2 -i pipe:0 -c:a aac -b:a 192k -f adts -y \"{chemin}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
            return new AudioMixRecorder(audio, Process.Start(psi)!, chemin);
        }
        catch (Exception e)
        {
            Journal.Ecrire($"[audio] piste mix indisponible : {e.Message}");
            return null;
        }
    }

    private void Boucle()
    {
        var flux = _ffmpeg.StandardInput.BaseStream;
        var mur = Stopwatch.StartNew();
        var dejaLu = 0L; // octets déjà écrits, pour caler la lecture sur le mur
        while (_actif)
        {
            Thread.Sleep(100);
            var cible = (long)(mur.Elapsed.TotalSeconds * OctetsParSeconde) / 4 * 4;
            var aLire = (int)Math.Min(cible - dejaLu, OctetsParSeconde); // borne 1 s (rattrapage)
            if (aLire <= 0) continue;
            var pcm = _audio.LirePcmDisponible(aLire);
            if (pcm.Length == 0) continue;
            dejaLu += pcm.Length;
            try { flux.Write(pcm, 0, pcm.Length); }
            catch { _actif = false; } // ffmpeg parti : on arrête proprement
        }
        try { _ffmpeg.StandardInput.Close(); _ffmpeg.WaitForExit(3000); } catch { /* déjà fermé */ }
    }

    public void Dispose()
    {
        _actif = false;
        _boucle.Join(2000);
        try { if (!_ffmpeg.HasExited) _ffmpeg.Kill(); } catch { /* déjà parti */ }
        _ffmpeg.Dispose();
    }
}
