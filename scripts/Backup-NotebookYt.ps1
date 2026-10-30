param(
    [Parameter(Mandatory=$true)][string]$Database,
    [Parameter(Mandatory=$true)][string]$MediaDirectory,
    [Parameter(Mandatory=$true)][string]$KeyDirectory,
    [Parameter(Mandatory=$true)][string]$Destination,
    [Parameter(Mandatory=$true)][switch]$ApplicationStopped,
    [string]$PgBin = '',
    [string]$Server = '127.0.0.1', [int]$Port = 5432, [string]$Username = 'notebookyt',
    [string[]]$ConfigurationReferences = @('ConnectionStrings__Editorial', 'Security__PasswordHash', 'Google__ClientId', 'Google__ClientSecret')
)
$ErrorActionPreference = 'Stop'
if (-not $ApplicationStopped) { throw 'Stop the application and worker before backup.' }
if ($Server -notin @('127.0.0.1', 'localhost', '::1')) { throw 'This script only supports local databases.' }
if (Test-Path -LiteralPath $Destination) { throw 'Destination must not exist.' }
$roots = @((Get-Item -LiteralPath $MediaDirectory), (Get-Item -LiteralPath $KeyDirectory))
foreach ($root in $roots) {
    if (-not $root.PSIsContainer) { throw 'Both storage roots must be directories.' }
    if (($root.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        (Get-ChildItem -LiteralPath $root.FullName -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })) {
        throw 'Linked storage entries are not supported.'
    }
}
$destinationPath = [IO.Path]::GetFullPath($Destination)
foreach ($root in $roots) {
    if ($destinationPath.StartsWith($root.FullName.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Backup must be outside storage roots.' }
}
$null = New-Item -ItemType Directory -Path $destinationPath
# Keep the archive private: it contains protected credentials and Data Protection keys.
$acl = Get-Acl -LiteralPath $destinationPath
$acl.SetAccessRuleProtection($true, $false)
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule([Security.Principal.WindowsIdentity]::GetCurrent().Name, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
Set-Acl -LiteralPath $destinationPath -AclObject $acl
$dump = if ($PgBin) { Join-Path $PgBin 'pg_dump.exe' } else { 'pg_dump' }
$dumpPath = Join-Path $destinationPath 'database.dump'
& $dump --host=$Server --port=$Port --username=$Username --no-password --format=custom --file $dumpPath --dbname=$Database
if ($LASTEXITCODE -ne 0) { throw 'pg_dump failed; the backup is incomplete.' }
$permissions = @()
for ($index = 0; $index -lt 2; $index++) {
    $name = @('media', 'keys')[$index]
    $root = $roots[$index]
    $target = Join-Path $destinationPath $name
    $null = New-Item -ItemType Directory -Path $target
    Get-ChildItem -LiteralPath $root.FullName -Force | Copy-Item -Destination $target -Recurse -Force
    foreach ($entry in @($root) + @(Get-ChildItem -LiteralPath $root.FullName -Force -Recurse)) {
        $relative = $name + $entry.FullName.Substring($root.FullName.Length).Replace('\', '/')
        $permissions += @{ path = $relative; sddl = (Get-Acl -LiteralPath $entry.FullName).Sddl }
    }
}
$files = @(Get-ChildItem -LiteralPath $destinationPath -File -Force -Recurse | ForEach-Object {
    @{ path = $_.FullName.Substring($destinationPath.Length + 1).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
@{ version = 1; createdAtUtc = [DateTime]::UtcNow.ToString('o'); database = $Database;
    configurationReferences = $ConfigurationReferences; files = $files; permissions = $permissions;
    protectionNote = 'Windows DPAPI keys require the original Windows user and machine. Configuration values are stored separately.'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destinationPath 'manifest.json') -Encoding UTF8
Write-Output "Backup complete: $($files.Count) files. Configuration values are not included."
