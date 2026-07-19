using System.Diagnostics;
using Replayo.Core;
using WF = System.Windows.Forms;

namespace Replayo.UI;

/// Icône de zone de notification : point d'entrée permanent de Replayo.
/// Le clic (gauche ou droit) ouvre le menu custom WPF (TrayMenuWindow).
public sealed class TrayIcon : IDisposable
{
    private readonly WF.NotifyIcon _icone;
    private readonly RecorderService _recorder;
    private readonly Action _sauvegarderClip;
    private readonly Action _ouvrirReglages;
    private readonly Action _quitter;
    private TrayMenuWindow? _menu;

    public TrayIcon(RecorderService recorder, Action sauvegarderClip, Action ouvrirReglages, Action quitter)
    {
        _recorder = recorder;
        _sauvegarderClip = sauvegarderClip;
        _ouvrirReglages = ouvrirReglages;
        _quitter = quitter;

        _icone = new WF.NotifyIcon
        {
            // Mono blanc dédié au tray (lisible sur la barre des tâches) ; repli sur l'icône d'app.
            Icon = new System.Drawing.Icon(ChoisirIconeTray()),
            Text = "Replayo",
            Visible = true,
        };
        _icone.MouseUp += (_, e) =>
        {
            if (e.Button is WF.MouseButtons.Left or WF.MouseButtons.Right) OuvrirMenu();
        };
        _icone.DoubleClick += (_, _) => ouvrirReglages();
        recorder.Notification += Notifier;
    }

    private void OuvrirMenu()
    {
        if (_menu is { IsVisible: true }) { _menu.Close(); return; }
        _menu = new TrayMenuWindow(_recorder, _sauvegarderClip, OuvrirDossier, _ouvrirReglages, _quitter);
        _menu.Show();
    }

    private static void OuvrirDossier()
        => Process.Start(new ProcessStartInfo("explorer.exe", Directory.CreateDirectory(AppPaths.DossierSortieDefaut).FullName));

    private static string ChoisirIconeTray()
    {
        var tray = Path.Combine(AppContext.BaseDirectory, "assets", "tray.ico");
        return File.Exists(tray) ? tray : Path.Combine(AppContext.BaseDirectory, "assets", "replayo.ico");
    }

    public void Notifier(string message)
        => _icone.ShowBalloonTip(4000, "Replayo", message, WF.ToolTipIcon.Info);

    public void Dispose() { _icone.Visible = false; _icone.Dispose(); }
}
