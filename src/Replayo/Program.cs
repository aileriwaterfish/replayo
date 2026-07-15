using Replayo;
using Replayo.Core;
using Replayo.Input;

// Version transitoire console (Plan B Task 2) — remplacée par l'app tray en Task 3.
var config = new ConfigStore().Charger();
using var recorder = new RecorderService();
recorder.Notification += m => Console.WriteLine($"[note] {m}");
recorder.Demarrer(config);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
using var hotkey = new HotkeyManager();
hotkey.Enregistrer(config.RaccourciModificateurs, config.RaccourciTouche, () =>
    _ = Task.Run(async () => { foreach (var c in await recorder.ClipperAsync()) Console.WriteLine($"[clip] ✓ {c}"); }));
Console.WriteLine("Alt+F10 → clip. Ctrl+C → quitter.");
try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (OperationCanceledException) { }
recorder.Arreter();
return 0;
