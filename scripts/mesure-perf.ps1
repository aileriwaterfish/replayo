# Mesure CPU/RAM de Replayo sur 60 s (a lancer pendant une capture active).
# Script en ASCII pur (compatibilite PowerShell 5.1 sans BOM).
$proc = Get-Process Replayo -ErrorAction Stop
$echantillons = @()
for ($i = 0; $i -lt 60; $i++) {
    $avant = $proc.TotalProcessorTime
    Start-Sleep -Seconds 1
    $proc.Refresh()
    $cpuPct = ($proc.TotalProcessorTime - $avant).TotalMilliseconds / 10 / [Environment]::ProcessorCount
    $echantillons += [pscustomobject]@{ CpuPct = [math]::Round($cpuPct, 2); RamMo = [math]::Round($proc.WorkingSet64 / 1MB) }
}
$cpu = [math]::Round(($echantillons | Measure-Object CpuPct -Average).Average, 2)
$ram = ($echantillons | Measure-Object RamMo -Maximum).Maximum
Write-Output ("CPU moyen : {0} pct - RAM max : {1} Mo" -f $cpu, $ram)
Write-Output "Objectifs : CPU sous 5 pct (encodeur materiel), RAM sous 200 Mo hors segment courant"
