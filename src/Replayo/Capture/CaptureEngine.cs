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
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private TimeSpan _origine = TimeSpan.MinValue;

    public FrameQueue Frames { get; } = new();
    public SizeInt32 Taille { get; private set; }
    public event Action? CaptureInterrompue;

    public void Demarrer()
    {
        _device = D3DHelper.CreerDeviceWinRT();
        var item = CaptureItemFactory.DepuisEcran(ecran.Handle);
        Taille = item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        _pool.FrameArrived += SurFrame;
        item.Closed += (_, _) => CaptureInterrompue?.Invoke(); // écran débranché
        _session = _pool.CreateCaptureSession(item);
        _session.IsCursorCaptureEnabled = true;
        DesactiverBordure(_session);
        _session.StartCapture();
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
        // La surface est référencée par la file ; l'encodeur la consomme puis la libère.
        Frames.AjouterOuJeter(new(frame.Surface, frame.SystemRelativeTime - _origine));
    }

    public void Arreter()
    {
        _session?.Dispose(); _session = null;
        if (_pool is not null) { _pool.FrameArrived -= SurFrame; _pool.Dispose(); _pool = null; }
        Frames.Terminer();
    }

    public void Dispose() => Arreter();
}
