using Replayo.UI;
using Xunit;

public class DureeTests
{
    [Theory]
    [InlineData("9", 300, 9)]        // saisie libre sous les paliers
    [InlineData(" 9 ", 300, 9)]      // espaces tolérés
    [InlineData("45", 300, 45)]      // valeur entre deux paliers, gardée telle quelle
    [InlineData("3", 300, 5)]        // sous la borne min → clamp 5
    [InlineData("99999", 300, 1200)] // au-dessus de la borne max → clamp 1200
    [InlineData("abc", 300, 300)]    // invalide → valeur actuelle
    [InlineData("", 300, 300)]       // vide → valeur actuelle
    [InlineData(null, 300, 300)]     // null → valeur actuelle
    [InlineData("-8", 120, 5)]       // négatif : parsé puis clampé à 5
    public void NormaliserDuree_ParseClampeOuRetombe(string? texte, int actuelle, int attendu)
        => Assert.Equal(attendu, Controls.NormaliserDuree(texte, actuelle));

    [Theory]
    [InlineData(9, 0)]     // 9 s → palier 15 s (index 0)
    [InlineData(15, 0)]    // palier exact
    [InlineData(45, 1)]    // équidistant 30/60 → premier palier (30, index 1)
    [InlineData(500, 6)]   // 500 s → 600 s (index 6)
    [InlineData(1200, 8)]  // borne max
    public void IndexPalierLePlusProche_RendLIndexDuPalierLePlusProche(int valeur, int attendu)
        => Assert.Equal(attendu, Controls.IndexPalierLePlusProche(valeur));
}
