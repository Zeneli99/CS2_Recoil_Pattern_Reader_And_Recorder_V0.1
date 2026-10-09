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
$buildExeName = 'CS2_Recoil_Pattern_Reader_And_Recorder_V0.3.8.exe'
$buildExe = Join-Path $buildOutput $buildExeName
$buildReferences = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll','/r:System.Management.dll','/r:System.Web.Extensions.dll',
    '/r:System.Xml.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
$buildArguments = @('/nologo','/codepage:65001','/target:winexe','/platform:x64','/optimize+',
    ('/out:' + $buildExe)) + $buildReferences + @(
    (Join-Path $buildRoot 'src/Program.cs'), (Join-Path $buildRoot 'src/Layout.cs'),
    (Join-Path $buildRoot 'src/GameIdentity.cs'), (Join-Path $buildRoot 'src/SessionRules.cs'),
    (Join-Path $buildRoot 'src/RecordingIO.cs'),
    (Join-Path $buildRoot 'src/NoFireGenerator.cs'), (Join-Path $buildRoot 'src/ExtractorForm.cs'),
    (Join-Path $buildRoot 'src/RecoilDynamics.cs'), (Join-Path $buildRoot 'src/AmcConverter.cs'),
    (Join-Path $buildRoot 'src/AmcInput.cs'), (Join-Path $buildRoot 'src/RazerXmlExporter.cs'),
    (Join-Path $buildRoot 'src/MacroExport.cs'), (Join-Path $buildRoot 'src/WeaponVDataIdentity.cs'),
    (Join-Path $buildRoot 'src/WeaponVariant.cs'),
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
      (Join-Path $buildRoot 'tests/AmcTimingChecks.cs'), (Join-Path $buildRoot 'tests/LayoutChecks.cs'), (Join-Path $buildRoot 'tests/SessionRulesChecks.cs'),
      (Join-Path $buildRoot 'tests/RazerChecks.cs'), (Join-Path $buildRoot 'tests/ExportSelectionChecks.cs'))
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
Copy-Item -LiteralPath (Join-Path $buildCoreOutput 'UI_RECORDER.png') -Destination $buildOutput -Force

