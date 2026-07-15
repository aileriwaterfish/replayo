using Microsoft.Win32;

namespace Replayo.Core;

/// Lancement avec Windows via HKCU\...\Run (source de vérité : le registre).
public static class AutostartManager
{
    private const string Cle = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Nom = "Replayo";

    public static bool EstActive()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Cle);
        return k?.GetValue(Nom) is string;
    }

    public static void Activer()
    {
        using var k = Registry.CurrentUser.CreateSubKey(Cle);
        // Environment.ProcessPath = chemin réel de l'exe (jamais dotnet.exe en publié).
        k.SetValue(Nom, $"\"{Environment.ProcessPath}\"");
    }

    public static void Desactiver()
    {
        using var k = Registry.CurrentUser.CreateSubKey(Cle);
        k.DeleteValue(Nom, throwOnMissingValue: false);
    }
}
