using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Replayo.Capture;

/// Crée le device Direct3D11 partagé par la capture et l'encodeur (zéro-copie GPU).
public static class D3DHelper
{
    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr module, uint flags,
        IntPtr featureLevels, uint numLevels, uint sdkVersion, out IntPtr device, out IntPtr featureLevel, out IntPtr context);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static IDirect3DDevice CreerDeviceWinRT()
    {
        // D3D_DRIVER_TYPE_HARDWARE = 1 ; D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20 ; SDK_VERSION = 7
        int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7,
                                   out var d3dDevice, out _, out var context);
        Marshal.ThrowExceptionForHR(hr);
        Marshal.Release(context);
        hr = CreateDirect3D11DeviceFromDXGIDevice(d3dDevice, out var winrtDevice);
        Marshal.Release(d3dDevice);
        Marshal.ThrowExceptionForHR(hr);
        var device = MarshalInterface<IDirect3DDevice>.FromAbi(winrtDevice);
        Marshal.Release(winrtDevice);
        return device;
    }
}