$buildPackage = Join-Path $buildOutput 'CS2_Recoil_Reader_Recorder_V0.3.8_FULL'
[void][System.IO.Directory]::CreateDirectory($buildPackage)
Copy-Item -LiteralPath $buildExe -Destination $buildPackage -Force
foreach ($buildItem in @('Avvia.cmd','LEGGIMI.txt','a2x-LICENSE.txt','README.md','ANALISI_AK47.md')) {
    Copy-Item -LiteralPath (Join-Path $buildRoot $buildItem) -Destination $buildPackage -Force
}
foreach ($buildItem in @('VERIFY.txt','UI_PREVIEW.png','UI_CONVERTER.png','UI_CONVERTER_AMC.png','UI_CONVERTER_NO_FIRE.png','UI_RECORDER.png')) {
    Copy-Item -LiteralPath (Join-Path $buildOutput $buildItem) -Destination $buildPackage -Force
}
$buildExample = Join-Path $buildPackage 'AMC/Esempio_AK47'
[void][System.IO.Directory]::CreateDirectory($buildExample)
foreach ($buildItem in @('AK47_FIXTURE_TEST.amc','AK47_FIXTURE_TEST.report.json')) {
    Copy-Item -LiteralPath (Join-Path $buildCoreOutput $buildItem) -Destination $buildExample -Force
}
$buildUserAmc = Join-Path $buildPackage 'AMC/AK47'
[void][System.IO.Directory]::CreateDirectory($buildUserAmc)
foreach ($buildItem in @('AK47_SENS_1.250_SMOOTH_10MS.amc','AK47_SENS_1.250_SMOOTH_10MS.report.json')) {
    Copy-Item -LiteralPath (Join-Path $buildCoreOutput $buildItem) -Destination $buildUserAmc -Force
    Copy-Item -LiteralPath (Join-Path $buildCoreOutput $buildItem) -Destination $buildOutput -Force
}
$buildNoFire = Join-Path $buildPackage 'AMC/Esempio_NoFire_SINTETICO'
[void][System.IO.Directory]::CreateDirectory($buildNoFire)
foreach ($buildItem in @('AK47_NO_FIRE_SYNTHETIC_TEST.amc','AK47_NO_FIRE_SYNTHETIC_TEST.report.json','AK47_NO_FIRE_SYNTHETIC_TEST.recoil.json')) {
    Copy-Item -LiteralPath (Join-Path $buildCoreOutput $buildItem) -Destination $buildNoFire -Force
}
$buildXml = Join-Path $buildPackage 'XML'
$buildGoldenMaster = Join-Path $buildPackage 'AMC/Golden_M249_TEST'
[void][System.IO.Directory]::CreateDirectory($buildGoldenMaster)
Copy-Item -LiteralPath (Join-Path $buildCoreOutput 'M249_GOLDEN_DERIVED_TEST.amc') -Destination $buildGoldenMaster -Force
[void][System.IO.Directory]::CreateDirectory($buildXml)
foreach ($buildItem in @('AK47_RAZER_FROM_AMC.xml','AK47_RAZER_FROM_AMC.report.json',
    'AK47_NO_FIRE_RAZER.xml','AK47_NO_FIRE_RAZER.report.json','M249_GOLDEN_EXPORT.xml','M249_GOLDEN_EXPORT.report.json')) {
    Copy-Item -LiteralPath (Join-Path $buildCoreOutput $buildItem) -Destination $buildXml -Force
}
$buildSource = Join-Path $buildPackage 'Source'
[void][System.IO.Directory]::CreateDirectory($buildSource)
foreach ($buildItem in @('src','tests','.github')) {
    Copy-Item -LiteralPath (Join-Path $buildRoot $buildItem) -Destination $buildSource -Recurse -Force
}
foreach ($buildItem in @('build.ps1','Avvia.cmd','LEGGIMI.txt','a2x-LICENSE.txt','README.md','ANALISI_AK47.md','.gitignore','.gitattributes')) {
    Copy-Item -LiteralPath (Join-Path $buildRoot $buildItem) -Destination $buildSource -Force
}
$buildCommit = if ([string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) { 'local build' } else { $env:GITHUB_SHA }
$buildInfo = @(
    'CS2 Recoil Pattern Reader And Recorder V0.3.8 EXPERIMENTAL',
    ('Commit: ' + $buildCommit),
    ('UTC: ' + [DateTime]::UtcNow.ToString('o')),
    'Platform: Windows x64, .NET Framework',
    'Target engine build: 14190',
    'Pinned dump: sezzyaep/CS2-OFFSETS fa24ed457f096f519d2cdbb95db55f4b7bc5c6df',
    'Client schema cross-checked against spotted-wtf/CS2-OFFSETS 35cb4039861708501bd2cf72850036575c56cbd6',
    'Sensitivity, pawn, controller and view-angle globals updated; pawn eye-angle field moved to 0x3600',
    'Conflicting dwGameRules globals excluded: exact named cs_gamerules entity and schema m_pGameRules used instead',
    'Rules pointer and entity identity revalidated before session checks; invalid, stale or ambiguous rules rejected',
    'Rules resolver and preserved Valve/session guards tested only in allocated test-process memory',
    'AMC/XML exporters, generation model, weapon variants and timing source identical to V0.3.7',
    'M4A1-S entity/VData name alias accepted only for item definition 60; silenced VData required',
    'Reported M4A1-S name mismatch reproduced in test memory; wrong IDs/names rejected and selected XML extraction tested',
    'M4A1-S mode 1 accepted only for ID 60 and silenced VData; mode 0 behavior preserved',
    'Cycle and four recoil fields select the active index from two-float CFiringModeFloat; raw mode preserved',
    'Native tests cover both firing variants and complete AMC/XML extraction with JSON reimport',
    'Validation failures identify the exact condition and retain actual weapon parameters in the diagnostic',
    'RNG/model and AMC command construction unchanged; reader validation, variant labels and diagnostics updated',
    'M4A1-S mounted/removed silencer state read from pinned m_bSilencerOn; mode and flag must agree',
    'M4A1-S output names/descriptions distinguish both variants; command construction and timing unchanged',
    'New nullable SilencerOn metadata preserves compatibility with previous JSON snapshots',
    'MP5-SD MP7 entity/MP5 VData alias accepted only for ID 23; MP7 ID 33 cannot substitute variant parameters',
    'Native tests cover M4A1-S profiles and reported MP5-SD alias through AMC/XML extraction and JSON reimport',
    'No-fire VData extraction tested only against allocated test-process memory; live CS2 VData: NOT TESTED',
    'No-fire legacy reconstruction and AMC export tested for simulated shot anchors, moderate 10ms output and JSON reimport',
    'AMC command construction/converter unchanged from V0.3.2; existing 1ms/MoveR convention preserved',
    'Exclusive AMC/XML selector controls main F8, converter and recorder; XML-only uses an unchanged temporary AMC master',
    'Selected-format extraction/conversion, XML-only output and master cleanup tested end-to-end without live CS2',
    'Synapse 4 XML: cumulative relative buffers, raw AMC Delay + 1ms per MoveR, release delay preserved, no anti-repeat tail',
    'User M249 calibration verified in game by user: 765 moves, X+25 Y+448, 7155+765=7920ms movement plus 79ms to release',
    'New exporter compared against attached golden XML; original M249 AMC could not be materialized here (403)',
    'Golden-derived AMC fixture reconstructs commands from verified XML, not original AMC bytes',
    'Timing-only correction preserves X/Y and native/model anchors; live CS2 accuracy: NOT TESTED',
    'Current-engine RNG, decay, scale, first-shot latency and resulting trajectory: NOT VERIFIED',
    'AMC execution fitting tested on synthetic quantized traces; real Bloody execution: NOT TESTED',
    'Live weapon/sensitivity detection and recoil compensation: NOT TESTED'
)
$buildInfo | Set-Content -LiteralPath (Join-Path $buildPackage 'BUILD_INFO.txt') -Encoding UTF8
$buildHash = (Get-FileHash -LiteralPath $buildExe -Algorithm SHA256).Hash
($buildHash + '  ' + $buildExeName) | Set-Content -LiteralPath (Join-Path $buildPackage 'SHA256.txt') -Encoding ASCII

Add-Type -AssemblyName System.IO.Compression.FileSystem
$buildZip = Join-Path $buildOutput 'CS2_Recoil_Reader_Recorder_V0.3.8_FULL.zip'
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
