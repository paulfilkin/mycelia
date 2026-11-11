# Makes the pictures for the help pages (Docs/Help/images) from the sample tree, using the
# program's screenshot mode.  Run it from the repository root after a change to a screen the help
# shows, then look the pictures over before committing them.
#
#   powershell -ExecutionPolicy Bypass -File tools\help-shots.ps1
#   powershell -ExecutionPolicy Bypass -Command "& .\tools\help-shots.ps1 -Only people,charts"
#
# (-Only needs -Command: with -File, "people,charts" arrives as one name and matches nothing.)
#
# It uses the Debug build (build the solution first) unless -Exe names another Mycelia.exe.
# Screenshot mode opens the invented sample tree, never reads or writes settings, and sends
# nothing to an AI service, so the pictures never show anyone's own tree or details.

param(
    [string]$Exe = "familyTree\bin\Debug\net10.0-windows\Mycelia.exe",
    [string[]]$Only = @(),
    [int]$MaxWidth = 1600
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$exePath = if ([IO.Path]::IsPathRooted($Exe)) { $Exe } else { Join-Path $root $Exe }
if (-not (Test-Path $exePath)) { throw "No program at $exePath.  Build the solution first." }
$outDir = Join-Path $root "Docs\Help\images"
New-Item -ItemType Directory -Force $outDir | Out-Null

# Picture name, then the switches after --screenshot <file>.
$shots = [ordered]@{
    "home"                   = @("--workspace", "Home")
    "people"                 = @("--workspace", "People")
    "person-view"            = @("--workspace", "Person")
    "places"                 = @("--workspace", "Places")
    "sources"                = @("--workspace", "Sources")
    "media"                  = @("--workspace", "Media")
    "charts"                 = @("--workspace", "Charts")
    "tools"                  = @("--workspace", "Tools")
    "assistant"              = @("--workspace", "People", "--assistant-demo")
    "options-general"        = @("--options", "General")
    "options-dates"          = @("--options", "Dates and names")
    "options-warnings"       = @("--options", "Warnings")
    "options-ai-model"       = @("--options", "AI model")
    "options-assistant"      = @("--options", "Assistant")
    "options-mcp-servers"    = @("--options", "MCP servers")
    "options-research"       = @("--options", "Research")
    "options-other-programs" = @("--options", "Other programs")
    "options-updates"        = @("--options", "Updates")
    "mcp-helper"             = @("--options", "MCP Helper")
}

foreach ($name in $shots.Keys) {
    if ($Only.Count -gt 0 -and $Only -notcontains $name) { continue }
    $file = Join-Path $outDir "$name.png"
    $raw = Join-Path $env:TEMP "mycelia-help-$name.png"
    Remove-Item $raw, "$raw.error.txt" -ErrorAction SilentlyContinue
    $arguments = @("--screenshot", "`"$raw`"") + ($shots[$name] | ForEach-Object { if ($_ -match " ") { "`"$_`"" } else { $_ } })
    $p = Start-Process -FilePath $exePath -ArgumentList $arguments -PassThru
    if (-not $p.WaitForExit(60000)) { $p.Kill(); throw "$name took too long." }
    if (Test-Path "$raw.error.txt") { Write-Warning "$name drew with an error: $raw.error.txt" }
    if (-not (Test-Path $raw)) { throw "$name made no picture." }

    # Scale down to MaxWidth: the window is drawn at the screen's DPI, which makes large files.
    $img = [System.Drawing.Image]::FromFile($raw)
    try {
        $w = [Math]::Min($img.Width, $MaxWidth)
        $h = [int][Math]::Round($img.Height * $w / $img.Width)
        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($img, 0, 0, $w, $h)
        $g.Dispose()
        $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }
    finally { $img.Dispose() }
    Remove-Item $raw
    Write-Host ("{0,-24} {1,5:N0} KB" -f "$name.png", ((Get-Item $file).Length / 1KB))
}
