namespace Replayo.Capture;

/// Ce que la surveillance demande à l'orchestrateur après une observation.
public enum DecisionSurveillance
{
    /// Rien à faire : l'horloge avance, ou le délai de grâce n'est pas écoulé.
    Rien,
    /// L'horloge de capture est gelée : relancer le pipeline.
    Redemarrer,
    /// Trop de relances infructueuses d'affilée : prévenir l'utilisateur et arrêter d'essayer.
    Abandon,
}

/// Chien de garde de la capture, en logique pure (aucun timer, aucun D3D) pour
/// être testable. On lui donne périodiquement l'horloge de capture vive et l'heure
/// murale ; il dit quand la capture est morte.
///
/// Pourquoi : la session Windows Graphics Capture ne survit pas à une mise en veille
/// du poste. `item.Closed` ne se déclenche pas, aucune exception ne remonte,
/// l'horloge se fige simplement — et Replayo continue de servir un buffer périmé.
/// Vécu les nuits du 16 et du 17/08/2026 : 4 parties de LoL enregistrées sur un
/// bureau figé, sans le moindre avertissement.
///
/// Le seuil est volontairement long : WGC ne livre une frame QUE si l'image change,
/// donc un bureau parfaitement immobile ne produit qu'une frame par minute (le
/// changement de minute de l'horloge). En dessous de ~90 s on prendrait cette
/// immobilité normale pour une panne.
public sealed class SurveillanceCapture(TimeSpan? seuilGel = null, int redemarragesMax = 5)
{
    public static readonly TimeSpan SeuilGelDefaut = TimeSpan.FromSeconds(90);

    private readonly TimeSpan _seuil = seuilGel ?? SeuilGelDefaut;
    private TimeSpan? _derniereHorloge;
    private DateTime _depuis;
    private bool _abandonSignale;

    /// Nombre de relances demandées depuis la dernière fois que l'horloge a avancé.
    public int RedemarragesConsecutifs { get; private set; }

    /// Remet le compteur de grâce à zéro. À appeler au démarrage du pipeline et
    /// après toute relance, pour ne pas juger une capture qui n'a pas encore vécu.
    public void Reinitialiser(DateTime maintenant)
    {
        _derniereHorloge = null;
        _depuis = maintenant;
    }

    /// `horloge` = horloge de capture vive, ou null si la capture est arrêtée.
    public DecisionSurveillance Observer(TimeSpan? horloge, DateTime maintenant)
    {
        // Capture arrêtée volontairement (pause disque, réglages) : ce n'est pas une panne.
        if (horloge is null)
        {
            _derniereHorloge = null;
            _depuis = maintenant;
            return DecisionSurveillance.Rien;
        }

        // Première observation, ou l'horloge a bougé : tout va bien, on repart de zéro.
        if (_derniereHorloge is null || horloge.Value != _derniereHorloge.Value)
        {
            _derniereHorloge = horloge;
            _depuis = maintenant;
            RedemarragesConsecutifs = 0;
            _abandonSignale = false;
            return DecisionSurveillance.Rien;
        }

        // Horloge identique. L'heure murale peut reculer (changement d'heure, NTP) :
        // on repart alors du nouveau repère plutôt que de conclure à une panne.
        var gel = maintenant - _depuis;
        if (gel < TimeSpan.Zero) { _depuis = maintenant; return DecisionSurveillance.Rien; }
        if (gel < _seuil) return DecisionSurveillance.Rien;

        if (RedemarragesConsecutifs >= redemarragesMax)
        {
            if (_abandonSignale) return DecisionSurveillance.Rien;
            _abandonSignale = true;
            return DecisionSurveillance.Abandon;
        }

        // On redonne un délai complet avant de juger la relance qui va suivre.
        RedemarragesConsecutifs++;
        _depuis = maintenant;
        return DecisionSurveillance.Redemarrer;
    }
}
