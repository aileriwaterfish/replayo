using System.Threading.Channels;
using Windows.Graphics.DirectX.Direct3D11;

namespace Replayo.Capture;

public sealed record FrameCapturee(IDirect3DSurface Surface, TimeSpan Horodatage);

/// File bornée entre la capture et l'encodeur. Si l'encodeur ne suit pas,
/// on jette la frame LA PLUS ANCIENNE (le direct prime sur l'historique).
///
/// La profondeur de 90 (~1,5 s à 60 fps) n'est pas arbitraire : elle absorbe le
/// blocage de l'encodeur à chaque frontière de segment, mesuré à ~0,28 s le
/// 18/08/2026 (cadence murale de 10,30 s pour des segments de 10,0167 s). La
/// réduire ferait perdre des frames toutes les 10 s. Voir la dette documentée
/// dans CaptureEngine.SurFrame : ces 90 entrées aliasent aujourd'hui 2 textures.
public sealed class FrameQueue
{
    private readonly Channel<FrameCapturee> _canal = Channel.CreateBounded<FrameCapturee>(
        new BoundedChannelOptions(90) { FullMode = BoundedChannelFullMode.DropOldest });

    public bool AjouterOuJeter(FrameCapturee f) => _canal.Writer.TryWrite(f);
    public ValueTask<FrameCapturee> PrendreAsync(CancellationToken ct) => _canal.Reader.ReadAsync(ct);
    public void Terminer() => _canal.Writer.TryComplete();
}
