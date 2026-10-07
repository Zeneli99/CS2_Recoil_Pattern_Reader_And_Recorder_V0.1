using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using RecoilProbe;

internal static class NoFireChecks {
 private static WeaponSnapshot Example() {
  return new WeaponSnapshot {Weapon="AK47",Sensitivity=1.25F,Build=Layout.TargetBuild,SchemaCommit=Layout.SourceCommit,
   NativeParametersRead=false,Native=new WeaponParameters {DesignerName="weapon_ak47",ItemDefinitionIndex=7,
    MaxClip=30,BulletsPerShot=1,FullAuto=true,RecoilSeed=223,Mode=0,CycleSeconds=0.1F,
    RecoilAngle=0,AngleVariance=70,RecoilMagnitude=30,MagnitudeVariance=0}};
 }
 internal static void Run(string output,RecordingData fixture,Action<bool,string> check,Action<Action,string> reject) {
  LegacyRecoilRandom random=new LegacyRecoilRandom(1);
  int[] golden={893351816,197493099,1624379149,1137522503,1998097157};bool matches=true;
  foreach(int value in golden)matches&=random.NextInteger()==value;
  check(matches,"Legacy RNG matches the independent Park-Miller/shuffle seed-1 vector");
  WeaponSnapshot input=Example();List<ReconstructedShot> shots=NoFireGenerator.Reconstruct(input);
  check(shots.Count==30&&shots[0].Shot==1&&shots[0].TimeMs==0&&shots[0].Pitch==0&&shots[0].Yaw==0,
   "No-fire reconstruction starts a full 30-shot magazine from reset, without input samples");
  check(Math.Abs(shots[0].ImpulseMagnitude-22.5)<1e-6,
   "First AK impulse applies the explicitly assumed 0.75 suppression");
  RecoilPoint first=AmcConverter.Points(fixture)[0];
  check(Math.Abs(shots[0].PitchVelocity-first.PitchVelocity)<0.00005&&
   Math.Abs(shots[0].YawVelocity-first.YawVelocity)<0.00005,
   "Seed-223 first impulse agrees with the provided AK recording (not a current-engine proof)");
  List<ReconstructedShot> again=NoFireGenerator.Reconstruct(input);bool repeat=true;
  for(int i=0;i<shots.Count;i++)repeat&=shots[i].Pitch==again[i].Pitch&&shots[i].Yaw==again[i].Yaw;
  check(repeat,"Identical native inputs and explicit assumptions reproduce the same trajectory");
  string amcPath=Path.Combine(output,"AK47_NO_FIRE_SYNTHETIC_TEST.amc");
  string saved=WeaponSnapshotIO.Save(input,amcPath);
  WeaponSnapshot loaded;
  check(WeaponSnapshotIO.TryLoad(saved,out loaded)&&loaded.Native.RecoilSeed==223&&loaded.Sensitivity==1.25F,
   "No-fire JSON roundtrip preserves native inputs separately from model assumptions");
  check(!loaded.NativeParametersRead&&!loaded.CurrentEngineAlgorithmVerified,
   "Synthetic inputs do not claim live extraction or current engine verification");
  AmcResult result=NoFireGenerator.Convert(loaded,amcPath,1.25);
  AmcInput amc=AmcInput.Load(result.AmcPath);
  check(result.MoveRCommandCostMs==1&&amc.MoveRCommandCostMs==1&&!result.CommandTimingMeasured,
   "No-fire output declares the Fusion 1ms command convention without claiming a hardware measurement");
  check(result.SourceKind=="CS2_VDATA_NO_FIRE_EXPERIMENTAL"&&result.RecoilSequenceReconstructed&&
   !result.DeterministicRecoilStateDirectlyRead&&!result.BallisticTrajectoryDirectlyRead&&
   !result.CurrentEngineAlgorithmVerified&&!result.InstantaneousRecoilVerified,
   "Report never mislabels a simulated no-fire trajectory as direct native extraction");
  check(result.ActiveDurationMs==2999&&amc.ReleaseTime==2999&&amc.TailMs==30000,
   "No-fire macro holds left click for the modeled magazine and has a 30-second tail");
  bool anchors=true,timing=true;int previous=-1;
  foreach(MouseAnchor point in amc.Anchors) {
   if(previous>=0)timing&=point.Time-previous>=10;previous=point.Time;
  }
  foreach(ReconstructedShot shot in shots) {
   int x=0,y=0;int at=AmcConverter.Round(shot.TimeMs);
   foreach(MouseAnchor point in amc.Anchors)if(point.Time<=at){x=point.X;y=point.Y;}
   anchors&=x==AmcConverter.Round(shot.Yaw*2/(1.25*0.022))&&
    y==AmcConverter.Round(-shot.Pitch*2/(1.25*0.022));
  }
  check(timing&&amc.Anchors.Count>30&&amc.Anchors.Count<400,
   "No-fire AK uses moderate >=10ms motion rather than 1ms smoothing");
  check(anchors,"Every simulated shot anchor survives cumulative rounding and AMC export");
  reject(delegate{NoFireGenerator.Convert(input,amcPath,1.25);},"No-fire converter cannot overwrite an existing AMC/report");
  reject(delegate{WeaponSnapshotIO.Save(input,amcPath);},"Extracted JSON cannot overwrite an existing file");
  AmcResult half=NoFireGenerator.Convert(input,Path.Combine(output,"AK47_NO_FIRE_SENS_2.500.amc"),2.5);
  check(Math.Abs(half.TotalX*2-result.TotalX)<=1&&Math.Abs(half.TotalY*2-result.TotalY)<=1&&
   half.ActiveDurationMs==result.ActiveDurationMs,"Sensitivity rescales no-fire movements without changing shot timing");
  input.CurrentEngineAlgorithmVerified=true;WeaponDataReader.Validate(input);
  check(!input.CurrentEngineAlgorithmVerified,"Imported verification claims are reset; simulation remains experimental");
  WeaponSnapshot bad=Example();bad.Native.CycleSeconds=float.NaN;
  reject(delegate{NoFireGenerator.Reconstruct(bad);},"Non-finite cycle is rejected");
  bad=Example();bad.Native.RecoilMagnitude=-1;
  reject(delegate{NoFireGenerator.Reconstruct(bad);},"Invalid recoil magnitude is rejected");
  bad=Example();bad.Native.Mode=1;
  reject(delegate{NoFireGenerator.Reconstruct(bad);},"Unverified alternate mode is rejected");
  bad=Example();bad.Native.FullAuto=false;
  reject(delegate{NoFireGenerator.Reconstruct(bad);},"Semi-auto cannot be silently generated as an automatic spray");
  bad=Example();bad.Native.MaxClip=201;
  reject(delegate{NoFireGenerator.Reconstruct(bad);},"Implausible magazine is rejected");
  bad=Example();bad.Native.RecoilSeed=-1;
  reject(delegate{NoFireGenerator.Reconstruct(bad);},"Unsupported negative seed is rejected");
  bad=Example();bad.Build=0;
  reject(delegate{NoFireGenerator.Reconstruct(bad);},"Different build cannot use the current layout");
  bad=Example();bad.Model.MouseYaw=0;
  reject(delegate{NoFireGenerator.Reconstruct(bad);},"Zero yaw scale is rejected before division");
  bad=Example();bad.Model.Variance=double.PositiveInfinity;
  reject(delegate{NoFireGenerator.Reconstruct(bad);},"Infinite model parameters are rejected");
  bad=Example();bad.Model.IntegrationStepSeconds=1.0/1000;
  reject(delegate{NoFireGenerator.Reconstruct(bad);},"Unexpected model timestep is rejected");
  NativeReadChecks(output,check,reject);
 }
 private static void WriteFloat(IntPtr address,int offset,float value) {
  Marshal.Copy(BitConverter.GetBytes(value),0,IntPtr.Add(address,offset),4);
 }
 private static void NativeReadChecks(string output,Action<bool,string> check,Action<Action,string> reject) {
  IntPtr weapon=Marshal.AllocHGlobal(0x2000),vdata=Marshal.AllocHGlobal(0x900),name=Marshal.StringToHGlobalAnsi("weapon_ak47"),
   silenced=Marshal.StringToHGlobalAnsi("weapon_m4a1_silencer"),mp5Name=Marshal.StringToHGlobalAnsi("weapon_mp5sd");
  try {
   Marshal.Copy(new byte[0x2000],0,weapon,0x2000);Marshal.Copy(new byte[0x900],0,vdata,0x900);
   Marshal.WriteIntPtr(weapon,Layout.WeaponVData,vdata);Marshal.WriteIntPtr(vdata,Layout.VDataName,name);
   Marshal.WriteByte(vdata,Layout.VDataFullAuto,1);Marshal.WriteInt32(vdata,Layout.VDataMaxClip,30);
   Marshal.WriteInt32(vdata,Layout.VDataBullets,1);Marshal.WriteInt32(vdata,Layout.VDataRecoilSeed,223);
   WriteFloat(vdata,Layout.VDataCycle,0.1F);WriteFloat(vdata,Layout.VDataRecoilAngleVariance,70);
   WriteFloat(vdata,Layout.VDataRecoilMagnitude,30);
   int pid;using(Process process=Process.GetCurrentProcess())pid=process.Id;
   using(ReadMemory memory=new ReadMemory(pid)) {
    WeaponParameters read=WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_ak47",7);
    check(read.RecoilSeed==223&&read.MaxClip==30&&read.FullAuto&&read.RecoilMagnitude==30&&read.CycleSeconds==0.1F,
     "Read-only native interop extracts VData from allocated test-process memory without firing");
    reject(delegate{WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_m4a1",7);},
     "VData with a name different from the active weapon is rejected");
    Marshal.WriteIntPtr(vdata,Layout.VDataName,silenced);Marshal.WriteInt32(vdata,Layout.VDataMaxClip,20);
    Marshal.WriteInt32(vdata,Layout.VDataRecoilSeed,555);
    WeaponParameters m4s=WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_m4a1",60);
    check(m4s.ItemDefinitionIndex==60&&m4s.DesignerName=="weapon_m4a1_silencer"&&m4s.MaxClip==20&&m4s.RecoilSeed==555,
     "Reported M4A1-S mismatch is accepted only for item ID 60 and reads silenced VData parameters");
    check(WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_m4a1_silencer",60).DesignerName=="weapon_m4a1_silencer",
     "M4A1-S canonical entity and VData names also remain valid");
    WeaponSnapshot m4Snapshot=new WeaponSnapshot {Weapon=WeaponCatalog.Name(m4s.ItemDefinitionIndex),Sensitivity=1.25F,
     Build=Layout.TargetBuild,SchemaCommit=Layout.SourceCommit,NativeParametersRead=false,Native=m4s};
    string savedParameters;
    MacroExportResult m4Xml=MainForm.ExportSnapshot(m4Snapshot,Path.Combine(output,"M4A1_S_NATIVE_TEST.amc"),MacroFormat.XmlRazer,out savedParameters);
    WeaponSnapshot m4Roundtrip;
    check(File.Exists(m4Xml.OutputPath)&&m4Xml.Xml.MoveCommands>0&&WeaponSnapshotIO.TryLoad(savedParameters,out m4Roundtrip)&&
     m4Roundtrip.Weapon=="M4A1_S"&&m4Roundtrip.Native.ItemDefinitionIndex==60&&m4Roundtrip.Native.DesignerName=="weapon_m4a1_silencer",
     "M4A1-S parameters read through the reported alias proceed through selected XML extraction and JSON reimport");
    // Distinct synthetic values catch wrong indexing independently for all five CFiringModeFloat fields.
    WriteFloat(vdata,Layout.VDataCycle+4,0.12F);WriteFloat(vdata,Layout.VDataRecoilAngle+4,-10);
    WriteFloat(vdata,Layout.VDataRecoilAngleVariance+4,55);WriteFloat(vdata,Layout.VDataRecoilMagnitude+4,21);
    WriteFloat(vdata,Layout.VDataRecoilMagnitudeVariance+4,2);Marshal.WriteInt32(weapon,Layout.WeaponMode,1);
    Marshal.WriteByte(weapon,Layout.WeaponSilencerOn,1);
    WeaponParameters alternate=WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_m4a1",60);
    check(alternate.Mode==1&&alternate.CycleSeconds==0.12F&&alternate.RecoilAngle==-10&&alternate.AngleVariance==55&&
     alternate.RecoilMagnitude==21&&alternate.MagnitudeVariance==2&&alternate.MaxClip==20&&alternate.RecoilSeed==555,
     "M4A1-S mode 1 selects the second float of every cycle/recoil field and preserves the raw mode");
    Marshal.WriteInt32(weapon,Layout.WeaponMode,0);
    Marshal.WriteByte(weapon,Layout.WeaponSilencerOn,0);
    WeaponParameters primary=WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_m4a1",60);
    check(primary.Mode==0&&primary.CycleSeconds==0.1F&&primary.RecoilAngle==0&&primary.AngleVariance==70&&
     primary.RecoilMagnitude==30&&primary.MagnitudeVariance==0,
     "M4A1-S mode 0 still reads all original primary values without mixing the two variants");
    foreach(int invalid in new int[] {-1,2,Int32.MaxValue}) {
     Marshal.WriteInt32(weapon,Layout.WeaponMode,invalid);
     reject(delegate{WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_m4a1",60);},
      "Out-of-range firing mode "+invalid+" is rejected before indexing VData floats");
    }
    // Reference values from game assets at SteamTracking/GameTracking-CS2 ac1278dbbff39b7fe5030fba42a010e455c011f6:
    // game/csgo/pak01_dir/scripts/weapons.vdata, weapon_m4a1_silencer. This is allocated test memory, not live CS2.
    Marshal.WriteInt32(weapon,Layout.WeaponMode,1);Marshal.WriteInt32(vdata,Layout.VDataRecoilSeed,38965);
    Marshal.WriteByte(weapon,Layout.WeaponSilencerOn,1);
    WriteFloat(vdata,Layout.VDataCycle,0.1F);WriteFloat(vdata,Layout.VDataCycle+4,0.1F);
    WriteFloat(vdata,Layout.VDataRecoilAngle,0);WriteFloat(vdata,Layout.VDataRecoilAngle+4,0);
    WriteFloat(vdata,Layout.VDataRecoilAngleVariance,65);WriteFloat(vdata,Layout.VDataRecoilAngleVariance+4,65);
    WriteFloat(vdata,Layout.VDataRecoilMagnitude,25);WriteFloat(vdata,Layout.VDataRecoilMagnitude+4,21);
    WriteFloat(vdata,Layout.VDataRecoilMagnitudeVariance,3);WriteFloat(vdata,Layout.VDataRecoilMagnitudeVariance+4,0);
    WeaponParameters silencedRead=WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_m4a1",60);
    WeaponSnapshot silencedSnapshot=new WeaponSnapshot {Weapon="M4A1_S",Sensitivity=1.25F,Build=Layout.TargetBuild,
     SchemaCommit=Layout.SourceCommit,NativeParametersRead=false,Native=silencedRead};
    WeaponDataReader.Validate(silencedSnapshot);
    check(silencedRead.Mode==1&&silencedRead.FullAuto&&silencedRead.BulletsPerShot==1&&silencedRead.RecoilMagnitude==21&&
     silencedRead.MagnitudeVariance==0&&silencedRead.RecoilSeed==38965,
     "M4A1-S mode 1 with pinned game-asset values passes the full validation that blocked V0.3.5");
    MacroExportResult silencedAmc=MainForm.ExportSnapshot(silencedSnapshot,Path.Combine(output,"M4A1_S_MODE1_AMC_TEST.amc"),
     MacroFormat.Amc,out savedParameters);
    AmcInput silencedMaster=AmcInput.Load(silencedAmc.OutputPath);
    check(WeaponSnapshotIO.TryLoad(savedParameters,out m4Roundtrip)&&m4Roundtrip.Native.Mode==1&&
     m4Roundtrip.Native.RecoilMagnitude==21&&silencedAmc.Amc.Shots==20&&silencedMaster.ReleaseTime==1999,
     "M4A1-S mode 1 completes AMC extraction and JSON reimport without normalizing the mode or substituting primary recoil");
    MacroExportResult silencedXml=MainForm.ExportSnapshot(silencedSnapshot,Path.Combine(output,"M4A1_S_MODE1_XML_TEST.amc"),
     MacroFormat.XmlRazer,out savedParameters);
    check(WeaponSnapshotIO.TryLoad(savedParameters,out m4Roundtrip)&&m4Roundtrip.Native.Mode==1&&
     silencedXml.Xml.MoveCommands==silencedAmc.Amc.MoveCommands&&silencedXml.Xml.TotalX==silencedAmc.Amc.TotalX&&
     silencedXml.Xml.TotalY==silencedAmc.Amc.TotalY&&silencedXml.Xml.ReleaseTimeMs==silencedMaster.ReleaseTime&&
     silencedXml.Xml.MoveRCommandCostMs==1&&!silencedXml.Xml.CoordinatesScaled,
     "M4A1-S mode 1 completes XML-only extraction with the same movement totals, release time and existing MoveR calibration");
    Marshal.WriteInt32(weapon,Layout.WeaponMode,0);Marshal.WriteByte(weapon,Layout.WeaponSilencerOn,0);
    WeaponParameters unsilencedRead=WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_m4a1",60);
    WeaponSnapshot unsilencedSnapshot=new WeaponSnapshot {Weapon="M4A1_S",Sensitivity=1.25F,Build=Layout.TargetBuild,
     SchemaCommit=Layout.SourceCommit,NativeParametersRead=false,Native=unsilencedRead};
    WeaponDataReader.Validate(unsilencedSnapshot);
    check(unsilencedRead.Mode==0&&unsilencedRead.SilencerOn==false&&unsilencedRead.RecoilMagnitude==25&&
     unsilencedRead.MagnitudeVariance==3&&silencedRead.SilencerOn==true&&silencedRead.RecoilMagnitude==21&&
     silencedRead.MagnitudeVariance==0,
     "Unsilenced M4A1-S reads primary recoil 25/3 while the silenced variant keeps secondary recoil 21/0");
    List<ReconstructedShot> unsilencedShots=NoFireGenerator.Reconstruct(unsilencedSnapshot);
    List<ReconstructedShot> silencedShots=NoFireGenerator.Reconstruct(silencedSnapshot);
    check(unsilencedShots[0].ImpulseMagnitude>=16.5&&unsilencedShots[0].ImpulseMagnitude<=21&&
     Math.Abs(silencedShots[0].ImpulseMagnitude-15.75)<1e-6&&
     (unsilencedShots[19].Pitch!=silencedShots[19].Pitch||unsilencedShots[19].Yaw!=silencedShots[19].Yaw),
     "M4A1-S firing variants reconstruct distinct trajectories from their own native magnitudes and variances");
    string offName=NoFireGenerator.SuggestedFileName(unsilencedSnapshot,1.25);
    string onName=NoFireGenerator.SuggestedFileName(silencedSnapshot,1.25);
    check(offName.StartsWith("M4A1_S_SENZA_SILENZIATORE_")&&onName.StartsWith("M4A1_S_CON_SILENZIATORE_")&&
     WeaponVariant.DisplayName(unsilencedSnapshot)=="M4A1-S senza silenziatore"&&
     WeaponVariant.DisplayName(silencedSnapshot)=="M4A1-S con silenziatore",
     "File suggestions and snapshot UI distinguish both M4A1-S profiles without changing the canonical weapon ID");
    string offSelection=Path.Combine(output,offName);
    MacroExportResult offAmc=MainForm.ExportSnapshot(unsilencedSnapshot,offSelection,MacroFormat.Amc,out savedParameters);
    AmcInput offMaster=AmcInput.Load(offAmc.OutputPath);
    check(WeaponSnapshotIO.TryLoad(savedParameters,out m4Roundtrip)&&m4Roundtrip.Weapon=="M4A1_S"&&
     m4Roundtrip.Native.Mode==0&&m4Roundtrip.Native.SilencerOn==false&&m4Roundtrip.Native.RecoilMagnitude==25&&
     offMaster.WeaponName=="M4A1_S_SENZA_SILENZIATORE"&&offMaster.ReleaseTime==1999,
     "Unsilenced M4A1-S completes AMC extraction and JSON reimport with its own profile header and primary recoil");
    MacroExportResult offXml=MainForm.ExportSnapshot(unsilencedSnapshot,offSelection,MacroFormat.XmlRazer,out savedParameters);
    check(WeaponSnapshotIO.TryLoad(savedParameters,out m4Roundtrip)&&m4Roundtrip.Native.SilencerOn==false&&
     Path.GetFileName(offXml.OutputPath).StartsWith("M4A1_S_SENZA_SILENZIATORE_")&&
     offXml.Xml.MoveCommands==offAmc.Amc.MoveCommands&&offXml.Xml.TotalX==offAmc.Amc.TotalX&&
     offXml.Xml.TotalY==offAmc.Amc.TotalY&&offXml.Xml.ReleaseTimeMs==offMaster.ReleaseTime&&
     offXml.Xml.MoveRCommandCostMs==1&&!offXml.Xml.CoordinatesScaled,
     "Unsilenced M4A1-S completes XML extraction with its own file name and unchanged AMC movement/timing calibration");
    check(silencedMaster.WeaponName=="M4A1_S_CON_SILENZIATORE"&&
     (offAmc.Amc.TotalX!=silencedAmc.Amc.TotalX||offAmc.Amc.TotalY!=silencedAmc.Amc.TotalY),
     "Both M4A1-S AMC descriptions identify their firing variant and retain different generated movement totals");
    unsilencedRead.SilencerOn=true;
    reject(delegate{WeaponDataReader.Validate(unsilencedSnapshot);},"Conflicting M4A1-S silencer flag and mode are rejected during transition");
    unsilencedRead.SilencerOn=null;
    string legacy=Path.Combine(output,"M4A1_S_MODE0_LEGACY.recoil.json");
    Dictionary<string,object> legacyObject=RecordingIO.Serializer().Deserialize<Dictionary<string,object>>(
     RecordingIO.Serializer().Serialize(unsilencedSnapshot));
    ((Dictionary<string,object>)legacyObject["Native"]).Remove("SilencerOn");
    File.WriteAllText(legacy,RecordingIO.Serializer().Serialize(legacyObject));
    check(WeaponSnapshotIO.TryLoad(legacy,out m4Roundtrip)&&m4Roundtrip.Native.Mode==0&&
     !m4Roundtrip.Native.SilencerOn.HasValue&&WeaponVariant.ExportName(m4Roundtrip)=="M4A1_S_SENZA_SILENZIATORE",
     "Previous M4A1-S JSON snapshots without a silencer flag remain importable using their recorded firing mode");
    Marshal.WriteByte(weapon,Layout.WeaponSilencerOn,2);
    reject(delegate{WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_m4a1",60);},
     "Extraction rejects an invalid silencer boolean rather than assuming mounted or removed");
    Marshal.WriteInt32(weapon,Layout.WeaponMode,1);Marshal.WriteByte(weapon,Layout.WeaponSilencerOn,1);
    silencedRead.FullAuto=false;
    reject(delegate{WeaponDataReader.Validate(silencedSnapshot);},"M4A1-S mode 1 cannot bypass a false full-auto flag");
    silencedRead.FullAuto=true;silencedRead.BulletsPerShot=2;
    reject(delegate{WeaponDataReader.Validate(silencedSnapshot);},"M4A1-S mode 1 cannot bypass an unsupported bullet count");
    InvalidOperationException parameterFailure=null;
    try{WeaponDataReader.Validate(silencedSnapshot);}catch(InvalidOperationException ex){parameterFailure=ex;}
    check(parameterFailure!=null&&parameterFailure.Message.Contains("proiettili per colpo=2"),
     "Validation identifies the exact failing parameter instead of the old combined full-auto/mode error");
    Diagnostics.ReportDirectory=output;Diagnostics.Record(parameterFailure,"M4A1-S TEST MEMORY ONLY",memory.PointerTrace);
    string parameterDiagnostic=File.ReadAllText(Diagnostics.LastPath);
    check(parameterDiagnostic.Contains("PARAMETRI ARMA LETTI")&&parameterDiagnostic.Contains("ID arma: 60")&&
     parameterDiagnostic.Contains("Full-auto VData: true")&&parameterDiagnostic.Contains("Proiettili per colpo: 2")&&
     parameterDiagnostic.Contains("Modalita' m_weaponMode: 1")&&parameterDiagnostic.Contains("Magnitudine recoil: 21"),
     "Diagnostic preserves the actual ID, full-auto flag, bullet count, firing mode and selected recoil values");
    Diagnostics.Record(parameterFailure);
    check(File.ReadAllText(Diagnostics.LastPath)==parameterDiagnostic,
     "Main UI catch preserves the detailed weapon-parameter diagnostic");
    silencedRead.BulletsPerShot=1;silencedRead.Mode=2;
    reject(delegate{WeaponDataReader.Validate(silencedSnapshot);},"Imported M4A1-S mode 2 remains unsupported");
    silencedRead.Mode=1;silencedRead.ItemDefinitionIndex=16;silencedSnapshot.Weapon="M4A4";
    reject(delegate{WeaponDataReader.Validate(silencedSnapshot);},"M4A4 cannot inherit the M4A1-S mode 1 exception");
    silencedRead.ItemDefinitionIndex=60;silencedSnapshot.Weapon="M4A1_S";silencedRead.DesignerName="weapon_m4a1";
    reject(delegate{WeaponDataReader.Validate(silencedSnapshot);},"Mode 1 requires M4A1-S silenced VData even for imported snapshots");
    Marshal.WriteInt32(weapon,Layout.WeaponMode,0);
    reject(delegate{WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_m4a1",16);},
     "M4A4 ID 16 cannot accept M4A1-S VData through the entity-name alias");
    reject(delegate{WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_m4a1_silencer",16);},
     "Matching silenced names with the wrong item ID are rejected");
    reject(delegate{WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_ak47",60);},
     "M4A1-S ID alone cannot authorize an unrelated entity name");
    Marshal.WriteIntPtr(vdata,Layout.VDataName,name);
    reject(delegate{WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_m4a1",60);},
     "M4A1-S ID and base entity cannot accept unrelated AK VData");
    check(WeaponVDataIdentity.Matches(16,"weapon_m4a1","weapon_m4a1")&&
     !WeaponVDataIdentity.Matches(60,"weapon_m4a1","weapon_m4a1"),
     "M4A4 identity remains exact while M4A1-S requires its own silenced VData");
    // Reproduce the MP5-SD report and use pinned game-asset values, not MP7 parameters.
    Marshal.WriteIntPtr(vdata,Layout.VDataName,mp5Name);Marshal.WriteInt32(weapon,Layout.WeaponMode,0);
    Marshal.WriteInt32(vdata,Layout.VDataMaxClip,30);Marshal.WriteInt32(vdata,Layout.VDataRecoilSeed,61649);
    WriteFloat(vdata,Layout.VDataCycle,0.08F);WriteFloat(vdata,Layout.VDataRecoilAngle,0);
    WriteFloat(vdata,Layout.VDataRecoilAngleVariance,70);WriteFloat(vdata,Layout.VDataRecoilMagnitude,16);
    WriteFloat(vdata,Layout.VDataRecoilMagnitudeVariance,1);
    WeaponParameters mp5Read=WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_mp7",23);
    check(mp5Read.ItemDefinitionIndex==23&&mp5Read.DesignerName=="weapon_mp5sd"&&mp5Read.RecoilSeed==61649&&
     mp5Read.RecoilMagnitude==16&&mp5Read.MagnitudeVariance==1&&mp5Read.CycleSeconds==0.08F,
     "Reported MP5-SD entity MP7/VData MP5 mismatch is accepted for ID 23 and reads MP5-specific parameters");
    check(WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_mp5sd",23).DesignerName=="weapon_mp5sd",
     "MP5-SD canonical entity and VData names remain valid");
    WeaponSnapshot mp5Snapshot=new WeaponSnapshot {Weapon="MP5_SD",Sensitivity=1.25F,Build=Layout.TargetBuild,
     SchemaCommit=Layout.SourceCommit,NativeParametersRead=false,Native=mp5Read};
    MacroExportResult mp5Amc=MainForm.ExportSnapshot(mp5Snapshot,Path.Combine(output,"MP5_SD_NATIVE_AMC_TEST.amc"),
     MacroFormat.Amc,out savedParameters);
    AmcInput mp5Master=AmcInput.Load(mp5Amc.OutputPath);
    check(WeaponSnapshotIO.TryLoad(savedParameters,out m4Roundtrip)&&m4Roundtrip.Native.ItemDefinitionIndex==23&&
     m4Roundtrip.Native.DesignerName=="weapon_mp5sd"&&mp5Amc.Amc.Shots==30&&mp5Master.ReleaseTime==2399,
     "MP5-SD parameters through the reported alias complete AMC extraction and JSON reimport");
    MacroExportResult mp5Xml=MainForm.ExportSnapshot(mp5Snapshot,Path.Combine(output,"MP5_SD_NATIVE_XML_TEST.amc"),
     MacroFormat.XmlRazer,out savedParameters);
    check(WeaponSnapshotIO.TryLoad(savedParameters,out m4Roundtrip)&&m4Roundtrip.Native.RecoilSeed==61649&&
     mp5Xml.Xml.MoveCommands==mp5Amc.Amc.MoveCommands&&mp5Xml.Xml.TotalX==mp5Amc.Amc.TotalX&&
     mp5Xml.Xml.TotalY==mp5Amc.Amc.TotalY&&mp5Xml.Xml.ReleaseTimeMs==mp5Master.ReleaseTime&&
     mp5Xml.Xml.MoveRCommandCostMs==1&&!mp5Xml.Xml.CoordinatesScaled,
     "MP5-SD parameters complete XML-only extraction with the same AMC movement totals and release timing");
    reject(delegate{WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_mp7",33);},
     "MP7 ID 33 cannot accept MP5-SD VData through the alias");
    reject(delegate{WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_mp5sd",33);},
     "Matching MP5-SD names with the wrong MP7 ID remain rejected");
    reject(delegate{WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_ak47",23);},
     "MP5-SD ID alone cannot authorize an unrelated entity name");
    mp5Read.DesignerName="weapon_mp7";
    reject(delegate{WeaponDataReader.Validate(mp5Snapshot);},"Imported MP5-SD snapshots cannot substitute MP7 VData");
    check(WeaponVDataIdentity.Matches(33,"weapon_mp7","weapon_mp7")&&
     !WeaponVDataIdentity.Matches(23,"weapon_mp7","weapon_mp7"),
     "MP7 keeps exact-name validation while MP5-SD requires its own variant VData");
    Marshal.WriteIntPtr(vdata,Layout.VDataName,name);
    Marshal.WriteByte(vdata,Layout.VDataFullAuto,2);
    reject(delegate{WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_ak47",7);},
     "Invalid native boolean cannot masquerade as a full-auto VData");
    Marshal.WriteIntPtr(weapon,Layout.WeaponVData,new IntPtr(1));
    reject(delegate{WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_ak47",7);},
     "Invalid VData pointer is rejected with the field-specific diagnostic");
   }
  } finally{Marshal.FreeHGlobal(mp5Name);Marshal.FreeHGlobal(silenced);Marshal.FreeHGlobal(name);Marshal.FreeHGlobal(vdata);Marshal.FreeHGlobal(weapon);}
 }
}
