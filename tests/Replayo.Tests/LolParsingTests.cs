using Replayo.Lol;
using Xunit;

public class LolParsingTests
{
    private const string EventDataJson = """
    {"Events":[
      {"EventID":0,"EventName":"GameStart","EventTime":0.03},
      {"EventID":3,"EventName":"FirstBlood","EventTime":152.6,"Recipient":"Léo#EUW"},
      {"EventID":4,"EventName":"ChampionKill","EventTime":152.6,"KillerName":"Léo#EUW","VictimName":"Ennemi#XX","Assisters":["Allie#1"]},
      {"EventID":10,"EventName":"Multikill","EventTime":400.1,"KillerName":"Léo#EUW","KillStreak":2},
      {"EventID":12,"EventName":"DragonKill","EventTime":845.2,"DragonType":"Ocean","Stolen":"True","KillerName":"Léo#EUW","Assisters":[]},
      {"EventID":20,"EventName":"GameEnd","EventTime":1900.5,"Result":"Win"}
    ]}
    """;

    [Fact]
    public void ParserEvenements_MappeTousLesChamps()
    {
        var evts = LiveClientClient.ParserEvenements(EventDataJson);
        Assert.Equal(6, evts.Count);

        var kill = evts.Single(e => e.Type == "ChampionKill");
        Assert.Equal(4, kill.Id);
        Assert.Equal(152.6, kill.TempsJeuSec, 3);
        Assert.Equal("Léo#EUW", kill.Tueur);
        Assert.Equal("Ennemi#XX", kill.Victime);

        var multi = evts.Single(e => e.Type == "Multikill");
        Assert.Equal(2, multi.Serie);

        var dragon = evts.Single(e => e.Type == "DragonKill");
        Assert.True(dragon.Vole); // "Stolen" est une CHAÎNE "True"/"False" chez Riot

        var fb = evts.Single(e => e.Type == "FirstBlood");
        Assert.Equal("Léo#EUW", fb.Beneficiaire);

        var fin = evts.Single(e => e.Type == "GameEnd");
        Assert.Equal("Win", fin.Resultat);
    }

    [Fact]
    public void ParserEvenements_JsonInvalide_ListeVide()
    {
        Assert.Empty(LiveClientClient.ParserEvenements("pas du json"));
        Assert.Empty(LiveClientClient.ParserEvenements("{}"));
    }

    [Fact]
    public void ParserGameTime_LitLeChamp()
        => Assert.Equal(845.2, LiveClientClient.ParserGameTime("""{"gameMode":"CLASSIC","gameTime":845.2,"mapName":"Map11"}""")!.Value, 3);

    [Fact]
    public void ParserGameTime_JsonInvalide_Null()
        => Assert.Null(LiveClientClient.ParserGameTime("oops"));

    [Fact]
    public void ParserNomJoueur_ChaineJsonLitterale()
        => Assert.Equal("Léo#EUW", LiveClientClient.ParserNomJoueur("\"Léo#EUW\""));

    [Fact]
    public void ParserLigneCommande_ExtraitPortEtToken()
    {
        var cmd = "\"C:\\Riot Games\\League of Legends\\LeagueClientUx.exe\" --riotclient-app-port=51234 " +
                  "--app-port=52716 --remoting-auth-token=k9Zx-abc_DEF --app-pid=1234";
        var res = LcuClient.ParserLigneCommande(cmd);
        Assert.NotNull(res);
        Assert.Equal(52716, res.Value.Port);
        Assert.Equal("k9Zx-abc_DEF", res.Value.Token);
    }

    [Fact]
    public void ParserLigneCommande_Incomplete_Null()
        => Assert.Null(LcuClient.ParserLigneCommande("LeagueClientUx.exe --app-port=1234"));
}
