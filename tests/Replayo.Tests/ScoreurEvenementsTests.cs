using Replayo.Lol;
using Xunit;

public class ScoreurEvenementsTests
{
    private const string Moi = "Léo#EUW";

    [Fact]
    public void KillParMoi_25()
        => Assert.Equal(25, ScoreurEvenements.Score(new(1, "ChampionKill", 100, Tueur: Moi, Victime: "Ennemi#1"), Moi));

    [Fact]
    public void KillParMoi_SansTagDansLEvenement_25()
        => Assert.Equal(25, ScoreurEvenements.Score(new(1, "ChampionKill", 100, Tueur: "Léo", Victime: "Ennemi"), Moi));

    [Fact]
    public void KillParUnHomonymeAvecAutreTag_ReconnuQuandMemePrefixe()
        => Assert.True(ScoreurEvenements.MemeJoueur("Léo#NA1", Moi)); // limite assumée : préfixe identique = moi

    [Fact]
    public void MaMort_0_Fusable()
        => Assert.Equal(0, ScoreurEvenements.Score(new(2, "ChampionKill", 100, Tueur: "Ennemi#1", Victime: Moi), Moi));

    [Fact]
    public void KillEntreTiers_ContexteFusable_0()
        => Assert.Equal(0, ScoreurEvenements.Score(new(3, "ChampionKill", 100, Tueur: "A#1", Victime: "B#2"), Moi));

    [Theory]
    [InlineData(2, 15)]
    [InlineData(3, 35)]
    [InlineData(4, 55)]
    [InlineData(5, 75)]
    public void MultikillParMoi_Bonus(int serie, int attendu)
        => Assert.Equal(attendu, ScoreurEvenements.Score(new(4, "Multikill", 100, Tueur: Moi, Serie: serie), Moi));

    [Fact]
    public void MultikillParUnTiers_Ignore()
        => Assert.Equal(ScoreurEvenements.NonRetenu, ScoreurEvenements.Score(new(5, "Multikill", 100, Tueur: "A#1", Serie: 3), Moi));

    [Fact]
    public void FirstBloodPourMoi_15()
        => Assert.Equal(15, ScoreurEvenements.Score(new(6, "FirstBlood", 100, Beneficiaire: Moi), Moi));

    [Fact]
    public void DragonVoleParMoi_70()
        => Assert.Equal(70, ScoreurEvenements.Score(new(7, "DragonKill", 100, Tueur: Moi, Vole: true), Moi));

    [Fact]
    public void DragonNonVole_Ignore()
        => Assert.Equal(ScoreurEvenements.NonRetenu, ScoreurEvenements.Score(new(8, "DragonKill", 100, Tueur: Moi, Vole: false), Moi));

    [Fact]
    public void BaronVoleParMoi_80()
        => Assert.Equal(80, ScoreurEvenements.Score(new(9, "BaronKill", 100, Tueur: Moi, Vole: true), Moi));

    [Theory]
    [InlineData("Win")]
    [InlineData("Lose")]
    public void GameEnd_JamaisScore_LeResultatEstUnClipDedie(string resultat)
        => Assert.Equal(ScoreurEvenements.NonRetenu, ScoreurEvenements.Score(new(10, "GameEnd", 100, Resultat: resultat), Moi));
}
