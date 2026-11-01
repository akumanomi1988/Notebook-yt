param(
    [Parameter(Mandatory=$true)][string]$BackupDirectory,
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-z][a-z0-9_]{0,62}$')][string]$NewDatabase,
    [Parameter(Mandatory=$true)][string]$MediaDirectory,
    [Parameter(Mandatory=$true)][string]$KeyDirectory,
    [string]$AdditionalDirectoryMappings = '',
    [switch]$PromptForPassword,
    [string]$PgBin = '',
    [string]$Server = '127.0.0.1', [int]$Port = 5432, [string]$Username = 'notebookyt'
)
$ErrorActionPreference = 'Stop'
if ($Server -notin @('127.0.0.1', 'localhost', '::1')) { throw 'This script only supports local databases.' }
if ((Test-Path -LiteralPath $MediaDirectory) -or (Test-Path -LiteralPath $KeyDirectory)) { throw 'Restore directories must not exist.' }
$mediaPath = [IO.Path]::GetFullPath($MediaDirectory).TrimEnd('\')
$keyPath = [IO.Path]::GetFullPath($KeyDirectory).TrimEnd('\')
if ($mediaPath -eq $keyPath -or $mediaPath.StartsWith($keyPath + '\', [StringComparison]::OrdinalIgnoreCase) -or $keyPath.StartsWith($mediaPath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Restore directories must be separate.' }
$backupPath = (Get-Item -LiteralPath $BackupDirectory).FullName.TrimEnd('\')
$manifest = Get-Content -LiteralPath (Join-Path $backupPath 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.version -ne 1) { throw 'Unsupported backup version.' }
$additionalTargets = @{}
$mappings = if ([string]::IsNullOrWhiteSpace($AdditionalDirectoryMappings)) { @() } else { @($AdditionalDirectoryMappings -split '\|') }
foreach ($mapping in $mappings) {
    $parts = $mapping -split '=', 2
    if ($parts.Length -ne 2 -or $parts[0] -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' -or $additionalTargets.ContainsKey($parts[0])) {
        throw 'Additional directory mappings must use unique name=path entries.'
    }
    $additionalTargets[$parts[0]] = [IO.Path]::GetFullPath($parts[1]).TrimEnd('\')
}
$additionalManifest = @($manifest.additionalDirectories)
if (@($additionalManifest).Count -ne $additionalTargets.Count) { throw 'Every backed up additional directory requires one restore mapping.' }
foreach ($entry in $additionalManifest) {
    if ($entry.name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' -or $entry.path -ne "additional/$($entry.name)" -or -not $additionalTargets.ContainsKey($entry.name)) {
        throw 'Additional directory manifest is invalid.'
    }
    if (Test-Path -LiteralPath $additionalTargets[$entry.name]) { throw 'Additional restore directories must not already exist.' }
}
function Get-SafePath([string]$Root, [string]$Relative) {
    $path = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $path.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe backup path.' }
    return $path
}
if ((Get-Item -LiteralPath $backupPath).Attributes -band [IO.FileAttributes]::ReparsePoint -or
    (Get-ChildItem -LiteralPath $backupPath -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })) { throw 'Linked backup entries are not supported.' }
foreach ($file in $manifest.files) {
    $path = Get-SafePath $backupPath $file.path
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw 'Backup checksum mismatch.' }
}
$actualFiles = @(Get-ChildItem -LiteralPath $backupPath -File -Force -Recurse | Where-Object Name -ne 'manifest.json')
if ($actualFiles.Count -ne @($manifest.files).Count) { throw 'Unexpected backup files.' }
foreach ($permission in $manifest.permissions) { $null = Get-SafePath $backupPath $permission.path }
$psql = if ($PgBin) { Join-Path $PgBin 'psql.exe' } else { 'psql' }
$createdb = if ($PgBin) { Join-Path $PgBin 'createdb.exe' } else { 'createdb' }
$restore = if ($PgBin) { Join-Path $PgBin 'pg_restore.exe' } else { 'pg_restore' }
$passwordMode = if ($PromptForPassword) { '--password' } else { '--no-password' }
$exists = & $psql --host=$Server --port=$Port --username=$Username $passwordMode --dbname=postgres -Atc "SELECT 1 FROM pg_database WHERE datname = '$NewDatabase'"
if ($LASTEXITCODE -ne 0) { throw 'Database preflight failed.' }
if ($exists -eq '1') { throw 'Target database already exists; refusing to overwrite.' }
& $createdb --host=$Server --port=$Port --username=$Username $passwordMode $NewDatabase
if ($LASTEXITCODE -ne 0) { throw 'Creating isolated target database failed.' }
& $restore --host=$Server --port=$Port --username=$Username $passwordMode --exit-on-error --dbname=$NewDatabase (Join-Path $backupPath 'database.dump')
if ($LASTEXITCODE -ne 0) { throw 'Database restore failed. Inspect the new target; no existing database was overwritten.' }
$null = New-Item -ItemType Directory -Path $mediaPath
$null = New-Item -ItemType Directory -Path $keyPath
foreach ($pair in @(@('media', $mediaPath), @('keys', $keyPath))) {
    Get-ChildItem -LiteralPath (Join-Path $backupPath $pair[0]) -Force | Copy-Item -Destination $pair[1] -Recurse -Force
}
foreach ($entry in $additionalManifest) {
    $target = $additionalTargets[$entry.name]
    $null = New-Item -ItemType Directory -Path $target
    Get-ChildItem -LiteralPath (Get-SafePath $backupPath $entry.path) -Force | Copy-Item -Destination $target -Recurse -Force
}
foreach ($permission in $manifest.permissions | Sort-Object { $_.path.Length } -Descending) {
    $parts = $permission.path -split '/', 2
    if ($parts[0] -eq 'media') { $root = $mediaPath }
    elseif ($parts[0] -eq 'keys') { $root = $keyPath }
    elseif ($parts[0] -eq 'additional') {
        $extra = $parts[1] -split '/', 2
        if (-not $additionalTargets.ContainsKey($extra[0])) { throw 'Missing additional directory permission mapping.' }
        $root = $additionalTargets[$extra[0]]
        if ($extra.Length -gt 1) { $parts = @('additional', $extra[1]) } else { $parts = @('additional') }
    }
    else { throw 'Unknown permission root.' }
    $target = if ($parts.Length -eq 1) { $root } else { Get-SafePath $root $parts[1] }
    $acl = Get-Acl -LiteralPath $target
    $acl.SetSecurityDescriptorSddlForm($permission.sddl)
    Set-Acl -LiteralPath $target -AclObject $acl
}
Write-Output 'Restore complete. Configure the new database, media and key paths before starting the application.'
