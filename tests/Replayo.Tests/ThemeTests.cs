using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Replayo.UI;
using Xunit;

public class ThemeTests
{
    [Fact]
    public void CreerRessources_ContientLesStylesImplicitesAttendus()
    {
        var dico = Theme.CreerRessources();
        foreach (var type in new[]
        {
            typeof(Label), typeof(TextBlock), typeof(TextBox), typeof(Button),
            typeof(ComboBox), typeof(ComboBoxItem), typeof(CheckBox),
            typeof(RadioButton), typeof(ScrollBar),
        })
            Assert.True(dico.Contains(type), $"Style implicite manquant pour {type.Name}");
    }

    [Fact]
    public void CreerRessources_ContientLeStyleInterrupteur()
        => Assert.True(Theme.CreerRessources().Contains("Interrupteur"));

    [Fact]
    public void Accent_EstLeVioletDeLaMaquette()
        => Assert.Equal("#FF6E6CF3", Theme.Accent.Color.ToString());
}
