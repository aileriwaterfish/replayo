using System.Threading.Channels;
using Windows.Graphics.DirectX.Direct3D11;

namespace Replayo.Capture;

public sealed record FrameCapturee(IDirect3DSurface Surface, TimeSpan Horodatage);

/// File bornée entre la capture et l'encodeur. Si l'encodeur ne suit pas,
/// on jette la frame LA PLUS ANCIENNE (le direct prime sur l'historique).
public sealed class FrameQueue
{
    private readonly Channel<FrameCapturee> _canal = Channel.CreateBounded<FrameCapturee>(
        new BoundedChannelOptions(90) { FullMode = BoundedChannelFullMode.DropOldest });

    public bool AjouterOuJeter(FrameCapturee f) => _canal.Writer.TryWrite(f);
    public ValueTask<FrameCapturee> PrendreAsync(CancellationToken ct) => _canal.Reader.ReadAsync(ct);
    public void Terminer() => _canal.Writer.TryComplete();
}
