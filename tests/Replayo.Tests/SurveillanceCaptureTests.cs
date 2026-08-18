using Replayo.Capture;
using Xunit;

public class SurveillanceCaptureTests
{
    private static readonly DateTime T0 = new(2026, 8, 17, 19, 40, 0, DateTimeKind.Utc);
    private static SurveillanceCapture Neuve(double seuilSec = 90, int max = 5)
        => new(TimeSpan.FromSeconds(seuilSec), max);

    [Fact]
    public void HorlogeQuiAvance_NeDemandeRien()
    {
        var s = Neuve();
        s.Reinitialiser(T0);
        for (int i = 0; i < 100; i++)
            Assert.Equal(DecisionSurveillance.Rien,
                s.Observer(TimeSpan.FromSeconds(i * 2), T0.AddSeconds(i * 2)));
    }

    [Fact]
    public void HorlogeGelee_SousLeSeuil_NeDemandeRien()
    {
        var s = Neuve();
        s.Reinitialiser(T0);
        var gelee = TimeSpan.FromSeconds(12458.452);
        Assert.Equal(DecisionSurveillance.Rien, s.Observer(gelee, T0));
        Assert.Equal(DecisionSurveillance.Rien, s.Observer(gelee, T0.AddSeconds(89)));
    }

    [Fact]
    public void HorlogeGelee_AuDelaDuSeuil_DemandeUnRedemarrage()
    {
        var s = Neuve();
        s.Reinitialiser(T0);
        var gelee = TimeSpan.FromSeconds(12458.452);
        s.Observer(gelee, T0);
        Assert.Equal(DecisionSurveillance.Redemarrer, s.Observer(gelee, T0.AddSeconds(90)));
        Assert.Equal(1, s.RedemarragesConsecutifs);
    }

    /// Le scénario réel : bureau immobile, une frame par minute. Ne doit JAMAIS
    /// être pris pour une panne, sinon on relance la capture en boucle au repos.
    [Fact]
    public void BureauImmobile_UneFrameParMinute_NeDeclenchePas()
    {
        var s = Neuve();
        s.Reinitialiser(T0);
        for (int minute = 0; minute < 120; minute++)
        {
            var horloge = TimeSpan.FromMinutes(minute);
            // 6 observations (toutes les 10 s) sur la même frame, puis la minute change.
            for (int k = 0; k < 6; k++)
                Assert.Equal(DecisionSurveillance.Rien,
                    s.Observer(horloge, T0.AddMinutes(minute).AddSeconds(k * 10)));
        }
    }

    [Fact]
    public void ApresRedemarrage_UnDelaiCompletEstRedonne()
    {
        var s = Neuve();
        s.Reinitialiser(T0);
        var gelee = TimeSpan.FromSeconds(500);
        s.Observer(gelee, T0);
        Assert.Equal(DecisionSurveillance.Redemarrer, s.Observer(gelee, T0.AddSeconds(90)));
        // Juste après, on ne redemande pas immédiatement une autre relance.
        Assert.Equal(DecisionSurveillance.Rien, s.Observer(gelee, T0.AddSeconds(100)));
        Assert.Equal(DecisionSurveillance.Redemarrer, s.Observer(gelee, T0.AddSeconds(180)));
        Assert.Equal(2, s.RedemarragesConsecutifs);
    }

    [Fact]
    public void RelancesInfructueuses_FinissentEnAbandonUneSeuleFois()
    {
        var s = Neuve(seuilSec: 10, max: 3);
        s.Reinitialiser(T0);
        var gelee = TimeSpan.FromSeconds(500);
        s.Observer(gelee, T0);
        for (int i = 1; i <= 3; i++)
            Assert.Equal(DecisionSurveillance.Redemarrer, s.Observer(gelee, T0.AddSeconds(i * 10)));
        Assert.Equal(DecisionSurveillance.Abandon, s.Observer(gelee, T0.AddSeconds(60)));
        // Pas de spam : l'abandon n'est signalé qu'une fois.
        Assert.Equal(DecisionSurveillance.Rien, s.Observer(gelee, T0.AddSeconds(120)));
    }

    [Fact]
    public void AbandonPuisHorlogeQuiRepart_RemetToutAZero()
    {
        var s = Neuve(seuilSec: 10, max: 1);
        s.Reinitialiser(T0);
        var gelee = TimeSpan.FromSeconds(500);
        s.Observer(gelee, T0);
        Assert.Equal(DecisionSurveillance.Redemarrer, s.Observer(gelee, T0.AddSeconds(10)));
        Assert.Equal(DecisionSurveillance.Abandon, s.Observer(gelee, T0.AddSeconds(30)));

        Assert.Equal(DecisionSurveillance.Rien, s.Observer(TimeSpan.FromSeconds(0.5), T0.AddSeconds(40)));
        Assert.Equal(0, s.RedemarragesConsecutifs);
        // Et le cycle peut se redéclencher normalement plus tard.
        var gelee2 = TimeSpan.FromSeconds(0.5);
        Assert.Equal(DecisionSurveillance.Redemarrer, s.Observer(gelee2, T0.AddSeconds(60)));
    }

    [Fact]
    public void CaptureArretee_NEstPasUnePanne()
    {
        var s = Neuve(seuilSec: 10);
        s.Reinitialiser(T0);
        for (int i = 0; i < 20; i++)
            Assert.Equal(DecisionSurveillance.Rien, s.Observer(null, T0.AddSeconds(i * 10)));
    }

    /// Une capture qui démarre morte (aucune frame, horloge à zéro) doit être relancée.
    [Fact]
    public void CaptureNeeMorte_EstDetectee()
    {
        var s = Neuve(seuilSec: 10);
        s.Reinitialiser(T0);
        Assert.Equal(DecisionSurveillance.Rien, s.Observer(TimeSpan.Zero, T0));
        Assert.Equal(DecisionSurveillance.Redemarrer, s.Observer(TimeSpan.Zero, T0.AddSeconds(10)));
    }

    /// Recul de l'heure murale (NTP, passage à l'heure d'hiver) : on ne conclut rien.
    [Fact]
    public void HeureMuraleQuiRecule_NeDeclenchePas()
    {
        var s = Neuve(seuilSec: 10);
        s.Reinitialiser(T0);
        var gelee = TimeSpan.FromSeconds(500);
        s.Observer(gelee, T0);
        Assert.Equal(DecisionSurveillance.Rien, s.Observer(gelee, T0.AddHours(-1)));
        // Le repère a été repris : il faut de nouveau attendre le seuil complet.
        Assert.Equal(DecisionSurveillance.Rien, s.Observer(gelee, T0.AddHours(-1).AddSeconds(5)));
        Assert.Equal(DecisionSurveillance.Redemarrer, s.Observer(gelee, T0.AddHours(-1).AddSeconds(10)));
    }
}
