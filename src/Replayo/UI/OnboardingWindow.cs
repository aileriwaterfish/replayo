using System.Windows;
using System.Windows.Controls;
using Replayo.Core;

namespace Replayo.UI;

/// Assistant de premier lancement : format → qualité → écrans → durée.
/// (L'étape « clé de licence » sera ajoutée en tête par le Plan C.)
public sealed class OnboardingWindow : Window
{
    private readonly ConfigStore _store;

    public OnboardingWindow(ConfigStore store)
    {
        _store = store;
        var cfg = store.Charger(); // défauts

        Title = "Bienvenue dans Replayo";
        Width = 480; Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var pile = new StackPanel { Margin = new Thickness(24) };
        pile.Children.Add(new TextBlock
        {
            Text = "Configuration en 4 réglages",
            FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4),
        });
        pile.Children.Add(new TextBlock
        {
            Text = "Modifiable à tout moment depuis l'icône Replayo (zone de notification).",
            Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        });

        pile.Children.Add(Controls.Titre("Format de sortie"));
        var format = Controls.ComboFormat(cfg.FormatSortie);
        pile.Children.Add(format);

        pile.Children.Add(Controls.Titre("Qualité"));
        var preset = Controls.ComboPreset(cfg.Preset);
        pile.Children.Add(preset);

        pile.Children.Add(Controls.Titre("Écran(s) à capturer"));
        var (panneauEcrans, lireEcrans) = Controls.PanneauEcrans(cfg.SourcesEcrans);
        pile.Children.Add(panneauEcrans);

        pile.Children.Add(Controls.Titre("Durée du replay"));
        var dureeLbl = new Label { Content = Controls.TexteDuree(cfg.DureeBufferSecondes) };
        var duree = Controls.SliderDuree(cfg.DureeBufferSecondes);
        duree.ValueChanged += (_, _) => dureeLbl.Content = Controls.TexteDuree(Controls.DureeSelectionnee(duree));
        pile.Children.Add(duree);
        pile.Children.Add(dureeLbl);

        var terminer = new Button
        {
            Content = "Terminer et lancer la capture",
            Margin = new Thickness(0, 24, 0, 0), Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true, // Entrée valide l'assistant
        };
        terminer.Click += (_, _) =>
        {
            cfg.FormatSortie = Controls.FormatSelectionne(format);
            cfg.Preset = Controls.PresetSelectionne(preset);
            cfg.SourcesEcrans = lireEcrans();
            cfg.DureeBufferSecondes = Controls.DureeSelectionnee(duree);
            _store.Enregistrer(cfg);
            DialogResult = true;
            Close();
        };
        pile.Children.Add(terminer);

        Content = new ScrollViewer { Content = pile, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
