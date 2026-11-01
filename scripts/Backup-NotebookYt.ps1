param(
    [Parameter(Mandatory=$true)][string]$Database,
    [Parameter(Mandatory=$true)][string]$MediaDirectory,
    [Parameter(Mandatory=$true)][string]$KeyDirectory,
    [Parameter(Mandatory=$true)][string]$Destination,
    [Parameter(Mandatory=$true)][switch]$ApplicationStopped,
    [string]$AdditionalDirectories = '',
    [switch]$PromptForPassword,
    [string]$PgBin = '',
    [string]$Server = '127.0.0.1', [int]$Port = 5432, [string]$Username = 'notebookyt',
    [string[]]$ConfigurationReferences = @('ConnectionStrings__Editorial', 'Google__ClientId', 'Google__ClientSecret')
)
$ErrorActionPreference = 'Stop'
if (-not $ApplicationStopped) { throw 'Stop the application and worker before backup.' }
if ($Server -notin @('127.0.0.1', 'localhost', '::1')) { throw 'This script only supports local databases.' }
if (Test-Path -LiteralPath $Destination) { throw 'Destination must not exist.' }
$roots = @(
    [pscustomobject]@{ Name = 'media'; Path = (Get-Item -LiteralPath $MediaDirectory).FullName },
    [pscustomobject]@{ Name = 'keys'; Path = (Get-Item -LiteralPath $KeyDirectory).FullName }
)
$extraNames = @{}
$additionalSources = if ([string]::IsNullOrWhiteSpace($AdditionalDirectories)) { @() } else { @($AdditionalDirectories -split '\|') }
foreach ($additionalDirectory in $additionalSources) {
    $sourcePath = (Get-Item -LiteralPath $additionalDirectory).FullName
    $name = Split-Path -Leaf $sourcePath
    if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' -or $name -in @('media', 'keys', 'database.dump', 'manifest.json') -or $extraNames.ContainsKey($name)) {
        throw 'Additional directory names must be unique and use safe filename characters.'
    }
    $extraNames[$name] = $true
    $roots += [pscustomobject]@{ Name = "additional/$name"; Path = $sourcePath }
}
foreach ($root in $roots) {
    $item = Get-Item -LiteralPath $root.Path
    if (-not $item.PSIsContainer) { throw 'All backup roots must be directories.' }
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        (Get-ChildItem -LiteralPath $item.FullName -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })) {
        throw 'Linked storage entries are not supported.'
    }
}
$destinationPath = [IO.Path]::GetFullPath($Destination)
foreach ($root in $roots) {
    if ($destinationPath.StartsWith($root.Path.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $root.Path.StartsWith($destinationPath.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Backup and source roots must not contain one another.' }
}
$null = New-Item -ItemType Directory -Path $destinationPath
# Keep the archive private: it contains protected credentials and Data Protection keys.
$acl = Get-Acl -LiteralPath $destinationPath
$acl.SetAccessRuleProtection($true, $false)
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule([Security.Principal.WindowsIdentity]::GetCurrent().Name, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
Set-Acl -LiteralPath $destinationPath -AclObject $acl
$dump = if ($PgBin) { Join-Path $PgBin 'pg_dump.exe' } else { 'pg_dump' }
$dumpPath = Join-Path $destinationPath 'database.dump'
$dumpArguments = @("--host=$Server", "--port=$Port", "--username=$Username", '--format=custom', '--file', $dumpPath, "--dbname=$Database")
if ($PromptForPassword) { $dumpArguments += '--password' } else { $dumpArguments += '--no-password' }
& $dump @dumpArguments
if ($LASTEXITCODE -ne 0) { throw 'pg_dump failed; the backup is incomplete.' }
$permissions = @()
foreach ($root in $roots) {
    $target = Join-Path $destinationPath $root.Name
    $null = New-Item -ItemType Directory -Path $target
    Get-ChildItem -LiteralPath $root.Path -Force | Copy-Item -Destination $target -Recurse -Force
    foreach ($entry in @((Get-Item -LiteralPath $root.Path)) + @(Get-ChildItem -LiteralPath $root.Path -Force -Recurse)) {
        $relative = $root.Name + $entry.FullName.Substring($root.Path.Length).Replace('\', '/')
        $permissions += @{ path = $relative; sddl = (Get-Acl -LiteralPath $entry.FullName).Sddl }
    }
}
$files = @(Get-ChildItem -LiteralPath $destinationPath -File -Force -Recurse | ForEach-Object {
    @{ path = $_.FullName.Substring($destinationPath.Length + 1).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
@{ version = 1; createdAtUtc = [DateTime]::UtcNow.ToString('o'); database = $Database;
    additionalDirectories = @($extraNames.Keys | Sort-Object | ForEach-Object { @{ name = $_; path = "additional/$_" } });
    configurationReferences = $ConfigurationReferences; files = $files; permissions = $permissions;
    protectionNote = 'Windows DPAPI keys require the original Windows user and machine. Configuration values are stored separately.'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destinationPath 'manifest.json') -Encoding UTF8
Write-Output "Backup complete: $($files.Count) files. Configuration values are not included."
