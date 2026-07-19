using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Replayo.Capture;
using Replayo.Core;

namespace Replayo.UI;

/// Fabrique de contrôles partagés entre l'onboarding et les réglages
/// (mêmes widgets, mêmes conversions), pour ne pas dupliquer la logique.
internal static class Controls
{
    // Durée du replay : paliers lisibles 15 s → 20 min.
    public static readonly int[] Durees =
        { 15, 30, 60, 120, 180, 300, 600, 900, 1200 };

    public static string TexteDuree(int s) => s < 60 ? $"{s} s" : $"{s / 60} min";

    public static readonly (string Cle, string Libelle)[] Presets =
    {
        ("eco", "Éco — 1080p 30 fps (léger)"),
        ("equilibre", "Équilibré — natif 60 fps (recommandé)"),
        ("qualite", "Qualité — natif 60 fps (débit élevé)"),
    };

    public static Label Titre(string texte) => new()
    {
        Content = texte,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 12, 0, 2),
    };

    public static ComboBox ComboPreset(string selection)
    {
        var c = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var (cle, lib) in Presets) c.Items.Add(new ComboBoxItem { Content = lib, Tag = cle });
        c.SelectedIndex = Math.Max(0, Array.FindIndex(Presets, p => p.Cle == selection));
        return c;
    }

    public static string PresetSelectionne(ComboBox c) => (string)((ComboBoxItem)c.SelectedItem).Tag;

    public static ComboBox ComboFormat(string selection)
    {
        var c = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
        c.Items.Add(new ComboBoxItem { Content = "MP4 (compatible partout)", Tag = "mp4" });
        c.Items.Add(new ComboBoxItem { Content = "MKV (robuste)", Tag = "mkv" });
        c.SelectedIndex = selection == "mkv" ? 1 : 0;
        return c;
    }

    public static string FormatSelectionne(ComboBox c) => (string)((ComboBoxItem)c.SelectedItem).Tag;

    public static Slider SliderDuree(int valeur) => new()
    {
        Minimum = 0, Maximum = Durees.Length - 1,
        Value = IndexPalierLePlusProche(valeur), // valeur libre (ex. 9 s) → palier le plus proche
        IsSnapToTickEnabled = true, TickFrequency = 1,
        Margin = new Thickness(0, 0, 0, 0),
    };

    public static int DureeSelectionnee(Slider s) => Durees[(int)Math.Round(s.Value)];

    // Saisie manuelle : parse + clamp 5–1200 ; invalide → on garde la valeur actuelle.
    public static int NormaliserDuree(string? texte, int valeurActuelle)
        => int.TryParse(texte?.Trim(), out int s) ? Math.Clamp(s, 5, 1200) : valeurActuelle;

    public static int IndexPalierLePlusProche(int valeur)
    {
        int meilleur = 0;
        for (int i = 1; i < Durees.Length; i++)
            if (Math.Abs(Durees[i] - valeur) < Math.Abs(Durees[meilleur] - valeur)) meilleur = i;
        return meilleur;
    }

    /// Slider + champ texte synchronisés, en rangée compacte (pour une carte) :
    /// le slider donne les paliers rapides, le champ accepte une valeur libre
    /// (5–1200 s) qui fait foi à l'enregistrement.
    public static (StackPanel Panneau, Func<int> Lire) PanneauDuree(int valeur)
    {
        int courante = valeur;
        bool majInterne = false; // vrai quand on déplace le slider par code (ne pas écraser la saisie)

        var slider = SliderDuree(valeur);
        slider.Width = 180;
        slider.VerticalAlignment = VerticalAlignment.Center;
        var champ = new TextBox
        {
            Text = valeur.ToString(), Width = 52,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 6, 0),
        };
        var unite = new TextBlock
        {
            Text = "s", FontSize = 12, Foreground = Theme.TexteSecondaire,
            VerticalAlignment = VerticalAlignment.Center,
        };

        slider.ValueChanged += (_, _) =>
        {
            if (majInterne) return;
            courante = DureeSelectionnee(slider);
            champ.Text = courante.ToString();
        };

        void Valider()
        {
            courante = NormaliserDuree(champ.Text, courante);
            champ.Text = courante.ToString();
            majInterne = true;
            slider.Value = IndexPalierLePlusProche(courante);
            majInterne = false;
        }
        champ.LostFocus += (_, _) => Valider();
        champ.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Valider(); e.Handled = true; } };

        var panneau = new StackPanel { Orientation = Orientation.Horizontal };
        panneau.Children.Add(slider);
        panneau.Children.Add(champ);
        panneau.Children.Add(unite);
        return (panneau, () => courante);
    }

    /// CheckBox rendue en interrupteur 40×20 (style nommé « Interrupteur » du thème).
    public static CheckBox Interrupteur(bool coche) => new()
    {
        IsChecked = coche,
        Style = (Style)Application.Current.Resources["Interrupteur"],
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static StackPanel LibelleCarte(string titre, string? sousTitre)
    {
        var pile = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        pile.Children.Add(new TextBlock { Text = titre, FontSize = 14, Foreground = Theme.Texte });
        if (sousTitre is not null)
            pile.Children.Add(new TextBlock
            {
                Text = sousTitre, FontSize = 12, Foreground = Theme.TexteSecondaire,
                Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap,
            });
        return pile;
    }

    private static Border FondCarte(UIElement contenu) => new()
    {
        Background = Theme.Surface,
        BorderBrush = Theme.Bordure,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(18, 14, 18, 14),
        Margin = new Thickness(0, 0, 0, 8),
        Child = contenu,
    };

    /// Carte « réglage » : titre + sous-titre à gauche, contrôle à droite.
    public static Border Carte(string titre, string? sousTitre, FrameworkElement droite)
    {
        var ligne = new DockPanel();
        droite.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(droite, Dock.Right);
        ligne.Children.Add(droite);
        ligne.Children.Add(LibelleCarte(titre, sousTitre));
        return FondCarte(ligne);
    }

    /// Carte à contenu empilé (radios, liste d'écrans), avec contrôle d'en-tête optionnel.
    public static Border CarteVerticale(string titre, string? sousTitre, FrameworkElement bas, FrameworkElement? droite = null)
    {
        var entete = new DockPanel();
        if (droite is not null)
        {
            droite.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(droite, Dock.Right);
            entete.Children.Add(droite);
        }
        entete.Children.Add(LibelleCarte(titre, sousTitre));
        bas.Margin = new Thickness(0, 12, 0, 0);
        var pile = new StackPanel();
        pile.Children.Add(entete);
        pile.Children.Add(bas);
        return FondCarte(pile);
    }

    /// Liste de cases à cocher : « Tous les écrans » + une par écran détecté.
    /// Liste des écrans détectés, pilotée par un interrupteur « tous » externe.
    private static (StackPanel Liste, Func<List<int>> Lire) ListeEcrans(List<int> selection, CheckBox tous)
    {
        var liste = new StackPanel { Opacity = selection.Count == 0 ? 0.45 : 1.0 };
        var cases = new List<CheckBox>();
        foreach (var e in MonitorInfo.EnumererEcrans())
        {
            var cb = new CheckBox
            {
                Content = $"Écran {e.Index + 1} — {e.Largeur}×{e.Hauteur}{(e.Principal ? " (principal)" : "")}",
                IsChecked = selection.Contains(e.Index),
                Margin = new Thickness(2, 2, 0, 2),
                IsEnabled = selection.Count != 0,
                Tag = e.Index,
            };
            cases.Add(cb);
            liste.Children.Add(cb);
        }
        tous.Checked += (_, _) => { foreach (var cb in cases) { cb.IsChecked = false; cb.IsEnabled = false; } liste.Opacity = 0.45; };
        tous.Unchecked += (_, _) => { foreach (var cb in cases) cb.IsEnabled = true; liste.Opacity = 1.0; };

        List<int> Lire() => tous.IsChecked == true
            ? new List<int>()
            : cases.Where(cb => cb.IsChecked == true).Select(cb => (int)cb.Tag!).ToList();

        return (liste, Lire);
    }

    /// Variante « carte » : l'interrupteur « tous » est rendu séparément (en-tête de carte).
    public static (CheckBox Tous, StackPanel Liste, Func<List<int>> Lire) EcransAvecInterrupteur(List<int> selection)
    {
        var tous = Interrupteur(selection.Count == 0);
        var (liste, lire) = ListeEcrans(selection, tous);
        return (tous, liste, lire);
    }

    /// Panneau autonome (onboarding) : interrupteur + libellé, puis la liste.
    public static (StackPanel Panneau, Func<List<int>> Lire) PanneauEcrans(List<int> selection)
    {
        var (tous, liste, lire) = EcransAvecInterrupteur(selection);
        var entete = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
        entete.Children.Add(tous);
        entete.Children.Add(new TextBlock
        {
            Text = "Tous les écrans", FontSize = 13, Foreground = Theme.Texte,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
        });
        var panneau = new StackPanel();
        panneau.Children.Add(entete);
        panneau.Children.Add(liste);
        return (panneau, lire);
    }

    public static string TexteRaccourci(uint mods, uint vk)
    {
        var parts = new List<string>();
        if ((mods & 0x2) != 0) parts.Add("Ctrl");
        if ((mods & 0x1) != 0) parts.Add("Alt");
        if ((mods & 0x4) != 0) parts.Add("Maj");
        var touche = KeyInterop.KeyFromVirtualKey((int)vk);
        parts.Add(touche.ToString());
        return string.Join("+", parts);
    }
}
