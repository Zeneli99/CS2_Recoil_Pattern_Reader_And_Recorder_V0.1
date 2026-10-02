param([string]$OutputDirectory = 'dist')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$buildRoot = $PSScriptRoot
$buildCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $buildCompiler)) { throw '.NET Framework x64 compiler missing.' }
$buildOutput = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else { [System.IO.Path]::GetFullPath((Join-Path $buildRoot $OutputDirectory)) }
[void][System.IO.Directory]::CreateDirectory($buildOutput)
$buildExeName = 'CS2_Recoil_Pattern_Reader_And_Recorder_V0.1.exe'
$buildExe = Join-Path $buildOutput $buildExeName
$buildReferences = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll','/r:System.Management.dll','/r:System.Web.Extensions.dll')
$buildArguments = @('/nologo','/codepage:65001','/target:winexe','/platform:x64','/optimize+',
    ('/out:' + $buildExe)) + $buildReferences + @(
    (Join-Path $buildRoot 'src/Program.cs'), (Join-Path $buildRoot 'src/Layout.cs'))
& $buildCompiler @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'Program compilation failed.' }

$buildTest = Join-Path $buildOutput 'SmokeChecks.exe'
$buildTestArguments = @('/nologo','/codepage:65001','/target:exe','/platform:x64',
    ('/out:' + $buildTest)) + $buildReferences + @((Join-Path $buildRoot 'tests/SmokeChecks.cs'))
& $buildCompiler @buildTestArguments
if ($LASTEXITCODE -ne 0) { throw 'Check compilation failed.' }
$buildPreview = Join-Path $buildOutput 'UI_PREVIEW.png'
& $buildTest $buildExe $buildPreview | Tee-Object -FilePath (Join-Path $buildOutput 'VERIFY.txt')
if ($LASTEXITCODE -ne 0) { throw 'Windows checks failed.' }

$buildPackage = Join-Path $buildOutput 'CS2_Recoil_Reader_Recorder_V0.1_FULL'
[void][System.IO.Directory]::CreateDirectory($buildPackage)
Copy-Item -LiteralPath $buildExe -Destination $buildPackage -Force
foreach ($buildItem in @('Avvia.cmd','LEGGIMI.txt','a2x-LICENSE.txt','README.md')) {
    Copy-Item -LiteralPath (Join-Path $buildRoot $buildItem) -Destination $buildPackage -Force
}
foreach ($buildItem in @('VERIFY.txt','UI_PREVIEW.png')) {
    Copy-Item -LiteralPath (Join-Path $buildOutput $buildItem) -Destination $buildPackage -Force
}
$buildSource = Join-Path $buildPackage 'Source'
[void][System.IO.Directory]::CreateDirectory($buildSource)
foreach ($buildItem in @('src','tests','.github')) {
    Copy-Item -LiteralPath (Join-Path $buildRoot $buildItem) -Destination $buildSource -Recurse -Force
}
foreach ($buildItem in @('build.ps1','Avvia.cmd','LEGGIMI.txt','a2x-LICENSE.txt','README.md','.gitignore')) {
    Copy-Item -LiteralPath (Join-Path $buildRoot $buildItem) -Destination $buildSource -Force
}
$buildCommit = if ([string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) { 'local build' } else { $env:GITHUB_SHA }
$buildInfo = @(
    'CS2 Recoil Pattern Reader And Recorder V0.1',
    ('Commit: ' + $buildCommit),
    ('UTC: ' + [DateTime]::UtcNow.ToString('o')),
    'Platform: Windows x64, .NET Framework',
    'Target engine build: 14188',
    'Game capture and recoil reconstruction: NOT TESTED'
)
$buildInfo | Set-Content -LiteralPath (Join-Path $buildPackage 'BUILD_INFO.txt') -Encoding UTF8
$buildHash = (Get-FileHash -LiteralPath $buildExe -Algorithm SHA256).Hash
($buildHash + '  ' + $buildExeName) | Set-Content -LiteralPath (Join-Path $buildPackage 'SHA256.txt') -Encoding ASCII

Add-Type -AssemblyName System.IO.Compression.FileSystem
$buildZip = Join-Path $buildOutput 'CS2_Recoil_Reader_Recorder_V0.1_FULL.zip'
if (Test-Path -LiteralPath $buildZip) { throw 'ZIP already exists; choose a fresh output directory.' }
[System.IO.Compression.ZipFile]::CreateFromDirectory($buildPackage, $buildZip,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host ('EXE: ' + $buildExe)
Write-Host ('FULL ZIP: ' + $buildZip)
Write-Host ('SHA256: ' + $buildHash)
