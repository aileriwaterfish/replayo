using System.Diagnostics;
using Replayo.Audio;
using Replayo.Core;

namespace Replayo.Lol;

/// Piste audio « jeu seul » d'une game : process loopback du processus LoL,
/// encodée au fil de l'eau par ffmpeg en audio_lol.m4a dans le dossier de la game.
/// Sans Spotify, Discord ni micro — c'est la piste des condensés TikTok.
public sealed class AudioLolRecorder : IDisposable
{
    public const string NomFichier = "audio_lol.m4a";

    private readonly ProcessLoopbackCapture _capture;
    private readonly Process _ffmpeg;

    /// Horloge de capture au démarrage effectif de la piste (pour la synchro montage).
    public double DebutCaptureSec { get; }

    private AudioLolRecorder(ProcessLoopbackCapture capture, Process ffmpeg, double debutCaptureSec)
    { _capture = capture; _ffmpeg = ffmpeg; DebutCaptureSec = debutCaptureSec; }

    /// Null si le démarrage échoue (OS trop ancien, ffmpeg absent…) : le mode LoL
    /// continue sans piste isolée, le montage retombera sur le mix complet.
    public static AudioLolRecorder? Demarrer(int pidJeu, string dossierGame, Func<TimeSpan?> horloge)
    {
        try
        {
            var sortie = Path.Combine(dossierGame, NomFichier);
            var psi = new ProcessStartInfo(AppPaths.FfmpegExe,
                $"-hide_banner -loglevel error -f f32le -ar 48000 -ac 2 -i pipe:0 -c:a aac -b:a 160k -y \"{sortie}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
            var ffmpeg = Process.Start(psi)!;

            var capture = new ProcessLoopbackCapture(pidJeu);
            var flux = ffmpeg.StandardInput.BaseStream;
            capture.EchantillonsRecus += (octets, n) =>
            {
                try { flux.Write(octets, 0, n); }
                catch { /* ffmpeg parti : la capture s'arrêtera au Dispose */ }
            };
            capture.Demarrer();

            return new AudioLolRecorder(capture, ffmpeg, horloge()?.TotalSeconds ?? 0);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[lol] piste audio jeu indisponible : {e.Message}");
            return null;
        }
    }

    /// Arrête proprement : fin de la capture puis fermeture du pipe (ffmpeg finalise le m4a).
    public void Terminer()
    {
        _capture.Dispose();
        try { _ffmpeg.StandardInput.Close(); _ffmpeg.WaitForExit(5000); } catch { /* déjà fermé */ }
    }

    public void Dispose() => Terminer();
}
