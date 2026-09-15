using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Replayo.Capture;

/// Capture continue d'un écran via Windows Graphics Capture.
/// Les frames restent des surfaces GPU (zéro-copie) jusqu'à l'encodeur.
public sealed class CaptureEngine(MonitorInfo ecran) : IDisposable
{
    private IDirect3DDevice? _device;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private TimeSpan _origine = TimeSpan.MinValue;
    private int _captureActive;

    public FrameQueue Frames { get; } = new();
    public SizeInt32 Taille { get; private set; }
    public event Action? CaptureInterrompue;

    /// Horloge de capture VIVE : horodatage de la dernière frame capturée, donc
    /// « maintenant » sur la timeline des segments. À utiliser pour dater un
    /// événement temps réel (mode LoL). Ne pas confondre avec
    /// `SegmentEncoder.HorlogeCapture`, qui suit la frame que l'encodeur vient de
    /// tirer et accuse donc le retard de la FrameQueue (~1,5 s à 60 fps).
    public TimeSpan HorlogeLive { get; private set; }

    public void Demarrer()
    {
        try
        {
            _device = D3DHelper.CreerDeviceWinRT();
            _item = CaptureItemFactory.DepuisEcran(ecran.Handle);
            Taille = _item.Size;
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _item.Size);
            _pool.FrameArrived += SurFrame;
            _item.Closed += SurCibleFermee;
            _session = _pool.CreateCaptureSession(_item);
            _session.IsCursorCaptureEnabled = true;
            DesactiverBordure(_session);
            Volatile.Write(ref _captureActive, 1);
            _session.StartCapture();
        }
        catch
        {
            Arreter();
            throw;
        }
    }

    private void SurCibleFermee(GraphicsCaptureItem _, object? __)
    {
        // Un événement déjà mis en file par une ancienne session ne doit pas
        // interrompre le pipeline qui vient de la remplacer.
        if (Volatile.Read(ref _captureActive) == 1)
            CaptureInterrompue?.Invoke();
    }

    /// Retire le contour coloré que Windows dessine autour de l'écran capturé.
    /// API absente avant le build 20348 (Windows 10) → garde à l'exécution.
    private static void DesactiverBordure(GraphicsCaptureSession session)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)) return;
        try
        {
            GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless).AsTask().Wait(2000);
            session.IsBorderRequired = false;
        }
        catch { } // au pire, le contour reste
    }

    private void SurFrame(Direct3D11CaptureFramePool pool, object? _)
    {
        using var frame = pool.TryGetNextFrame();
        if (frame is null) return;
        if (_origine == TimeSpan.MinValue) _origine = frame.SystemRelativeTime;
        HorlogeLive = frame.SystemRelativeTime - _origine;
        // ATTENTION — dette connue, mesurée le 18/08/2026, PAS corrigée ici.
        // Le `using` ci-dessus rend le tampon au pool dès la sortie de cette méthode,
        // alors que la surface part dans la FrameQueue pour un usage DIFFÉRÉ par
        // l'encodeur. Le pool n'ayant que 2 tampons, les entrées de la file aliasent
        // 2 textures : l'encodeur lit du contenu plus récent que l'horodatage qu'il
        // écrit. Contraire au contrat WGC (une surface n'est valide que tant que sa
        // frame est ouverte), et personne ne libère jamais de surface dans ce dépôt.
        // Corriger demande soit ~140 Mo de VRAM (0,28 s de blocage mesuré à chaque
        // frontière de segment × 60 fps × 8,3 Mo par texture 1080p BGRA), soit de
        // supprimer ce blocage en préparant le transcodeur du segment suivant à
        // l'avance. À faire avec une validation en jeu réel, pas à l'aveugle.
        Frames.AjouterOuJeter(new(frame.Surface, HorlogeLive));
    }

    public void Arreter()
    {
        Volatile.Write(ref _captureActive, 0);
        if (_item is not null) _item.Closed -= SurCibleFermee;
        _session?.Dispose(); _session = null;
        if (_pool is not null) { _pool.FrameArrived -= SurFrame; _pool.Dispose(); _pool = null; }
        _item = null;
        _device = null;
        Frames.Terminer();
    }

    public void Dispose() => Arreter();
}
