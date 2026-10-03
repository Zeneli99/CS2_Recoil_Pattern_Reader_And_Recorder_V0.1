@echo off
cd /d "%~dp0"
if not exist "CS2_Recoil_Pattern_Reader_And_Recorder_V0.2.2.exe" (
  echo EXE non trovato. Estrai tutto il pacchetto completo prima di avviare.
  pause
  exit /b 1
)
start "" "%~dp0CS2_Recoil_Pattern_Reader_And_Recorder_V0.2.2.exe"
