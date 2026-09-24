using System.IO.Compression;
using Replayo.Core;

namespace Replayo.Tests;

public sealed class UpdateServiceTests
{
    private const string Release = """
        {"tag_name":"v0.2.0","draft":false,"prerelease":false,"assets":[
          {"name":"Replayo-win-x64.zip","browser_download_url":"https://github.com/aileriwaterfish/replayo/releases/download/v0.2.0/Replayo-win-x64.zip"},
          {"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/aileriwaterfish/replayo/releases/download/v0.2.0/SHA256SUMS.txt"}]}
        """;

    [Fact]
    public void ReleasePlusRecente_EstProposee()
    {
        var maj = UpdateService.LireRelease(Release, new Version(0, 1, 0));
        Assert.Equal(new Version(0, 2, 0), maj?.Version);
    }

    [Fact]
    public void VersionIdentique_NEstPasProposee()
        => Assert.Null(UpdateService.LireRelease(Release, new Version(0, 2, 0)));

    [Fact]
    public void ArchiveHorsDepot_EstRejetee()
        => Assert.Null(UpdateService.LireRelease(
            Release.Replace("github.com/aileriwaterfish/replayo", "github.com/inconnu/replayo"),
            new Version(0, 1, 0)));

    [Fact]
    public void Empreinte_LitUniquementLePaquetAttendu()
    {
        var hash = new string('a', 64);
        Assert.Equal(hash, UpdateService.LireEmpreinte($"{hash}  Replayo-win-x64.zip\n"));
        Assert.Throws<InvalidDataException>(() => UpdateService.LireEmpreinte($"{hash}  autre.zip\n"));
    }

    [Fact]
    public void ArchiveExtraite_EtFichierInattenduRejete()
    {
        var racine = Path.Combine(Path.GetTempPath(), "ReplayoUpdateTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(racine);
        try
        {
            var archive = Path.Combine(racine, "release.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                foreach (var nom in new[] { "Replayo.exe", "ffmpeg.exe", "assets/clip.wav", "assets/replayo.ico", "assets/tray.ico" })
                {
                    var entree = zip.CreateEntry(nom);
                    using var writer = new StreamWriter(entree.Open());
                    writer.Write(nom);
                }
            var sortie = Path.Combine(racine, "contenu");
            UpdateService.ExtraireArchive(archive, sortie);
            Assert.True(File.Exists(Path.Combine(sortie, "Replayo.exe")));
            Assert.True(File.Exists(Path.Combine(sortie, "assets", "tray.ico")));

            var mauvais = Path.Combine(racine, "mauvais.zip");
            using (var zip = ZipFile.Open(mauvais, ZipArchiveMode.Create))
                zip.CreateEntry("../echappe.txt");
            Assert.Throws<InvalidDataException>(() => UpdateService.ExtraireArchive(mauvais, Path.Combine(racine, "mauvais")));
            Assert.False(File.Exists(Path.Combine(racine, "echappe.txt")));
        }
        finally { Directory.Delete(racine, recursive: true); }
    }

    [Fact]
    public void Installation_RemplaceLesBinairesEtPreserveLesAutresFichiers()
    {
        var racine = Path.Combine(Path.GetTempPath(), "ReplayoUpdateTest-" + Guid.NewGuid().ToString("N"));
        var contenu = Path.Combine(racine, "contenu");
        var installation = Path.Combine(racine, "installation");
        Directory.CreateDirectory(contenu);
        Directory.CreateDirectory(installation);
        try
        {
            foreach (var nom in new[] { "Replayo.exe", "ffmpeg.exe", "assets/clip.wav", "assets/replayo.ico", "assets/tray.ico" })
            {
                var chemin = Path.Combine(contenu, nom.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
                File.WriteAllText(chemin, "nouveau");
            }
            File.WriteAllText(Path.Combine(installation, "Replayo.exe"), "ancien");
            File.WriteAllText(Path.Combine(installation, "config.json"), "préférence");
            UpdateInstaller.Appliquer(contenu, installation, Path.Combine(racine, "sauvegarde"));
            Assert.Equal("nouveau", File.ReadAllText(Path.Combine(installation, "Replayo.exe")));
            Assert.Equal("préférence", File.ReadAllText(Path.Combine(installation, "config.json")));
            Assert.Equal("ancien", File.ReadAllText(Path.Combine(racine, "sauvegarde", "Replayo.exe")));
        }
        finally { Directory.Delete(racine, recursive: true); }
    }

    [Fact]
    public void Installation_Echouee_RestaureLePremierFichier()
    {
        var racine = Path.Combine(Path.GetTempPath(), "ReplayoUpdateTest-" + Guid.NewGuid().ToString("N"));
        var contenu = Path.Combine(racine, "contenu");
        var installation = Path.Combine(racine, "installation");
        Directory.CreateDirectory(contenu);
        Directory.CreateDirectory(installation);
        try
        {
            foreach (var nom in new[] { "Replayo.exe", "ffmpeg.exe", "assets/clip.wav", "assets/replayo.ico", "assets/tray.ico" })
            {
                var chemin = Path.Combine(contenu, nom.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
                File.WriteAllText(chemin, "nouveau");
            }
            var exe = Path.Combine(installation, "Replayo.exe");
            File.WriteAllText(exe, "ancien");
            var ffmpeg = Path.Combine(installation, "ffmpeg.exe");
            File.WriteAllText(ffmpeg, "occupé");
            using (var verrou = new FileStream(ffmpeg, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.ThrowsAny<IOException>(() => UpdateInstaller.Appliquer(
                    contenu, installation, Path.Combine(racine, "sauvegarde")));
            Assert.Equal("ancien", File.ReadAllText(exe));
        }
        finally { Directory.Delete(racine, recursive: true); }
    }
}
