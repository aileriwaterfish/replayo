using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Replayo.Audio;

/// Capture WASAPI « process loopback » (Windows 10 20H2+) : uniquement le son émis
/// par un processus et ses enfants (mode LoL : le jeu seul, sans Spotify/Discord).
/// Format de sortie : float 32 bits, 48 kHz, stéréo. Interop maison :
/// ActivateAudioInterfaceAsync n'est pas exposé publiquement par NAudio.
public sealed class ProcessLoopbackCapture(int pidCible) : IDisposable
{
    private const string VadProcessLoopback = @"VAD\Process_Loopback";
    private static Guid _iidIAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

    public static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    private AudioClient? _client;
    private EventWaitHandle? _evenement;
    private Thread? _boucle;
    private volatile bool _actif;

    /// Reçoit chaque paquet d'échantillons bruts (float 32 interleaved stéréo).
    public event Action<byte[], int>? EchantillonsRecus;

    // --- Interop ActivateAudioInterfaceAsync ---

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivationHandler
    {
        void ActivateCompleted(IActivationOperation operation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivationOperation
    {
        void GetActivateResult(out int hrActivation, [MarshalAs(UnmanagedType.IUnknown)] out object? interfaceActivee);
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string cheminPeripherique, ref Guid riid,
        IntPtr paramsActivation, IActivationHandler handler, out IActivationOperation operation);

    private sealed class Handler : IActivationHandler
    {
        public readonly ManualResetEventSlim Pret = new(false);
        public object? Interface;
        public int Hr;

        public void ActivateCompleted(IActivationOperation operation)
        {
            operation.GetActivateResult(out Hr, out Interface);
            Pret.Set();
        }
    }

    // AUDIOCLIENT_ACTIVATION_PARAMS { type ; { pid ; mode } } passé en PROPVARIANT VT_BLOB.
    [StructLayout(LayoutKind.Sequential)]
    private struct ParamsActivation
    {
        public int ActivationType;      // 1 = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
        public int TargetProcessId;
        public int ProcessLoopbackMode; // 0 = INCLUDE_TARGET_PROCESS_TREE
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariantBlob
    {
        public ushort vt; // 65 = VT_BLOB
        public ushort r1, r2, r3;
        public int TailleBlob;
        public IntPtr DonneesBlob;
    }

    /// Démarre la capture. Toute la vie COM (activation, Initialize, boucle) se
    /// passe dans un thread MTA dédié : IAudioClient n'a pas de proxy COM et ne
    /// doit jamais traverser d'appartement (E_NOINTERFACE depuis le STA de WPF).
    public void Demarrer()
    {
        using var pret = new ManualResetEventSlim(false);
        Exception? erreur = null;

        _actif = true;
        _boucle = new Thread(() => VieCapture(() => pret.Set(), e => { erreur = e; pret.Set(); }))
        { IsBackground = true, Name = "replayo-loopback-lol" };
        _boucle.SetApartmentState(ApartmentState.MTA);
        _boucle.Start();

        if (!pret.Wait(TimeSpan.FromSeconds(8)))
            throw new InvalidOperationException("Process loopback : délai d'activation dépassé.");
        if (erreur is not null) { _actif = false; throw erreur; }
    }

    private void VieCapture(Action succes, Action<Exception> echec)
    {
        try
        {
            var parametres = new ParamsActivation
            { ActivationType = 1, TargetProcessId = pidCible, ProcessLoopbackMode = 0 };

            var tailleParams = Marshal.SizeOf<ParamsActivation>();
            var pParams = Marshal.AllocHGlobal(tailleParams);
            var pPropVariant = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariantBlob>());
            try
            {
                Marshal.StructureToPtr(parametres, pParams, false);
                Marshal.StructureToPtr(new PropVariantBlob
                { vt = 65, TailleBlob = tailleParams, DonneesBlob = pParams }, pPropVariant, false);

                var handler = new Handler();
                ActivateAudioInterfaceAsync(VadProcessLoopback, ref _iidIAudioClient, pPropVariant, handler, out _);
                if (!handler.Pret.Wait(TimeSpan.FromSeconds(5)) || handler.Interface is null || handler.Hr < 0)
                    throw new InvalidOperationException($"Activation du process loopback impossible (hr=0x{handler.Hr:X8}).");

                _client = new AudioClient((IAudioClient)handler.Interface);
            }
            finally
            {
                Marshal.FreeHGlobal(pPropVariant);
                Marshal.FreeHGlobal(pParams);
            }

            // Loopback + event callback ; tampon d'1 s (unités de 100 ns).
            _client.Initialize(AudioClientShareMode.Shared,
                AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback,
                10_000_000, 0, Format, Guid.Empty);

            _evenement = new EventWaitHandle(false, EventResetMode.AutoReset);
            _client.SetEventHandle(_evenement.SafeWaitHandle.DangerousGetHandle());
            _client.Start();
            succes();
        }
        catch (Exception e) { echec(e); return; }

        var capture = _client.AudioCaptureClient;
        var octetsParFrame = Format.BlockAlign;
        while (_actif)
        {
            if (!_evenement!.WaitOne(200)) continue;
            // Boucle canonique WASAPI : ne lire que les paquets annoncés, sinon le
            // périphérique virtuel sert du silence sans limite de débit.
            while (_actif && capture.GetNextPacketSize() > 0)
            {
                var pDonnees = capture.GetBuffer(out var frames, out _, out _, out _);
                if (frames == 0) { capture.ReleaseBuffer(0); break; }
                var octets = new byte[frames * octetsParFrame];
                if (pDonnees != IntPtr.Zero) Marshal.Copy(pDonnees, octets, 0, octets.Length);
                // pointeur nul = paquet silencieux (flag Silent) : on émet des zéros
                capture.ReleaseBuffer(frames);
                EchantillonsRecus?.Invoke(octets, octets.Length);
            }
        }
        try { _client.Stop(); } catch { /* déjà arrêté */ }
    }

    public void Dispose()
    {
        _actif = false;
        _boucle?.Join(500);
        try { _client?.Stop(); } catch { /* déjà arrêté */ }
        _client?.Dispose();
        _evenement?.Dispose();
    }
}
