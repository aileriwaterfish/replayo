using Replayo.Core;
using Xunit;

public class ConfigStoreTests
{
    [Fact]
    public void Charger_SansFichier_RendLesDefauts()
    {
        var store = new ConfigStore(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var cfg = store.Charger();
        Assert.Equal(300, cfg.DureeBufferSecondes);
        Assert.Equal("equilibre", cfg.Preset);
        Assert.Equal("mp4", cfg.FormatSortie);
        Assert.True(cfg.AudioSysteme);
        Assert.False(cfg.AudioMicro);
    }

    [Fact]
    public void EnregistrerPuisCharger_ConserveLesValeurs()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var store = new ConfigStore(dir);
        var cfg = store.Charger();
        cfg.DureeBufferSecondes = 1200; // 20 min = borne max
        cfg.FormatSortie = "mkv";
        store.Enregistrer(cfg);
        Assert.Equal(1200, new ConfigStore(dir).Charger().DureeBufferSecondes);
        Assert.Equal("mkv", new ConfigStore(dir).Charger().FormatSortie);
    }

    [Fact]
    public void Charger_BorneLaDureeEntre5Et1200Secondes()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var store = new ConfigStore(dir);
        var cfg = store.Charger();
        cfg.DureeBufferSecondes = 99999;
        store.Enregistrer(cfg);
        Assert.Equal(1200, new ConfigStore(dir).Charger().DureeBufferSecondes);

        cfg.DureeBufferSecondes = 2;
        store.Enregistrer(cfg);
        Assert.Equal(5, new ConfigStore(dir).Charger().DureeBufferSecondes);

        cfg.DureeBufferSecondes = 9; // sous l'ancien minimum de 15 : doit passer tel quel
        store.Enregistrer(cfg);
        Assert.Equal(9, new ConfigStore(dir).Charger().DureeBufferSecondes);
    }

    [Fact]
    public void Charger_RaccourciParDefaut_AltF10()
    {
        var store = new ConfigStore(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var cfg = store.Charger();
        Assert.Equal(0x1u, cfg.RaccourciModificateurs);
        Assert.Equal(0x79u, cfg.RaccourciTouche);
    }

    [Fact]
    public void Existe_VraiSeulementApresEnregistrement()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var store = new ConfigStore(dir);
        Assert.False(store.Existe);
        store.Enregistrer(store.Charger());
        Assert.True(store.Existe);
    }

    [Theory]
    [InlineData("eco", 1920, 1080, 30, 8_000_000u)]
    [InlineData("qualite", null, null, 60, 40_000_000u)]
    public void QualityPreset_DepuisNom(string nom, int? l, int? h, int fps, uint debit)
    {
        var p = QualityPreset.DepuisNom(nom);
        Assert.Equal(l, p.Largeur);
        Assert.Equal(h, p.Hauteur);
        Assert.Equal(fps, p.Fps);
        Assert.Equal(debit, p.DebitBitsParSeconde);
    }
}
