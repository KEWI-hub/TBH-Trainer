param(
    [int]$X1Minutes = 21,      # x1 already has ~9 min from the user's own run
    [int]$X5Minutes = 30,
    [int]$SnapshotMinutes = 5,
    [string]$Phases = '',      # e.g. '1:15,5:15,1:15,5:15' overrides X1/X5Minutes
    [switch]$Clear,            # wipe the hook's chest counters first
    [string]$Out = 'D:\TrainerBuild\drop_test_log.txt'
)
# Drives the x1-vs-x5 chest test through the trainer's own shared-memory channels:
# TBHTrainerShared (speed) and TBHTrainerHeroes (command 37 = chest log, read-only).
Add-Type -AssemblyName System.Core
$ErrorActionPreference = 'Stop'

function Log([string]$s) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $s
    Add-Content -Path $Out -Value $line -Encoding UTF8
    Write-Output $line
}

$shared = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting('TBHTrainerShared')
$sv = $shared.CreateViewAccessor(0, 132)
if ($sv.ReadInt32(0) -ne 0x31484254) { throw 'TBHTrainerShared magic mismatch' }
$heroes = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting('TBHTrainerHeroes')
$hv = $heroes.CreateViewAccessor(0, 524288)
if ($hv.ReadInt32(0) -ne 0x48524854) { throw 'TBHTrainerHeroes magic mismatch' }

function SetSpeed([float]$scale) {
    $sv.Write([long]0x0C, [single]$scale)
    $sv.Write([long]0x08, [int]($(if ($scale -gt 1.01) { 1 } else { 0 })))
}

function ChestLog([float]$clear = 0) {
    $hv.Write([long]16, [int]37)
    $hv.Write([long]20, [int]0)
    $hv.Write([long]24, [single]$clear)
    $hv.Write([long]8, [int]0)
    $hv.Write([long]12, [int]0)
    $hv.Write([long]4, [int]($hv.ReadInt32(4) + 1))
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        $done = $hv.ReadInt32(8)
        if ($done -eq 1 -or $done -lt 0) {
            $len = [Math]::Min([Math]::Max($hv.ReadInt32(12), 0), 524288 - 33)
            $buf = New-Object byte[] $len
            [void]$hv.ReadArray([long]32, $buf, 0, $len)
            return [System.Text.Encoding]::UTF8.GetString($buf)
        }
        Start-Sleep -Milliseconds 40
    }
    return $null
}

function Snapshot([string]$label) {
    $r = ChestLog
    if (-not $r) { Log "$label : hook did not answer"; return '' }
    # keep the summary, not the per-change list
    $summary = ($r -split "`r?`n") | Where-Object { $_ -match '^\s+x\d:|box request guard|watching for' }
    Log "$label"
    foreach ($l in $summary) { Log "  $($l.Trim())" }
    return $r
}

function Blocked([string]$r) { return $r -match 'BLOCKED now' }

function RunPhase([float]$scale, [int]$minutes, [string]$name) {
    SetSpeed $scale
    Log "=== phase $name : speed x$scale for $minutes min ==="
    $end = (Get-Date).AddMinutes($minutes)
    $next = (Get-Date).AddMinutes($SnapshotMinutes)
    while ((Get-Date) -lt $end) {
        Start-Sleep -Seconds 20
        if ($sv.ReadInt32(0x08) -ne $(if ($scale -gt 1.01) { 1 } else { 0 })) {
            Log "speed was changed from outside (trainer UI?) - putting x$scale back"
            SetSpeed $scale
        }
        if ((Get-Date) -ge $next) {
            $r = Snapshot "snapshot ($name)"
            $next = (Get-Date).AddMinutes($SnapshotMinutes)
            if (Blocked $r) {
                Log "AbuseGuard BLOCKED during $name - back to x1 and stopping the fast phase"
                SetSpeed 1.0
                $script:GuardBlocked = $true
                return $false
            }
        }
    }
    return $true
}

try {
    if (-not $Phases) { $Phases = "1:$X1Minutes,5:$X5Minutes" }
    Log "drop test started (phases $Phases)"
    if ($Clear) { [void](ChestLog 1); Log 'chest counters cleared' }
    [void](Snapshot 'start')
    $n = 0
    foreach ($p in $Phases -split ',') {
        $n++
        $scale, $mins = $p -split ':'
        # RunPhase's own output also carries the log lines, so a block is signalled by a flag
        RunPhase ([float]$scale) ([int]$mins) "x$scale #$n" | Out-Null
        if ($script:GuardBlocked) { break }
    }
}
finally {
    SetSpeed 1.0
    Log 'speed restored to x1'
    $final = ChestLog
    if ($final) {
        Log '=== final chest log ==='
        Add-Content -Path $Out -Value $final -Encoding UTF8
        Write-Output $final
    }
    $hv.Dispose(); $heroes.Dispose(); $sv.Dispose(); $shared.Dispose()
}
