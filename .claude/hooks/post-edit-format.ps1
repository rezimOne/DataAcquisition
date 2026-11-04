# PostToolUse (Edit|Write|MultiEdit) - uruchamia formatter dla zmienionego pliku.
# Mapowanie rozszerzenie -> komenda w .claude/hooks/format.json jako lista argumentow,
# np. [".\\node_modules\\.bin\\prettier", "--write", "{file}"]; {file} = sciezka pliku.
# Komenda jest wywolywana bezposrednio (bez interpretacji przez powloke), wiec nazwa
# pliku nie moze wstrzyknac polecenia. Brak wpisu = brak akcji. Hook nie blokuje.

$ErrorActionPreference = 'Stop'

try { [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
$data = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = $data.tool_input.file_path
if (-not $path -or -not (Test-Path -LiteralPath $path)) { exit 0 }

$root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { $data.cwd }
$configFile = Join-Path $root '.claude/hooks/format.json'
if (-not (Test-Path -LiteralPath $configFile)) { exit 0 }

$config = Get-Content -Raw -LiteralPath $configFile | ConvertFrom-Json
$ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
$prop = $config.PSObject.Properties | Where-Object { $_.Name -eq $ext } | Select-Object -First 1
if (-not $prop -or -not $prop.Value) { exit 0 }

$argv = @($prop.Value | ForEach-Object { ([string]$_).Replace('{file}', $path) })
if ($argv.Count -eq 0 -or -not $argv[0]) { exit 0 }
$exe = $argv[0]
$rest = if ($argv.Count -gt 1) { $argv[1..($argv.Count - 1)] } else { @() }

Push-Location -LiteralPath $root
try {
    $output = & $exe @rest 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("[SDLC] Formatter zwrocil kod $LASTEXITCODE dla '$path': $output")
        exit 1
    }
} catch {
    [Console]::Error.WriteLine("[SDLC] Nie udalo sie uruchomic formattera '$exe': $($_.Exception.Message)")
    exit 1
} finally {
    Pop-Location
}
exit 0
