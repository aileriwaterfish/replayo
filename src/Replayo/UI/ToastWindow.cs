using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Replayo.UI;

/// Toast « Clip sauvegardé » affiché en haut à droite pendant 4 s
/// (maquette « Replayo UI », écran 1b). Jamais activée : WS_EX_NOACTIVATE
/// pour ne pas voler le focus au jeu, WS_EX_TOOLWINDOW pour rester hors Alt+Tab.
public sealed class ToastWindow : Window
{
    private const int DureeAffichageMs = 2000;

    [DllImport("user32.dll")] private static extern int GetWindowLongW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLongW(IntPtr hwnd, int index, int valeur);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    public static void Afficher(string nomFichier, int dureeSecondes)
        => new ToastWindow(nomFichier, dureeSecondes).Show();

    private ToastWindow(string nomFichier, int dureeSecondes)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            _ = SetWindowLongW(h, GWL_EXSTYLE, GetWindowLongW(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };

        var pastille = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(46, 0x6E, 0x6C, 0xF3)), // accent à ~18 %
            Child = new TextBlock
            {
                Text = "✓", FontSize = 15, Foreground = Theme.AccentSurvol,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var textes = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        textes.Children.Add(new TextBlock { Text = "Clip sauvegardé", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        textes.Children.Add(new TextBlock
        {
            Text = $"{nomFichier} · {dureeSecondes} s", FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
            Margin = new Thickness(0, 2, 0, 0),
        });

        var ligne = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 12, 16, 12) };
        ligne.Children.Add(pastille);
        ligne.Children.Add(textes);

        var progression = new Rectangle
        {
            Height = 2, Fill = Theme.Accent,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Bottom,
            RenderTransformOrigin = new Point(0, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };

        var grille = new Grid();
        grille.Children.Add(ligne);
        grille.Children.Add(progression);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 0x16, 0x16, 0x1A)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = grille,
        };

        Loaded += (_, _) =>
        {
            var zone = SystemParameters.WorkArea;
            Left = zone.Right - ActualWidth - 16;
            Top = zone.Top + 16;
            progression.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(DureeAffichageMs)));
            var minuteur = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DureeAffichageMs) };
            minuteur.Tick += (_, _) => { minuteur.Stop(); Close(); };
            minuteur.Start();
        };
    }
}
