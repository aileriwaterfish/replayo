using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace Replayo.UI;

/// Thème sombre permanent : palette + styles implicites (Application.Resources)
/// + barre de titre sombre par fenêtre (DWM). Aucun suivi du thème Windows.
internal static class Theme
{
    public static readonly SolidColorBrush FondFenetre = Fige("#202020");
    public static readonly SolidColorBrush Surface = Fige("#2B2B2B");
    public static readonly SolidColorBrush Bordure = Fige("#3A3A3A");
    public static readonly SolidColorBrush Survol = Fige("#3A3A3A");
    public static readonly SolidColorBrush Presse = Fige("#454545");
    public static readonly SolidColorBrush Texte = Fige("#F0F0F0");
    public static readonly SolidColorBrush TexteSecondaire = Fige("#A0A0A0");
    public static readonly SolidColorBrush Accent = Fige("#6E6CF3");        // violet maquette (oklch 0.62 0.19 278)
    public static readonly SolidColorBrush AccentSurvol = Fige("#918FF6");

    private static SolidColorBrush Fige(string hex)
    {
        var pinceau = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        pinceau.Freeze();
        return pinceau;
    }

    public static void Appliquer()
        => Application.Current.Resources.MergedDictionaries.Add(CreerRessources());

    /// Fond sombre + barre de titre sombre (attribut DWM 20, Windows 10 1809+).
    public static void Sombre(Window fenetre)
    {
        fenetre.Background = FondFenetre;
        fenetre.SourceInitialized += (_, _) =>
        {
            int actif = 1;
            _ = DwmSetWindowAttribute(new WindowInteropHelper(fenetre).Handle, 20, ref actif, sizeof(int));
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribut, ref int valeur, int taille);

    public static ResourceDictionary CreerRessources()
    {
        var dico = new ResourceDictionary();

        var label = new Style(typeof(Label));
        label.Setters.Add(new Setter(Control.ForegroundProperty, Texte));
        dico.Add(typeof(Label), label);

        // Les styles implicites de TextBlock (non-Control) ne fuient pas dans les templates.
        var bloc = new Style(typeof(TextBlock));
        bloc.Setters.Add(new Setter(TextBlock.ForegroundProperty, Texte));
        dico.Add(typeof(TextBlock), bloc);

        var boite = new Style(typeof(TextBox));
        boite.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
        boite.Setters.Add(new Setter(Control.ForegroundProperty, Texte));
        boite.Setters.Add(new Setter(Control.BorderBrushProperty, Bordure));
        boite.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, Texte));
        boite.Setters.Add(new Setter(TextBoxBase.SelectionBrushProperty, Accent));
        boite.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 4, 2)));
        dico.Add(typeof(TextBox), boite);

        var coche = new Style(typeof(CheckBox));
        coche.Setters.Add(new Setter(Control.ForegroundProperty, Texte));
        dico.Add(typeof(CheckBox), coche);

        var radio = new Style(typeof(RadioButton));
        radio.Setters.Add(new Setter(Control.ForegroundProperty, Texte));
        dico.Add(typeof(RadioButton), radio);

        var bouton = new Style(typeof(Button));
        bouton.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
        bouton.Setters.Add(new Setter(Control.ForegroundProperty, Texte));
        bouton.Setters.Add(new Setter(Control.BorderBrushProperty, Bordure));
        bouton.Setters.Add(new Setter(Control.TemplateProperty, ParseTemplate(GabaritBouton)));
        dico.Add(typeof(Button), bouton);

        var combo = new Style(typeof(ComboBox));
        combo.Setters.Add(new Setter(Control.ForegroundProperty, Texte));
        combo.Setters.Add(new Setter(Control.TemplateProperty, ParseTemplate(GabaritCombo)));
        dico.Add(typeof(ComboBox), combo);

        var element = new Style(typeof(ComboBoxItem));
        element.Setters.Add(new Setter(Control.ForegroundProperty, Texte));
        element.Setters.Add(new Setter(Control.TemplateProperty, ParseTemplate(GabaritComboItem)));
        dico.Add(typeof(ComboBoxItem), element);

        var barre = new Style(typeof(ScrollBar));
        barre.Setters.Add(new Setter(FrameworkElement.WidthProperty, 10.0));
        barre.Setters.Add(new Setter(Control.TemplateProperty, ParseTemplate(GabaritScrollBar)));
        dico.Add(typeof(ScrollBar), barre);

        // Style nommé (pas implicite) : CheckBox rendue en interrupteur 40×20.
        var interrupteur = new Style(typeof(CheckBox));
        interrupteur.Setters.Add(new Setter(Control.TemplateProperty, ParseTemplate(GabaritInterrupteur)));
        dico.Add("Interrupteur", interrupteur);

        return dico;
    }

    private static ControlTemplate ParseTemplate(string xaml) => (ControlTemplate)XamlReader.Parse(xaml);

    private const string Ns =
        "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
        "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";

    private const string GabaritBouton = $@"
