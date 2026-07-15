using System.Windows;
using System.Windows.Controls;

namespace Replayo.UI;

/// Boîte de renommage optionnelle affichée juste après un clip (si activé).
/// Annuler garde le nom automatique ; OK renomme dans le même dossier.
public sealed class RenameDialog : Window
{
    /// Renomme le clip dans son dossier (caractères interdits filtrés). Renvoie le
    /// nouveau chemin, ou l'ancien si le nom est vide/inchangé/déjà pris. Logique
    /// pure et testable, séparée de l'UI.
    public static string RenommerFichier(string cheminClip, string nomSaisi)
    {
        var dossier = Path.GetDirectoryName(cheminClip)!;
        var ext = Path.GetExtension(cheminClip);
        var actuel = Path.GetFileNameWithoutExtension(cheminClip);
        var propre = string.Join("", nomSaisi.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (propre.Length == 0 || propre == actuel) return cheminClip;
        var cible = Path.Combine(dossier, propre + ext);
        try { File.Move(cheminClip, cible, overwrite: false); return cible; }
        catch { return cheminClip; } // nom déjà pris : on garde l'auto
    }

    public RenameDialog(string cheminClip)
    {
        var nomActuel = Path.GetFileNameWithoutExtension(cheminClip);

        Title = "Nommer le clip";
        Width = 420; Height = 170;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;

        var pile = new StackPanel { Margin = new Thickness(20) };
        pile.Children.Add(new TextBlock { Text = "Nom du fichier :", Margin = new Thickness(0, 0, 0, 6) });
        var boite = new TextBox { Text = nomActuel };
        boite.Loaded += (_, _) => { boite.Focus(); boite.SelectAll(); };
        pile.Children.Add(boite);

        var barre = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var annuler = new Button { Content = "Garder le nom auto", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Renommer", Padding = new Thickness(12, 4, 12, 4), IsDefault = true };
        annuler.Click += (_, _) => Close();
        ok.Click += (_, _) => { RenommerFichier(cheminClip, boite.Text); Close(); };
        barre.Children.Add(annuler);
        barre.Children.Add(ok);
        pile.Children.Add(barre);

        Content = pile;
    }
}
