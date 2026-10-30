param(
    [Parameter(Mandatory=$true)][string]$PgBin,
    [Parameter(Mandatory=$true)][string]$FfmpegBin,
    [string]$Dotnet = 'dotnet', [int]$PostgresPort = 55433
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (Get-NetTCPConnection -State Listen -LocalPort 5080 -ErrorAction SilentlyContinue) { throw 'Port 5080 is already occupied; stop the test instance first.' }
$id = [Guid]::NewGuid().ToString('N')
$root = Join-Path $repository ".audit\acceptance-$id"
$database = "notebook_acceptance_$id"
$runtime = Join-Path $root 'runtime'
$null = New-Item -ItemType Directory -Path $root
Push-Location (Join-Path $repository 'web')
try { & npm run build; if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' } } finally { Pop-Location }
& $Dotnet publish (Join-Path $repository 'src\NotebookYt.Api') -o $runtime --nologo
if ($LASTEXITCODE -ne 0) { throw 'Backend publish failed.' }
Copy-Item (Join-Path $repository 'web\dist') (Join-Path $runtime 'wwwroot') -Recurse
& (Join-Path $PgBin 'createdb.exe') --host=127.0.0.1 --port=$PostgresPort --username=postgres --no-password $database
if ($LASTEXITCODE -ne 0) { throw 'Creating isolated acceptance database failed.' }
# This publicly documented password belongs exclusively to this isolated test instance.
# Never reuse this profile for operational data or expose it beyond loopback.
$salt = New-Object byte[] 16
$rng = [Security.Cryptography.RandomNumberGenerator]::Create(); $rng.GetBytes($salt); $rng.Dispose()
$derive = New-Object Security.Cryptography.Rfc2898DeriveBytes('controlled-acceptance-password', $salt, 100000, [Security.Cryptography.HashAlgorithmName]::SHA256)
$hash = '100000:' + [Convert]::ToBase64String($salt) + ':' + [Convert]::ToBase64String($derive.GetBytes(32)); $derive.Dispose()
$settings = @{
    ConnectionStrings__Editorial = "Host=127.0.0.1;Port=$PostgresPort;Database=$database;Username=postgres"
    Hosting__Urls = 'http://127.0.0.1:5080'; ASPNETCORE_ENVIRONMENT = 'Production'
    Security__RequireAuthentication = 'true'; Security__PasswordHash = $hash
    Security__KeyDirectory = (Join-Path $root 'keys'); Storage__Root = (Join-Path $root 'media')
    Media__ffmpegPath = (Join-Path $FfmpegBin 'ffmpeg.exe'); Media__ffprobePath = (Join-Path $FfmpegBin 'ffprobe.exe')
    Worker__Enabled = 'true'
}
$previous = @{}
try {
    foreach ($name in $settings.Keys) { $previous[$name] = [Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name, $settings[$name]) }
    Push-Location $runtime
    try {
        & $Dotnet NotebookYt.Api.dll --migrate
        if ($LASTEXITCODE -ne 0) { throw 'Migration failed.' }
        $process = Start-Process -FilePath $Dotnet -ArgumentList 'NotebookYt.Api.dll' -WorkingDirectory $runtime -WindowStyle Hidden -RedirectStandardOutput (Join-Path $root 'api.log') -RedirectStandardError (Join-Path $root 'api-error.log') -PassThru
    } finally { Pop-Location }
} finally { foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) } }
$ready = $false
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    try { $null = Invoke-RestMethod http://127.0.0.1:5080/health; $ready = $true; break } catch { Start-Sleep -Seconds 1 }
}
if (-not $ready) { throw "Acceptance startup failed. Inspect $root" }
@{ database = $database; postgresPort = $PostgresPort; root = $root; processId = $process.Id; url = 'http://127.0.0.1:5080' } | ConvertTo-Json | Set-Content (Join-Path $root 'instance.json') -Encoding UTF8
Write-Output "Authenticated isolated acceptance instance: $root"
Write-Output 'Run web tests with PLAYWRIGHT_BASE_URL=http://127.0.0.1:5080. Preserve instance.json for restart and cleanup.'
