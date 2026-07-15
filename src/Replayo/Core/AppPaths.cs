namespace Replayo.Core;

/// Chemins de l'application — tout est sous le profil utilisateur, rien en Program Files.
public static class AppPaths
{
    public static string DossierConfig =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Replayo");

    public static string DossierBuffer =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Replayo", "buffer");

    public static string DossierSortieDefaut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Replayo");

    public static string FfmpegExe =>
        Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
}
