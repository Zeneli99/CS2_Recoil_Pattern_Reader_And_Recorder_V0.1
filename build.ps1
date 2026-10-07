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
$buildExeName = 'CS2_Recoil_Pattern_Reader_And_Recorder_V0.3.2.exe'
$buildExe = Join-Path $buildOutput $buildExeName
$buildReferences = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll','/r:System.Management.dll','/r:System.Web.Extensions.dll',
    '/r:System.Xml.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
$buildArguments = @('/nologo','/codepage:65001','/target:winexe','/platform:x64','/optimize+',
    ('/out:' + $buildExe)) + $buildReferences + @(
    (Join-Path $buildRoot 'src/Program.cs'), (Join-Path $buildRoot 'src/Layout.cs'),
    (Join-Path $buildRoot 'src/GameIdentity.cs'), (Join-Path $buildRoot 'src/RecordingIO.cs'),
    (Join-Path $buildRoot 'src/NoFireGenerator.cs'), (Join-Path $buildRoot 'src/ExtractorForm.cs'),
    (Join-Path $buildRoot 'src/RecoilDynamics.cs'), (Join-Path $buildRoot 'src/AmcConverter.cs'),
    (Join-Path $buildRoot 'src/AmcInput.cs'),
    (Join-Path $buildRoot 'src/Recorder.cs'), (Join-Path $buildRoot 'src/AmcExecutionCheck.cs'),
    (Join-Path $buildRoot 'src/UserInterface.cs'), (Join-Path $buildRoot 'src/Diagnostics.cs'))
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

$buildCore = Join-Path $buildOutput 'CoreChecks.exe'
$buildCoreArguments = @('/nologo','/codepage:65001','/target:exe','/platform:x64',
    ('/out:' + $buildCore), ('/r:' + $buildExe)) + $buildReferences +
    @((Join-Path $buildRoot 'tests/CoreChecks.cs'), (Join-Path $buildRoot 'tests/NoFireChecks.cs'),
      (Join-Path $buildRoot 'tests/AmcTimingChecks.cs'), (Join-Path $buildRoot 'tests/LayoutChecks.cs'))
& $buildCompiler @buildCoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Conversion check compilation failed.' }
$buildCoreOutput = Join-Path $buildOutput 'ConversionChecks'
& $buildCore (Join-Path $buildRoot 'tests/fixtures') $buildCoreOutput |
    Tee-Object -FilePath (Join-Path $buildOutput 'CONVERSION_VERIFY.txt')
if ($LASTEXITCODE -ne 0) { throw 'Conversion regression checks failed.' }
Get-Content -LiteralPath (Join-Path $buildOutput 'CONVERSION_VERIFY.txt') |
    Add-Content -LiteralPath (Join-Path $buildOutput 'VERIFY.txt') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $buildCoreOutput 'UI_CONVERTER.png') -Destination $buildOutput -Force
Copy-Item -LiteralPath (Join-Path $buildCoreOutput 'UI_CONVERTER_AMC.png') -Destination $buildOutput -Force
Copy-Item -LiteralPath (Join-Path $buildCoreOutput 'UI_CONVERTER_NO_FIRE.png') -Destination $buildOutput -Force

