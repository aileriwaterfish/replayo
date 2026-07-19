using Replayo.Input;
using Xunit;

public class HotkeyTests
{
    [Theory]
    [InlineData(0x79u, 0x1u, 0x79u, 0x1u, true)]   // Alt+F10 attendu, Alt+F10 pressé
    [InlineData(0x78u, 0x1u, 0x79u, 0x1u, false)]  // mauvaise touche (F9)
    [InlineData(0x79u, 0x0u, 0x79u, 0x1u, false)]  // Alt manquant
    [InlineData(0x79u, 0x3u, 0x79u, 0x1u, false)]  // Ctrl en trop
    [InlineData(0x79u, 0x6u, 0x79u, 0x6u, true)]   // Ctrl+Shift+F10 exact
    public void Correspond_CompareToucheEtModificateursExactement(uint vk, uint mods, uint vkCfg, uint modsCfg, bool attendu)
        => Assert.Equal(attendu, HotkeyManager.Correspond(vk, mods, vkCfg, modsCfg));
}
