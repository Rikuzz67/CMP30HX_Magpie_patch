[CmdletBinding()]
param(
    [string]$AppDir,
    [ValidateSet('Install','Check','Restore')][string]$Action = 'Install',
    [string]$CoreFile,
    [string]$SnippetFile,
    [switch]$Interactive
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
$CoreOriginal = '91d3742f0df3f2dd85141fa39ea257f7b9243c3e6755cd7fea894c721e7491aa'
$CorePatched = 'cf36298fb8bdcf46e0e5d2f558f9132f9aff86a95db5ce5ab849fbacb4083702'
$KnownNames = @('Magpie.exe','_nvngx.dll','30HXCoreLoader.dll','nvngx_dlssnr.dll')
$BackupName = '.30hx-compat-backup-v1'
$SourceModels = @(
    '6eb209e764f39872625debd6abaf45e2bb6322f6f270f781f70c059ae30b3927',
    'a8a237a967469c455a84654932b513ab656fbaa1e0c44504b1e1bf0890f902b8'
)

function Pick-File([string]$Title,[string]$Filter) {
    if (-not $Interactive) { throw 'A required input was not found. Supply the corresponding path parameter.' }
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = $Title; $dialog.Filter = $Filter
    try {
        if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'Cancelled. No installation requested.' }
        return $dialog.FileName
    } finally { $dialog.Dispose() }
}
function Hash-File([string]$Path) { return [PatchEngine]::FileHash($Path) }
function Reject-Reparse([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        $item = Get-Item -LiteralPath $Path -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing to modify a symbolic link or junction.' }
    }
}
function Test-WritableExe([string]$Path) {
    # Running images cannot be opened for exclusive write. Never kill a process.
    $stream = [IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    $stream.Dispose()
}
function Write-NewBytes([string]$Path,[byte[]]$Bytes) {
    $stream = [IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $stream.Write($Bytes,0,$Bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
}
function Load-State([string]$Backup) {
    Reject-Reparse $Backup
    $statePath = Join-Path $Backup 'state.json'; Reject-Reparse $statePath
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if ($state.schema -ne 1 -or $state.phase -notin @('installed','prepared')) { throw 'Unrecognized backup state.' }
    $seen = @{}
    foreach ($entry in $state.files) {
        if ($entry.name -notin $KnownNames -or $seen.ContainsKey($entry.name)) { throw 'Invalid backup filename.' }
        $seen[$entry.name] = $true
        if ($entry.installedHash -notmatch '^[0-9a-f]{64}$') { throw 'Invalid installed hash.' }
        if ($entry.existed) {
            if ($entry.originalHash -notmatch '^[0-9a-f]{64}$') { throw 'Invalid original hash.' }
            $path = Join-Path $Backup $entry.name; Reject-Reparse $path
            if ((Hash-File $path) -ne $entry.originalHash) { throw 'Backup integrity check failed.' }
        }
    }
    if (-not $seen.ContainsKey('Magpie.exe')) { throw 'Executable backup is missing.' }
    return $state
}
function Find-Core([string]$Target) {
    if ($CoreFile) {
        $candidate = (Resolve-Path -LiteralPath $CoreFile).Path
        if ((Hash-File $candidate) -ne $CoreOriginal) { throw 'Supplied NGX core does not match the supported driver-core SHA256.' }
        return $candidate
    }
    $local = Join-Path $Target '_nvngx.dll'
    if ((Test-Path -LiteralPath $local) -and (Hash-File $local) -eq $CoreOriginal) { return $local }
    $inputCore = Join-Path $PSScriptRoot 'inputs/_nvngx.dll'
    if ((Test-Path -LiteralPath $inputCore) -and (Hash-File $inputCore) -eq $CoreOriginal) { return $inputCore }
    Write-Host 'Looking for a matching installed NVIDIA NGX core (read only)...'
    $store = Join-Path $env:SystemRoot 'System32/DriverStore/FileRepository'
    foreach ($driver in Get-ChildItem -LiteralPath $store -Directory -Filter 'nv*.inf_*' -ErrorAction SilentlyContinue) {
        foreach ($candidate in @((Join-Path $driver.FullName '_nvngx.dll'),(Join-Path $driver.FullName 'amd64/_nvngx.dll'))) {
            if ((Test-Path -LiteralPath $candidate) -and (Hash-File $candidate) -eq $CoreOriginal) { return $candidate }
        }
    }
    Write-Host 'No matching core found. This patch requires the validated 616.92 core hash.'
    $selected = Pick-File 'Choose an original matching _nvngx.dll copy' 'NGX core (_nvngx.dll)|_nvngx.dll'
    if ((Hash-File $selected) -ne $CoreOriginal) { throw 'Unsupported core. Nothing installed. See README for the supported version.' }
    return $selected
}

$exitCode = 0
try {
    Add-Type -Path (Join-Path $PSScriptRoot 'src/PatchEngine.cs')
    if (-not $AppDir) {
        $chosen = Pick-File 'Choose Magpie.exe in the extracted community package' 'Magpie executable (Magpie.exe)|Magpie.exe'
        $AppDir = [IO.Path]::GetDirectoryName($chosen)
    }
    $target = (Resolve-Path -LiteralPath $AppDir).Path
    if (-not (Test-Path -LiteralPath $target -PathType Container)) { throw 'AppDir must be a directory.' }
    Reject-Reparse $target
    foreach ($name in $KnownNames) { Reject-Reparse (Join-Path $target $name) }
    $exePath = Join-Path $target 'Magpie.exe'; $backup = Join-Path $target $BackupName
    $versions = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'versions.json') -Raw | ConvertFrom-Json
    $exeHash = Hash-File $exePath
    $version = @($versions | Where-Object { $_.originalHash -eq $exeHash -or $_.patchedHash -eq $exeHash })
    if ($Action -eq 'Restore') {
        $state = Load-State $backup
        # Check every destination before restoring any file. Preserve later user changes.
        foreach ($entry in $state.files) {
            $path = Join-Path $target $entry.name
            if (Test-Path -LiteralPath $path) {
                $current = Hash-File $path
                if ($current -ne $entry.installedHash -and (-not $entry.existed -or $current -ne $entry.originalHash)) { throw 'A patched file was changed after installation; refusing automatic restore.' }
            } elseif ($state.phase -eq 'installed') { throw 'A patched file is missing; refusing automatic restore.' }
        }
        Test-WritableExe $exePath
        foreach ($entry in $state.files) {
            $path = Join-Path $target $entry.name
            if ($entry.existed) { Copy-Item -LiteralPath (Join-Path $backup $entry.name) -Destination $path -Force }
            elseif (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
        $restoredFolder = Join-Path $target ('.30hx-compat-restored-' + [Guid]::NewGuid().ToString('N'))
        Move-Item -LiteralPath $backup -Destination $restoredFolder
        Write-Host 'RESTORE_OK: original files restored; backup retained; personal settings untouched.' -ForegroundColor Green
    } elseif ($version.Count -ne 1) {
        throw 'Unsupported Magpie executable. Use the exact community release linked in README; no files changed.'
    } elseif ($Action -eq 'Check') {
        Write-Host ('VERSION_OK: ' + $version[0].version)
        if (Test-Path -LiteralPath $backup) {
            $state = Load-State $backup
            if ($state.phase -ne 'installed') { throw 'Installation is incomplete; use Restore.' }
            foreach ($entry in $state.files) {
                if ((Hash-File (Join-Path $target $entry.name)) -ne $entry.installedHash) { throw 'An installed file no longer matches its manifest.' }
            }
            if ((Hash-File (Join-Path $target 'nvngx_dlssnr.dll')) -ne $state.modelHash) { throw 'DLSSNR model changed since installation.' }
            Write-Host 'CHECK_OK: installed files match. GPU/application execution still requires testing.' -ForegroundColor Green
        } else { Write-Host 'CHECK_OK: recognized original executable; compatibility patch not installed.' }
    } else {
        $v = $version[0]
        if ($exeHash -eq $v.patchedHash) { throw 'This executable is already patched. Use Check or Restore.' }
        if (Test-Path -LiteralPath $backup) { throw 'Backup already exists. Restore the prior installation before installing again.' }
        Test-WritableExe $exePath
        $helperPath = Join-Path $PSScriptRoot '30HXCoreLoader.dll'
        if ((Hash-File $helperPath) -ne $v.helperHash) { throw 'Patch package loader integrity mismatch.' }
        $modelPath = if ($SnippetFile) { (Resolve-Path -LiteralPath $SnippetFile).Path } else { Join-Path $target 'nvngx_dlssnr.dll' }
        if (-not (Test-Path -LiteralPath $modelPath) -or (Hash-File $modelPath) -notin $SourceModels) {
            Write-Host 'A supported community DLSSNR DLL is required. Select the RTX20/30/40 community option, not the RTX50 original.'
            Write-Host $v.optionsUrl
            $modelPath = Pick-File 'Choose a supported COMMUNITY nvngx_dlssnr.dll' 'DLSSNR DLL (nvngx_dlssnr.dll)|nvngx_dlssnr.dll'
            if ((Hash-File $modelPath) -notin $SourceModels) { throw 'Unsupported DLSSNR DLL; no files installed.' }
        }
        $coreSource = Find-Core $target
        $originalExe = [IO.File]::ReadAllBytes($exePath)
        $patchedExe = [PatchEngine]::Exe($originalExe,$v.originalHash,[uint32]$v.callRva,[uint32]$v.loadIat,[uint32]$v.procIat)
        if ([PatchEngine]::Hash($patchedExe) -ne $v.patchedHash) { throw 'Patched executable hash mismatch.' }
        $patchedCore = [PatchEngine]::Core([IO.File]::ReadAllBytes($coreSource))
        $helperBytes = [IO.File]::ReadAllBytes($helperPath)
        $modelHash = Hash-File $modelPath
        $modelBytes = $null
        if (-not (Test-Path -LiteralPath (Join-Path $target 'nvngx_dlssnr.dll')) -or (Hash-File (Join-Path $target 'nvngx_dlssnr.dll')) -ne $modelHash) {
            $modelBytes = [IO.File]::ReadAllBytes($modelPath)
            if ([PatchEngine]::Hash($modelBytes) -ne $modelHash) { throw 'DLSSNR source changed while reading.' }
        }
        $changes = @(
            @{name='Magpie.exe';bytes=$patchedExe;hash=$v.patchedHash},
            @{name='_nvngx.dll';bytes=$patchedCore;hash=$CorePatched},
            @{name='30HXCoreLoader.dll';bytes=$helperBytes;hash=$v.helperHash}
        )
        if ($null -ne $modelBytes) { $changes += @{name='nvngx_dlssnr.dll';bytes=$modelBytes;hash=$modelHash} }
        $records = @()
        foreach ($change in $changes) {
            $path = Join-Path $target $change.name; $exists = Test-Path -LiteralPath $path
            $records += @{name=$change.name;existed=[bool]$exists;originalHash=$(if ($exists) { Hash-File $path } else { $null });installedHash=$change.hash}
        }
        # All version/core/model checks finish before any target mutation.
        New-Item -ItemType Directory -Path $backup | Out-Null
        $committed = @()
        try {
            foreach ($entry in $records) {
                if ($entry.existed) {
                    Copy-Item -LiteralPath (Join-Path $target $entry.name) -Destination (Join-Path $backup $entry.name)
                    if ((Hash-File (Join-Path $backup $entry.name)) -ne $entry.originalHash) { throw 'Backup verification failed.' }
                }
            }
            $state = @{schema=1;phase='prepared';version=$v.version;modelHash=$modelHash;files=$records}
            $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backup 'state.json') -Encoding UTF8
            foreach ($change in $changes) {
                Write-NewBytes (Join-Path $backup ($change.name + '.new')) $change.bytes
                if ((Hash-File (Join-Path $backup ($change.name + '.new'))) -ne $change.hash) { throw 'Staged file verification failed.' }
            }
            # Commit the executable last, once its required DLLs are in place.
            foreach ($change in @($changes | Sort-Object { if ($_.name -eq 'Magpie.exe') { 1 } else { 0 } })) {
                $path = Join-Path $target $change.name
                $committed += $change.name
                Copy-Item -LiteralPath (Join-Path $backup ($change.name + '.new')) -Destination $path -Force
                if ((Hash-File $path) -ne $change.hash) { throw 'Committed file verification failed.' }
            }
            $state.phase = 'installed'
            $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backup 'state.json') -Encoding UTF8
            foreach ($change in $changes) { Remove-Item -LiteralPath (Join-Path $backup ($change.name + '.new')) }
        } catch {
            $originalFailure = $_
            $rollbackComplete = $true
            foreach ($name in $committed) {
                $entry = @($records | Where-Object { $_.name -eq $name })[0]
                try {
                    if ($entry.existed) { Copy-Item -LiteralPath (Join-Path $backup $name) -Destination (Join-Path $target $name) -Force }
                    elseif (Test-Path -LiteralPath (Join-Path $target $name)) { Remove-Item -LiteralPath (Join-Path $target $name) }
                } catch { $rollbackComplete = $false }
            }
            if (-not $rollbackComplete) { Write-Host 'Automatic rollback was incomplete. Keep the backup folder and use Restore.' -ForegroundColor Red }
            throw $originalFailure
        }
        Write-Host ('INSTALL_OK: ' + $v.version + ' compatibility patch installed.') -ForegroundColor Green
        Write-Host 'Launch Magpie manually. Add the DLSSNR effect; start with one pass, 25% input dimensions and optical flow off.'
        Write-Host 'No FPS target or personal config was changed. v0.6.11 still needs your application test.'
    }
} catch {
    $exitCode = 1
    Write-Host ('FAILED: ' + $_.Exception.Message) -ForegroundColor Red
}
if ($Interactive) { [void](Read-Host 'Press Enter to close') }
exit $exitCode
