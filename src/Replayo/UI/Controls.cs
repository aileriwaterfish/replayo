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

    public static Slider SliderDuree(int valeur)
    {
        int idx = Math.Max(0, Array.IndexOf(Durees, valeur));
        if (idx < 0) idx = 5; // 300 s
        return new Slider
        {
            Minimum = 0, Maximum = Durees.Length - 1, Value = idx,
            IsSnapToTickEnabled = true, TickFrequency = 1,
            Margin = new Thickness(0, 0, 0, 0),
        };
    }

    public static int DureeSelectionnee(Slider s) => Durees[(int)Math.Round(s.Value)];

    /// Liste de cases à cocher : « Tous les écrans » + une par écran détecté.
    public static (StackPanel Panneau, Func<List<int>> Lire) PanneauEcrans(List<int> selection)
    {
        var panneau = new StackPanel();
        var ecrans = MonitorInfo.EnumererEcrans();
        var tous = new CheckBox { Content = "Tous les écrans", IsChecked = selection.Count == 0, Margin = new Thickness(0, 2, 0, 2) };
        panneau.Children.Add(tous);
        var cases = new List<CheckBox>();
        foreach (var e in ecrans)
        {
            var cb = new CheckBox
            {
                Content = $"Écran {e.Index + 1} — {e.Largeur}×{e.Hauteur}{(e.Principal ? " (principal)" : "")}",
                IsChecked = selection.Contains(e.Index),
                Margin = new Thickness(18, 2, 0, 2),
                IsEnabled = selection.Count != 0,
                Tag = e.Index,
            };
            cases.Add(cb);
            panneau.Children.Add(cb);
        }
        tous.Checked += (_, _) => { foreach (var cb in cases) { cb.IsChecked = false; cb.IsEnabled = false; } };
        tous.Unchecked += (_, _) => { foreach (var cb in cases) cb.IsEnabled = true; };

        List<int> Lire() => tous.IsChecked == true
            ? new List<int>()
            : cases.Where(cb => cb.IsChecked == true).Select(cb => (int)cb.Tag!).ToList();

        return (panneau, Lire);
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
