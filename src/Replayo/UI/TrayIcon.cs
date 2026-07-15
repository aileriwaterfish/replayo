using System.Diagnostics;
using Replayo.Core;
using WF = System.Windows.Forms;

namespace Replayo.UI;

/// Icône de zone de notification : point d'entrée permanent de Replayo.
public sealed class TrayIcon : IDisposable
{
    private readonly WF.NotifyIcon _icone;
    private readonly WF.ToolStripMenuItem _basculer;
    private readonly RecorderService _recorder;

    public TrayIcon(RecorderService recorder, Action ouvrirReglages, Action quitter)
    {
        _recorder = recorder;
        var menu = new WF.ContextMenuStrip();
        _basculer = new WF.ToolStripMenuItem("Arrêter la capture", null, (_, _) => Basculer());
        menu.Items.Add(_basculer);
        menu.Items.Add("Ouvrir le dossier des clips", null, (_, _) =>
            Process.Start(new ProcessStartInfo("explorer.exe", Directory.CreateDirectory(AppPaths.DossierSortieDefaut).FullName)));
        menu.Items.Add("Réglages…", null, (_, _) => ouvrirReglages());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Quitter Replayo", null, (_, _) => quitter());

        _icone = new WF.NotifyIcon
        {
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "assets", "replayo.ico")),
            Text = "Replayo",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icone.DoubleClick += (_, _) => ouvrirReglages();
        recorder.Notification += Notifier;
    }

    private void Basculer()
    {
        if (_recorder.EnCapture) _recorder.Arreter();
        else _recorder.Demarrer(new ConfigStore().Charger());
        RafraichirEtat();
    }

    public void RafraichirEtat()
        => _basculer.Text = _recorder.EnCapture ? "Arrêter la capture" : "Démarrer la capture";

    public void Notifier(string message)
        => _icone.ShowBalloonTip(4000, "Replayo", message, WF.ToolTipIcon.Info);

    public void Dispose() { _icone.Visible = false; _icone.Dispose(); }
}
