using Microsoft.Win32;

namespace Replayo.Core;

/// Lancement avec Windows via HKCU\...\Run (source de vérité : le registre).
public static class AutostartManager
{
    private const string Cle = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NomDefaut = "Replayo";

    public static bool EstActive() => EstActive(NomDefaut);
    public static void Activer() => Activer(NomDefaut);
    public static void Desactiver() => Desactiver(NomDefaut);

    // Surcharges réservées aux tests. Activer() écrit Environment.ProcessPath, qui vaut
    // testhost.exe sous le runner : un test qui écrit sur la vraie valeur "Replayo" y laisse
    // le chemin du runner, et Replayo ne démarre plus avec la session (constaté le 31/07/2026,
    // une game perdue). Les tests passent donc un nom de valeur bidon.
    internal static bool EstActive(string nomValeur)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Cle);
        return k?.GetValue(nomValeur) is string;
    }

    internal static void Activer(string nomValeur)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Cle);
        // Environment.ProcessPath = chemin réel de l'exe (jamais dotnet.exe en publié).
        k.SetValue(nomValeur, $"\"{Environment.ProcessPath}\"");
    }

    internal static void Desactiver(string nomValeur)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Cle);
        k.DeleteValue(nomValeur, throwOnMissingValue: false);
    }
}
