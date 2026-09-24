using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Replayo.Core;

internal sealed record MiseAJour(Version Version, Uri Archive, Uri Empreintes);

/// Vérifie les Releases GitHub et prépare un paquet validé avant tout arrêt de l'app.
internal sealed class UpdateService
{
    internal const string NomArchive = "Replayo-win-x64.zip";
    internal const string NomEmpreintes = "SHA256SUMS.txt";
    private const string Api = "https://api.github.com/repos/aileriwaterfish/replayo/releases/latest";
    private static readonly HttpClient Http = CreerClient();
    private static readonly string[] Fichiers =
        ["Replayo.exe", "ffmpeg.exe", "assets/clip.wav", "assets/replayo.ico", "assets/tray.ico"];

    internal static Version VersionInstallee => typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0);

    private static HttpClient CreerClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Replayo", "0.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    internal async Task<MiseAJour?> VerifierAsync(CancellationToken ct = default)
    {
        using var attente = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attente.CancelAfter(TimeSpan.FromSeconds(15));
        using var reponse = await Http.GetAsync(Api, attente.Token).ConfigureAwait(false);
        if (reponse.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // aucune Release encore
        reponse.EnsureSuccessStatusCode();
        var json = await reponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return LireRelease(json, VersionInstallee);
    }

    internal static MiseAJour? LireRelease(string json, Version versionInstallee)
    {
        using var doc = JsonDocument.Parse(json);
        var release = doc.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString();
        if (tag is null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version)
            || version <= versionInstallee) return null;

        Uri? archive = null, empreintes = null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var nom = asset.GetProperty("name").GetString();
            if (nom is not (NomArchive or NomEmpreintes)) continue;
            if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url)
                || url.Scheme != Uri.UriSchemeHttps || url.Host != "github.com"
                || !url.AbsolutePath.StartsWith($"/aileriwaterfish/replayo/releases/download/{tag}/", StringComparison.Ordinal)
                || !url.AbsolutePath.EndsWith('/' + nom, StringComparison.Ordinal)) return null;
            if (nom == NomArchive) archive = url;
            else empreintes = url;
        }
        return archive is not null && empreintes is not null ? new MiseAJour(version, archive, empreintes) : null;
    }

    internal async Task<string> PreparerAsync(MiseAJour maj, CancellationToken ct = default)
    {
        var dossier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Replayo", "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);
        try
        {
            var empreintes = await Http.GetStringAsync(maj.Empreintes, ct).ConfigureAwait(false);
            var empreinteAttendue = LireEmpreinte(empreintes);
            var archive = Path.Combine(dossier, NomArchive);
            using (var reponse = await Http.GetAsync(maj.Archive, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                reponse.EnsureSuccessStatusCode();
                if (reponse.Content.Headers.ContentLength > 300_000_000)
                    throw new InvalidDataException("Archive de mise à jour trop grande.");
                await using var source = await reponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var cible = File.Create(archive);
                var tampon = new byte[81920];
                long taille = 0;
                int lu;
                while ((lu = await source.ReadAsync(tampon, ct).ConfigureAwait(false)) != 0)
                {
                    taille += lu;
                    if (taille > 300_000_000) throw new InvalidDataException("Archive de mise à jour trop grande.");
                    await cible.WriteAsync(tampon.AsMemory(0, lu), ct).ConfigureAwait(false);
                }
            }
            await using (var fichier = File.OpenRead(archive))
            {
                var empreinte = Convert.ToHexString(await SHA256.HashDataAsync(fichier, ct).ConfigureAwait(false));
                if (!empreinte.Equals(empreinteAttendue, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Empreinte SHA-256 de la mise à jour incorrecte.");
            }
            var contenu = Path.Combine(dossier, "contenu");
            await Task.Run(() => ExtraireArchive(archive, contenu), ct).ConfigureAwait(false);
            File.Copy(Environment.ProcessPath ?? throw new InvalidOperationException("Exécutable introuvable."),
                Path.Combine(dossier, "updater.exe"));
            return dossier;
        }
        catch
        {
            Directory.Delete(dossier, recursive: true);
            throw;
        }
    }

    internal static string LireEmpreinte(string manifeste)
    {
        foreach (var ligne in manifeste.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var morceaux = ligne.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (morceaux.Length == 2 && morceaux[1] == NomArchive && morceaux[0].Length == 64
                && morceaux[0].All(Uri.IsHexDigit)) return morceaux[0];
        }
        throw new InvalidDataException("Empreinte SHA-256 absente du paquet.");
    }

    internal static void ExtraireArchive(string archive, string sortie)
    {
        using var zip = ZipFile.OpenRead(archive);
        var vus = new HashSet<string>(StringComparer.Ordinal);
        long taille = 0;
        Directory.CreateDirectory(sortie);
        foreach (var entree in zip.Entries)
        {
            if (!Fichiers.Contains(entree.FullName, StringComparer.Ordinal) || !vus.Add(entree.FullName))
                throw new InvalidDataException($"Fichier inattendu dans la mise à jour : {entree.FullName}");
            taille += entree.Length;
            if (taille > 300_000_000) throw new InvalidDataException("Contenu de mise à jour trop grand.");
            var chemin = Path.Combine(sortie, entree.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
            entree.ExtractToFile(chemin);
        }
        if (vus.Count != Fichiers.Length) throw new InvalidDataException("Paquet de mise à jour incomplet.");
    }

    internal static void LancerInstallation(string dossier)
    {
        var updater = Path.Combine(dossier, "updater.exe");
        var info = new ProcessStartInfo(updater) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--apply-update");
        info.ArgumentList.Add(dossier);
        info.ArgumentList.Add(AppContext.BaseDirectory);
        info.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _ = Process.Start(info) ?? throw new InvalidOperationException("Impossible de lancer l'installation.");
    }
}
