using System.Net.Http;
using System.Text.Json;

namespace Replayo.Lol;

/// API Riot « Live Client Data » (https://127.0.0.1:2999) : événements de la game
/// en cours, temps de jeu, nom du joueur actif. Certificat auto-signé accepté.
/// Toute erreur réseau/JSON → null ou liste vide, jamais d'exception.
public sealed class LiveClientClient : IDisposable
{
    private const string Base = "https://127.0.0.1:2999/liveclientdata";
    private readonly HttpClient _http;

    public LiveClientClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
    }

    public async Task<string?> NomJoueurAsync() => ParserNomJoueur(await LireAsync("activeplayername"));
    public async Task<double?> GameTimeAsync() => ParserGameTime(await LireAsync("gamestats"));
    public async Task<List<EvenementLol>> EvenementsAsync() => ParserEvenements(await LireAsync("eventdata"));

    private async Task<string> LireAsync(string route)
    {
        try { return await _http.GetStringAsync($"{Base}/{route}"); }
        catch { return ""; } // jeu absent ou en chargement : réponse vide
    }

    public static string? ParserNomJoueur(string json)
    {
        try { return JsonDocument.Parse(json).RootElement.GetString(); }
        catch { return null; }
    }

    public static double? ParserGameTime(string json)
    {
        try
        {
            return JsonDocument.Parse(json).RootElement.TryGetProperty("gameTime", out var t)
                ? t.GetDouble() : null;
        }
        catch { return null; }
    }

    public static List<EvenementLol> ParserEvenements(string json)
    {
        var evts = new List<EvenementLol>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Events", out var events)) return evts;
            foreach (var e in events.EnumerateArray())
            {
                string? Chaine(string nom) => e.TryGetProperty(nom, out var v) ? v.GetString() : null;
                evts.Add(new(
                    Id: e.TryGetProperty("EventID", out var id) ? id.GetInt32() : 0,
                    Type: Chaine("EventName") ?? "",
                    TempsJeuSec: e.TryGetProperty("EventTime", out var t) ? t.GetDouble() : 0,
                    Tueur: Chaine("KillerName"),
                    Victime: Chaine("VictimName"),
                    Beneficiaire: Chaine("Recipient"),
                    Serie: e.TryGetProperty("KillStreak", out var s) ? s.GetInt32() : 0,
                    Vole: Chaine("Stolen") == "True", // chaîne, pas booléen, chez Riot
                    Resultat: Chaine("Result")));
            }
        }
        catch { /* JSON inattendu : on rend ce qu'on a */ }
        return evts;
    }

    public void Dispose() => _http.Dispose();
}