<ControlTemplate {Ns} TargetType='Button'>
  <Border x:Name='fond' Background='{{TemplateBinding Background}}' BorderBrush='{{TemplateBinding BorderBrush}}'
          BorderThickness='1' CornerRadius='3' Padding='{{TemplateBinding Padding}}'>
    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='fond' Property='Background' Value='#3A3A3A'/></Trigger>
    <Trigger Property='IsPressed' Value='True'><Setter TargetName='fond' Property='Background' Value='#454545'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

    private const string GabaritCombo = $@"
<ControlTemplate {Ns} TargetType='ComboBox'>
  <Grid>
    <ToggleButton Focusable='False' ClickMode='Press'
                  IsChecked='{{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={{RelativeSource TemplatedParent}}}}'>
      <ToggleButton.Template>
        <ControlTemplate TargetType='ToggleButton'>
          <Border x:Name='fond' Background='#2D2D2D' BorderBrush='#3F3F3F' BorderThickness='1' CornerRadius='3'>
            <Path Data='M 0 0 L 4 4 L 8 0 Z' Fill='#F0F0F0' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,8,0'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='fond' Property='Background' Value='#3A3A3A'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </ToggleButton.Template>
    </ToggleButton>
    <ContentPresenter Content='{{TemplateBinding SelectionBoxItem}}' ContentTemplate='{{TemplateBinding SelectionBoxItemTemplate}}'
                      Margin='8,3,24,3' VerticalAlignment='Center' IsHitTestVisible='False'/>
    <Popup IsOpen='{{TemplateBinding IsDropDownOpen}}' Placement='Bottom' AllowsTransparency='True' Focusable='False'>
      <Border Background='#2D2D2D' BorderBrush='#3F3F3F' BorderThickness='1' CornerRadius='3'
              MinWidth='{{TemplateBinding ActualWidth}}' MaxHeight='{{TemplateBinding MaxDropDownHeight}}'>
        <ScrollViewer><ItemsPresenter/></ScrollViewer>
      </Border>
    </Popup>
  </Grid>
</ControlTemplate>";

    private const string GabaritComboItem = $@"
<ControlTemplate {Ns} TargetType='ComboBoxItem'>
  <Border x:Name='fond' Background='Transparent' Padding='8,4,8,4'>
    <ContentPresenter/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='fond' Property='Background' Value='#3A3A3A'/></Trigger>
    <Trigger Property='IsSelected' Value='True'><Setter TargetName='fond' Property='Background' Value='#454545'/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

    private const string GabaritInterrupteur = $@"
<ControlTemplate {Ns} TargetType='CheckBox'>
  <Border x:Name='piste' Width='40' Height='20' CornerRadius='10' Background='#3F3F3F'>
    <Border x:Name='pouce' Width='14' Height='14' CornerRadius='7' Background='#F0F0F0'
            HorizontalAlignment='Left' Margin='3,0,0,0'/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsChecked' Value='True'>
      <Setter TargetName='piste' Property='Background' Value='#6E6CF3'/>
      <Setter TargetName='pouce' Property='HorizontalAlignment' Value='Right'/>
      <Setter TargetName='pouce' Property='Margin' Value='0,0,3,0'/>
    </Trigger>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='pouce' Property='Background' Value='#FFFFFF'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

    private const string GabaritScrollBar = $@"
<ControlTemplate {Ns} TargetType='ScrollBar'>
  <Grid Background='#1E1E1E'>
    <Track x:Name='PART_Track' IsDirectionReversed='True'>
      <Track.Thumb>
        <Thumb>
          <Thumb.Template>
            <ControlTemplate TargetType='Thumb'>
              <Border Background='#3F3F3F' CornerRadius='4' Margin='2'/>
            </ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
    </Track>
  </Grid>
</ControlTemplate>";
}
