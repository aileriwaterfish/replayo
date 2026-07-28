using System.Management;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Replayo.Lol;

/// API locale du client League (LCU) : port + token lus sur la ligne de commande du
/// processus LeagueClientUx, basic auth riot:token, certificat auto-signé accepté.
/// Sert uniquement à connaître la file de la game en cours (queueId 420 = solo/duo).
public static partial class LcuClient
{
    [GeneratedRegex(@"(?:^|\s)--app-port=(\d+)")]
    private static partial Regex RegexPort();

    [GeneratedRegex(@"(?:^|\s)--remoting-auth-token=([\w-]+)")]
    private static partial Regex RegexToken();

    public static (int Port, string Token)? ParserLigneCommande(string? cmd)
    {
        if (cmd is null) return null;
        var port = RegexPort().Match(cmd);
        var token = RegexToken().Match(cmd);
        return port.Success && token.Success ? (int.Parse(port.Groups[1].Value), token.Groups[1].Value) : null;
    }

    /// queueId de la session de jeu en cours (null si client absent, pas en game, ou erreur).
    public static async Task<int?> QueueIdAsync()
    {
        var acces = TrouverAcces();
        if (acces is null) return null;
        var (port, token) = acces.Value;

        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"riot:{token}")));
        try
        {
            var json = await http.GetStringAsync($"https://127.0.0.1:{port}/lol-gameflow/v1/session");
            return JsonDocument.Parse(json).RootElement
                .GetProperty("gameData").GetProperty("queue").GetProperty("id").GetInt32();
        }
        catch { return null; }
    }

    private static (int Port, string Token)? TrouverAcces()
    {
        try
        {
            using var recherche = new ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process WHERE Name='LeagueClientUx.exe'");
            foreach (var proc in recherche.Get())
                if (ParserLigneCommande(proc["CommandLine"] as string) is { } acces) return acces;
        }
        catch { /* WMI indisponible : tant pis, pas de mode LoL */ }
        return null;
    }
}
