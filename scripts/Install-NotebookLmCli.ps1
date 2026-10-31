param(
    [string] $Version = '0.8.6'
)

$ErrorActionPreference = 'Stop'
$profileRoot = Join-Path $env:LOCALAPPDATA 'NotebookYt\operator'
$installRoot = Join-Path $profileRoot "tools\notebooklm-py-$Version"
$venv = Join-Path $installRoot 'venv'
$executable = Join-Path $venv 'Scripts\notebooklm.exe'

if ($Version -ne '0.8.6') { throw 'This repository pins the reviewed notebooklm-py version 0.8.6.' }
if (-not (Test-Path -LiteralPath (Join-Path $venv 'Scripts\python.exe'))) {
    $pythonLauncher = Get-Command py -ErrorAction SilentlyContinue
    if ($pythonLauncher) { & $pythonLauncher.Source -3.11 -m venv $venv }
    else {
        $python = Get-Command python -ErrorAction SilentlyContinue
        if (-not $python) { throw 'Python 3.11 is required to install the pinned local CLI.' }
        & $python.Source -m venv $venv
    }
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated notebooklm-py virtual environment.' }
}
$python = Join-Path $venv 'Scripts\python.exe'
& $python -m pip install --disable-pip-version-check 'notebooklm-py[browser,cookies]==0.8.6'
if ($LASTEXITCODE -ne 0) { throw 'Could not install the pinned notebooklm-py dependency.' }
& $python -m playwright install chromium
if ($LASTEXITCODE -ne 0) { throw 'Could not install Chromium for the supported NotebookLM login flow.' }
$versionOutput = & $executable --version 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $versionOutput -notmatch '(?<![0-9])0\.8\.6(?![0-9])') {
    throw 'The installed NotebookLM CLI did not report the reviewed version 0.8.6.'
}
New-Item -ItemType Directory -Force -Path (Join-Path $profileRoot 'notebooklm-cli-profile') | Out-Null
'Pinned notebooklm-py CLI is installed. No account or browser credentials were copied.'
