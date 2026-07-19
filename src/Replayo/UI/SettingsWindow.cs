using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Replayo.Core;

namespace Replayo.UI;

/// Fenêtre de réglages : tous les paramètres, appliqués à chaud à l'enregistrement.
/// Contrôles construits en code (pas de XAML) pour rester en un seul fichier.
public sealed class SettingsWindow : Window
{
    public static event Action<ReplayoConfig>? ConfigChangee;

    private readonly ConfigStore _store;
    private readonly RecorderService _recorder;

    private readonly Slider _duree;
    private readonly Label _dureeLbl;
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

        Title = "Réglages Replayo";
        Width = 520; Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanMinimize;
        Theme.Sombre(this);

        var pile = new StackPanel { Margin = new Thickness(20) };

        pile.Children.Add(Controls.Titre("Durée du replay"));
        _dureeLbl = new Label { Content = Controls.TexteDuree(cfg.DureeBufferSecondes) };
        _duree = Controls.SliderDuree(cfg.DureeBufferSecondes);
        _duree.ValueChanged += (_, _) => _dureeLbl.Content = Controls.TexteDuree(Controls.DureeSelectionnee(_duree));
        pile.Children.Add(_duree);
        pile.Children.Add(_dureeLbl);

        pile.Children.Add(Controls.Titre("Qualité"));
        _preset = Controls.ComboPreset(cfg.Preset);
        pile.Children.Add(_preset);

        pile.Children.Add(Controls.Titre("Format de sortie"));
        _format = Controls.ComboFormat(cfg.FormatSortie);
        pile.Children.Add(_format);

        pile.Children.Add(Controls.Titre("Écran(s) à capturer"));
        var (panneauEcrans, lire) = Controls.PanneauEcrans(cfg.SourcesEcrans);
        _lireEcrans = lire;
        pile.Children.Add(panneauEcrans);

        pile.Children.Add(Controls.Titre("Audio"));
        _audioSys = new CheckBox { Content = "Son du système", IsChecked = cfg.AudioSysteme, Margin = new Thickness(0, 2, 0, 2) };
        _audioMic = new CheckBox { Content = "Micro", IsChecked = cfg.AudioMicro, Margin = new Thickness(0, 2, 0, 2) };
        pile.Children.Add(_audioSys);
        pile.Children.Add(_audioMic);

        pile.Children.Add(Controls.Titre("Raccourci du clip (cliquez puis tapez la combinaison)"));
        _raccourci = new TextBox { Text = Controls.TexteRaccourci(_mods, _vk), IsReadOnly = true };
        _raccourci.PreviewKeyDown += CapturerRaccourci;
        pile.Children.Add(_raccourci);

        pile.Children.Add(Controls.Titre("Nom des clips"));
        _nomAuto = new RadioButton { Content = "Automatique (horodaté)", IsChecked = !cfg.NommageManuel, Margin = new Thickness(0, 2, 0, 2) };
        var nomManuel = new RadioButton { Content = "Me demander à chaque clip", IsChecked = cfg.NommageManuel, Margin = new Thickness(0, 2, 0, 2) };
        pile.Children.Add(_nomAuto);
        pile.Children.Add(nomManuel);

        pile.Children.Add(Controls.Titre("Dossier des clips (vide = Vidéos\\Replayo)"));
        _dossier = new TextBox { Text = cfg.DossierSortie };
        var parcourir = new Button { Content = "Parcourir…", Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 2, 10, 2) };
        parcourir.Click += (_, _) =>
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _dossier.Text = dlg.SelectedPath;
        };
        pile.Children.Add(_dossier);
        pile.Children.Add(parcourir);

        _autostart = new CheckBox { Content = "Démarrer Replayo avec Windows", IsChecked = AutostartManager.EstActive(), Margin = new Thickness(0, 16, 0, 2) };
        pile.Children.Add(_autostart);

        var enregistrer = new Button { Content = "Enregistrer", Margin = new Thickness(0, 20, 0, 0), Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Right };
        enregistrer.Click += (_, _) => Enregistrer();
        pile.Children.Add(enregistrer);

        Content = new ScrollViewer { Content = pile, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Closing += (_, e) => { e.Cancel = true; Hide(); }; // fermer = masquer, la capture continue
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
        cfg.DureeBufferSecondes = Controls.DureeSelectionnee(_duree);
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
