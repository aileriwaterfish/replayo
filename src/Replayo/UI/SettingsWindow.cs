using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Replayo.Core;

namespace Replayo.UI;

/// Fenêtre de réglages : tous les paramètres, appliqués à chaud à l'enregistrement.
/// Contrôles construits en code (pas de XAML) pour rester en un seul fichier.
/// Mise en page « cartes » (maquette Claude Design « Replayo UI », écran 1a).
public sealed class SettingsWindow : Window
{
    public static event Action<ReplayoConfig>? ConfigChangee;

    private readonly ConfigStore _store;
    private readonly RecorderService _recorder;

    private readonly Func<int> _lireDuree;
    private readonly ComboBox _preset;
    private readonly ComboBox _format;
    private readonly Func<List<int>> _lireEcrans;
    private readonly CheckBox _audioSys;
    private readonly CheckBox _audioMic;
    private readonly TextBox _raccourci;
    private readonly RadioButton _nomAuto;
    private readonly CheckBox _autostart;
    private readonly TextBox _dossier;

    private uint _mods, _vk;

    public SettingsWindow(ConfigStore store, RecorderService recorder)
    {
        _store = store;
        _recorder = recorder;
        var cfg = store.Charger();
        _mods = cfg.RaccourciModificateurs;
        _vk = cfg.RaccourciTouche;

        Title = "Réglages — Replayo";
        Width = 640; Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanMinimize;
        Theme.Sombre(this);

        var pile = new StackPanel { Margin = new Thickness(24, 8, 24, 28) };

        pile.Children.Add(Controls.Titre("Capture"));

        var (panneauDuree, lireDuree) = Controls.PanneauDuree(cfg.DureeBufferSecondes);
        _lireDuree = lireDuree;
        pile.Children.Add(Controls.Carte("Durée du replay", "Les dernières secondes gardées en mémoire", panneauDuree));

        _preset = Controls.ComboPreset(cfg.Preset);
        _preset.MinWidth = 280;
        pile.Children.Add(Controls.Carte("Qualité", "Résolution et fluidité de l'enregistrement", _preset));

        _format = Controls.ComboFormat(cfg.FormatSortie);
        _format.MinWidth = 280;
        pile.Children.Add(Controls.Carte("Format de sortie", "MP4 recommandé pour le partage", _format));

        var (tousEcrans, listeEcrans, lireEcrans) = Controls.EcransAvecInterrupteur(cfg.SourcesEcrans);
        _lireEcrans = lireEcrans;
        pile.Children.Add(Controls.CarteVerticale("Tous les écrans", "Capturer chaque moniteur connecté", listeEcrans, tousEcrans));

        pile.Children.Add(Controls.Titre("Audio"));
        (_audioSys, var carteSys) = CarteInterrupteur("Son du système", null, cfg.AudioSysteme);
        pile.Children.Add(carteSys);
        (_audioMic, var carteMic) = CarteInterrupteur("Micro", null, cfg.AudioMicro);
        pile.Children.Add(carteMic);

        pile.Children.Add(Controls.Titre("Clips"));

        _raccourci = new TextBox
        {
            Text = Controls.TexteRaccourci(_mods, _vk), IsReadOnly = true,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center, MinWidth = 96,
            Padding = new Thickness(12, 5, 12, 5), Cursor = Cursors.Hand,
        };
        _raccourci.PreviewKeyDown += CapturerRaccourci;
        pile.Children.Add(Controls.Carte("Raccourci du clip", "Cliquer puis taper la combinaison", _raccourci));

        var exempleAuto = new StackPanel { Orientation = Orientation.Horizontal };
        exempleAuto.Children.Add(new TextBlock { Text = "Automatique (horodaté)", Foreground = Theme.Texte, FontSize = 13 });
        exempleAuto.Children.Add(new TextBlock
        {
            Text = "Replay_2026-07-19_21-04.mp4", Foreground = Theme.TexteSecondaire,
            FontSize = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        _nomAuto = new RadioButton { Content = exempleAuto, IsChecked = !cfg.NommageManuel, Margin = new Thickness(0, 2, 0, 2) };
        var nomManuel = new RadioButton { Content = "Me demander à chaque clip", IsChecked = cfg.NommageManuel, Margin = new Thickness(0, 2, 0, 2) };
        var pileNoms = new StackPanel();
        pileNoms.Children.Add(_nomAuto);
        pileNoms.Children.Add(nomManuel);
        pile.Children.Add(Controls.CarteVerticale("Nom des clips", null, pileNoms));

        _dossier = new TextBox { Text = cfg.DossierSortie, MinWidth = 220, VerticalContentAlignment = VerticalAlignment.Center };
        var parcourir = new Button { Content = "Parcourir…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 5, 12, 5) };
        parcourir.Click += (_, _) =>
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _dossier.Text = dlg.SelectedPath;
        };
        var droiteDossier = new StackPanel { Orientation = Orientation.Horizontal };
        droiteDossier.Children.Add(_dossier);
        droiteDossier.Children.Add(parcourir);
        pile.Children.Add(Controls.Carte("Dossier des clips", "Vide = Vidéos\\Replayo", droiteDossier));

        (_autostart, var carteAuto) = CarteInterrupteur("Démarrer avec Windows", "Lance Replayo à l'ouverture de session", AutostartManager.EstActive());
        pile.Children.Add(carteAuto);

        var enregistrer = new Button
        {
            Content = "Enregistrer", Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(18, 7, 18, 7), HorizontalAlignment = HorizontalAlignment.Right,
        };
        enregistrer.Click += (_, _) => Enregistrer();
        pile.Children.Add(enregistrer);

        Content = new ScrollViewer { Content = pile, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Closing += (_, e) => { e.Cancel = true; Hide(); }; // fermer = masquer, la capture continue
    }

    /// Carte à interrupteur, avec libellé d'état « Activé / Désactivé » qui suit le toggle.
    private static (CheckBox Toggle, Border Carte) CarteInterrupteur(string titre, string? sousTitre, bool coche)
    {
        var toggle = Controls.Interrupteur(coche);
        var etat = new TextBlock
        {
            Text = coche ? "Activé" : "Désactivé", FontSize = 12,
            Foreground = Theme.TexteSecondaire, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        toggle.Checked += (_, _) => etat.Text = "Activé";
        toggle.Unchecked += (_, _) => etat.Text = "Désactivé";
        var droite = new StackPanel { Orientation = Orientation.Horizontal };
        droite.Children.Add(etat);
        droite.Children.Add(toggle);
        return (toggle, Controls.Carte(titre, sousTitre, droite));
    }

    private void CapturerRaccourci(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var touche = e.Key == Key.System ? e.SystemKey : e.Key;
        if (touche is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift) return;
        uint mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= 0x1;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= 0x2;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= 0x4;
        _mods = mods;
        _vk = (uint)KeyInterop.VirtualKeyFromKey(touche);
        _raccourci.Text = Controls.TexteRaccourci(_mods, _vk);
    }

    private void Enregistrer()
    {
        var cfg = _store.Charger();
        cfg.DureeBufferSecondes = _lireDuree();
        cfg.Preset = Controls.PresetSelectionne(_preset);
        cfg.FormatSortie = Controls.FormatSelectionne(_format);
        cfg.SourcesEcrans = _lireEcrans();
        cfg.AudioSysteme = _audioSys.IsChecked == true;
        cfg.AudioMicro = _audioMic.IsChecked == true;
        cfg.RaccourciModificateurs = _mods;
        cfg.RaccourciTouche = _vk;
        cfg.NommageManuel = _nomAuto.IsChecked != true;
        cfg.DossierSortie = _dossier.Text.Trim();
        _store.Enregistrer(cfg);

        if (_autostart.IsChecked == true) AutostartManager.Activer(); else AutostartManager.Desactiver();

        _recorder.Redemarrer(cfg);
        ConfigChangee?.Invoke(cfg);
        Hide();
    }
}
