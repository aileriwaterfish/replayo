using Replayo.Core;

if (args is ["--apply-update", var dossier, var installation, var pidTexte]
    && int.TryParse(pidTexte, out var pid))
    return UpdateInstaller.Executer(dossier, installation, pid);

Console.Error.WriteLine("Usage : Replayo.Updater --apply-update <dossier> <installation> <pid>");
return 2;