$buildPackage = Join-Path $buildOutput 'CS2_Recoil_Reader_Recorder_V0.3.2_FULL'
[void][System.IO.Directory]::CreateDirectory($buildPackage)
Copy-Item -LiteralPath $buildExe -Destination $buildPackage -Force
foreach ($buildItem in @('Avvia.cmd','LEGGIMI.txt','a2x-LICENSE.txt','README.md','ANALISI_AK47.md')) {
    Copy-Item -LiteralPath (Join-Path $buildRoot $buildItem) -Destination $buildPackage -Force
}
foreach ($buildItem in @('VERIFY.txt','UI_PREVIEW.png','UI_CONVERTER.png','UI_CONVERTER_AMC.png','UI_CONVERTER_NO_FIRE.png')) {
    Copy-Item -LiteralPath (Join-Path $buildOutput $buildItem) -Destination $buildPackage -Force
}
$buildExample = Join-Path $buildPackage 'Esempio_AK47'
[void][System.IO.Directory]::CreateDirectory($buildExample)
foreach ($buildItem in @('AK47_FIXTURE_TEST.amc','AK47_FIXTURE_TEST.report.json')) {
    Copy-Item -LiteralPath (Join-Path $buildCoreOutput $buildItem) -Destination $buildExample -Force
}
$buildUserAmc = Join-Path $buildPackage 'AMC_AK47'
[void][System.IO.Directory]::CreateDirectory($buildUserAmc)
foreach ($buildItem in @('AK47_SENS_1.250_SMOOTH_10MS.amc','AK47_SENS_1.250_SMOOTH_10MS.report.json')) {
    Copy-Item -LiteralPath (Join-Path $buildCoreOutput $buildItem) -Destination $buildUserAmc -Force
    Copy-Item -LiteralPath (Join-Path $buildCoreOutput $buildItem) -Destination $buildOutput -Force
}
$buildNoFire = Join-Path $buildPackage 'Esempio_NoFire_SINTETICO'
[void][System.IO.Directory]::CreateDirectory($buildNoFire)
foreach ($buildItem in @('AK47_NO_FIRE_SYNTHETIC_TEST.amc','AK47_NO_FIRE_SYNTHETIC_TEST.report.json','AK47_NO_FIRE_SYNTHETIC_TEST.recoil.json')) {
    Copy-Item -LiteralPath (Join-Path $buildCoreOutput $buildItem) -Destination $buildNoFire -Force
}
$buildSource = Join-Path $buildPackage 'Source'
[void][System.IO.Directory]::CreateDirectory($buildSource)
foreach ($buildItem in @('src','tests','.github')) {
    Copy-Item -LiteralPath (Join-Path $buildRoot $buildItem) -Destination $buildSource -Recurse -Force
}
foreach ($buildItem in @('build.ps1','Avvia.cmd','LEGGIMI.txt','a2x-LICENSE.txt','README.md','ANALISI_AK47.md','.gitignore')) {
    Copy-Item -LiteralPath (Join-Path $buildRoot $buildItem) -Destination $buildSource -Force
}
$buildCommit = if ([string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) { 'local build' } else { $env:GITHUB_SHA }
$buildInfo = @(
    'CS2 Recoil Pattern Reader And Recorder V0.3.2 EXPERIMENTAL',
    ('Commit: ' + $buildCommit),
    ('UTC: ' + [DateTime]::UtcNow.ToString('o')),
    'Platform: Windows x64, .NET Framework',
    'Target engine build: 14189',
    'No-fire VData extraction tested only against allocated test-process memory; live CS2 VData: NOT TESTED',
    'No-fire legacy reconstruction and AMC export tested for simulated shot anchors, moderate 10ms output and JSON reimport',
    'AMC exporter uses a declared 1ms/MoveR convention matching the working Fusion capture; physical command timing: NOT MEASURED',
    'Timing-only correction preserves X/Y and native/model anchors; live CS2 accuracy: NOT TESTED',
    'Current-engine RNG, decay, scale, first-shot latency and resulting trajectory: NOT VERIFIED',
    'AMC execution fitting tested on synthetic quantized traces; real Bloody execution: NOT TESTED',
    'Live weapon/sensitivity detection and recoil compensation: NOT TESTED'
)
$buildInfo | Set-Content -LiteralPath (Join-Path $buildPackage 'BUILD_INFO.txt') -Encoding UTF8
$buildHash = (Get-FileHash -LiteralPath $buildExe -Algorithm SHA256).Hash
($buildHash + '  ' + $buildExeName) | Set-Content -LiteralPath (Join-Path $buildPackage 'SHA256.txt') -Encoding ASCII

Add-Type -AssemblyName System.IO.Compression.FileSystem
$buildZip = Join-Path $buildOutput 'CS2_Recoil_Reader_Recorder_V0.3.2_FULL.zip'
if (Test-Path -LiteralPath $buildZip) { throw 'ZIP already exists; choose a fresh output directory.' }
Add-Type -AssemblyName System.IO.Compression
$buildStream = [System.IO.File]::Open($buildZip, [System.IO.FileMode]::CreateNew)
$buildArchive = [System.IO.Compression.ZipArchive]::new($buildStream,
    [System.IO.Compression.ZipArchiveMode]::Create, $false)
try {
    foreach ($buildFile in (Get-ChildItem -LiteralPath $buildPackage -File -Recurse -Force | Sort-Object FullName)) {
        $buildRelative = $buildFile.FullName.Substring($buildPackage.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($buildArchive,
            $buildFile.FullName, $buildRelative, [System.IO.Compression.CompressionLevel]::Optimal)
    }
} finally {
    $buildArchive.Dispose()
    $buildStream.Dispose()
}
$buildZipCheck = [System.IO.Compression.ZipFile]::OpenRead($buildZip)
try {
    foreach ($buildEntry in $buildZipCheck.Entries) {
        if ($buildEntry.FullName.Contains('\')) { throw 'Nonstandard ZIP path separator.' }
    }
    if ($null -eq $buildZipCheck.GetEntry('Source/.github/workflows/build-windows.yml') -or
        $null -eq $buildZipCheck.GetEntry('Source/src/Program.cs')) {
        throw 'Source files missing from ZIP.'
    }
} finally { $buildZipCheck.Dispose() }
Write-Host ('EXE: ' + $buildExe)
Write-Host ('FULL ZIP: ' + $buildZip)
Write-Host ('SHA256: ' + $buildHash)
