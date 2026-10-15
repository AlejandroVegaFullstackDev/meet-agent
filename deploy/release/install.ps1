# MeetAgent: instalador para Windows (sin administrador). Copia el conector y el MCP a
# %LOCALAPPDATA%\MeetAgent\bin y los registra (HKCU) para la extensión en Chrome, Edge y Firefox.
# Uso:  powershell -ExecutionPolicy Bypass -File install.ps1     ·  Desinstalar: ... -File install.ps1 -Remove
param([switch]$Remove)
$ErrorActionPreference = 'Stop'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$App = Join-Path $env:LOCALAPPDATA 'MeetAgent'
$Bin = Join-Path $App 'bin'
$Name = 'com.meetagent.host'
$GeckoId = 'meetagent@alejandrovega.dev'
$ChromiumIds = (Get-Content (Join-Path $Here 'chromium-ids.txt') -Raw).Trim() -split '\s+'
$Keys = @{
  'HKCU:\Software\Google\Chrome\NativeMessagingHosts'    = 'chromium'
  'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts'   = 'chromium'
  'HKCU:\Software\Mozilla\NativeMessagingHosts'          = 'firefox'
}

if ($Remove) {
  foreach ($k in $Keys.Keys) { Remove-Item -Path "$k\$Name" -ErrorAction SilentlyContinue }
  Remove-Item -Recurse -Force $Bin -ErrorAction SilentlyContinue
  Write-Host "MeetAgent desinstalado. Tus reuniones siguen en $App\data."
  exit 0
}

New-Item -ItemType Directory -Force -Path $Bin, (Join-Path $App 'data') | Out-Null
Copy-Item (Join-Path $Here 'bin\meetagent-host.exe'), (Join-Path $Here 'bin\meetagent-mcp.exe') $Bin -Force
$HostExe = Join-Path $Bin 'meetagent-host.exe'

$chromium = @{ name = $Name; description = 'MeetAgent'; path = $HostExe; type = 'stdio'
               allowed_origins = @($ChromiumIds | ForEach-Object { "chrome-extension://$_/" }) }
$firefox  = @{ name = $Name; description = 'MeetAgent'; path = $HostExe; type = 'stdio'; allowed_extensions = @($GeckoId) }
$files = @{
  chromium = Join-Path $App "$Name.chromium.json"
  firefox  = Join-Path $App "$Name.firefox.json"
}
# UTF-8 SIN BOM: Set-Content -Encoding UTF8 de PowerShell 5 agrega BOM y Chrome no lee el manifest.
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($files.chromium, ($chromium | ConvertTo-Json), $utf8)
[IO.File]::WriteAllText($files.firefox,  ($firefox  | ConvertTo-Json), $utf8)
foreach ($k in $Keys.Keys) {
  New-Item -Path "$k\$Name" -Force | Out-Null
  Set-ItemProperty -Path "$k\$Name" -Name '(default)' -Value $files[$Keys[$k]]
}
Write-Host "MeetAgent instalado en $App"
Write-Host "  Reuniones: $App\data\meetagent.db"
Write-Host "  Falta: cargar la extensión (ver LEEME.md) y, si usas IA por MCP:"
Write-Host "    claude mcp add meetagent -- `"$Bin\meetagent-mcp.exe`""
