using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Replayo.Core;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;

namespace Replayo.UI;

/// Menu custom de la zone de notification (maquette « Replayo UI », écran 1c).
/// Reconstruit à chaque ouverture : état et raccourci toujours à jour.
public sealed class TrayMenuWindow : Window
{
    private bool _fermetureDemandee; // Close() pendant la fermeture jette InvalidOperationException

    public TrayMenuWindow(RecorderService recorder, Action sauvegarderClip, Action ouvrirDossier, Action ouvrirReglages, Action quitter)
    {
        var cfg = new ConfigStore().Charger();

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Deactivated += (_, _) => Fermer();

        var pile = new StackPanel { Width = 268 };

        // En-tête : tuile dégradée + nom + état.
        var glyphe = Controls.GlypheBoucle(15, Brushes.White);
        glyphe.HorizontalAlignment = HorizontalAlignment.Center;
        glyphe.VerticalAlignment = VerticalAlignment.Center;
        var tuile = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(6),
            Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#7E8FFA"),
                (Color)ColorConverter.ConvertFromString("#8A4FE6"),
                new Point(0, 0), new Point(1, 1)),
            Child = glyphe,
        };
        var titres = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        titres.Children.Add(new TextBlock { Text = "Replayo", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        titres.Children.Add(new TextBlock
        {
            Text = recorder.EnCapture ? $"● Replay actif — {cfg.DureeBufferSecondes} s en mémoire" : "Replay arrêté",
            FontSize = 11,
            Foreground = recorder.EnCapture ? Theme.AccentSurvol : Theme.TexteSecondaire,
            Margin = new Thickness(0, 1, 0, 0),
        });
        var entete = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 12, 10) };
        entete.Children.Add(tuile);
        entete.Children.Add(titres);
        pile.Children.Add(entete);
        pile.Children.Add(Separateur());

        // Replay activé + interrupteur (basculer la capture).
        var toggle = Controls.Interrupteur(recorder.EnCapture);
        toggle.IsHitTestVisible = false; // c'est la rangée entière qui est cliquable
        pile.Children.Add(Item("Replay activé", toggle, () =>
        {
            if (recorder.EnCapture) recorder.Arreter();
            else recorder.Demarrer(new ConfigStore().Charger());
        }));

        var raccourci = new TextBlock
        {
            Text = Controls.TexteRaccourci(cfg.RaccourciModificateurs, cfg.RaccourciTouche),
            FontSize = 11, FontFamily = new FontFamily("Consolas"),
            Foreground = Theme.TexteSecondaire, VerticalAlignment = VerticalAlignment.Center,
        };
        pile.Children.Add(Item("Sauvegarder le clip", raccourci, sauvegarderClip));
        pile.Children.Add(Item("Ouvrir le dossier des clips", null, ouvrirDossier));
        pile.Children.Add(Item("Réglages…", null, ouvrirReglages));
        pile.Children.Add(Separateur());
        pile.Children.Add(Item("Quitter", null, quitter));

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(250, 0x20, 0x20, 0x20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            Child = pile,
        };

        // Ancré au-dessus du curseur, contenu dans la zone de travail.
        Loaded += (_, _) =>
        {
            var souris = System.Windows.Forms.Control.MousePosition;
            var zone = SystemParameters.WorkArea;
            Left = Math.Clamp(souris.X - ActualWidth, zone.Left, zone.Right - ActualWidth);
            Top = Math.Clamp(souris.Y - ActualHeight - 8, zone.Top, zone.Bottom - ActualHeight);
            Activate(); // nécessaire pour recevoir Deactivated au clic ailleurs
        };
    }

    private void Fermer()
    {
        if (_fermetureDemandee) return;
        _fermetureDemandee = true;
        Close();
    }

    private static Border Separateur() => new()
    {
        Height = 1,
        Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
        Margin = new Thickness(8, 4, 8, 4),
    };

    private Border Item(string texte, FrameworkElement? droite, Action action)
    {
        var ligne = new DockPanel();
        if (droite is not null)
        {
            droite.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(droite, Dock.Right);
            ligne.Children.Add(droite);
        }
        ligne.Children.Add(new TextBlock { Text = texte, FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });

        var item = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 9, 12, 9), Background = Brushes.Transparent, Child = ligne, Cursor = Cursors.Hand };
        item.MouseEnter += (_, _) => item.Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
        item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
        item.MouseLeftButtonUp += (_, _) => { Fermer(); action(); };
        return item;
    }
}
