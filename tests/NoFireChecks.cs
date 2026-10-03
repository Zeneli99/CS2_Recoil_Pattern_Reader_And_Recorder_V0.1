using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using RecoilProbe;

internal static class NoFireChecks {
 private static WeaponSnapshot Example() {
  return new WeaponSnapshot {Weapon="AK47",Sensitivity=1.25F,Build=14188,SchemaCommit=Layout.SourceCommit,
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
  NativeReadChecks(check,reject);
 }
 private static void WriteFloat(IntPtr address,int offset,float value) {
  Marshal.Copy(BitConverter.GetBytes(value),0,IntPtr.Add(address,offset),4);
 }
 private static void NativeReadChecks(Action<bool,string> check,Action<Action,string> reject) {
  IntPtr weapon=Marshal.AllocHGlobal(0x2000),vdata=Marshal.AllocHGlobal(0x900),name=Marshal.StringToHGlobalAnsi("weapon_ak47");
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
    Marshal.WriteByte(vdata,Layout.VDataFullAuto,2);
    reject(delegate{WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_ak47",7);},
     "Invalid native boolean cannot masquerade as a full-auto VData");
    Marshal.WriteIntPtr(weapon,Layout.WeaponVData,new IntPtr(1));
    reject(delegate{WeaponDataReader.ReadParameters(memory,weapon.ToInt64(),"weapon_ak47",7);},
     "Invalid VData pointer is rejected with the field-specific diagnostic");
   }
  } finally{Marshal.FreeHGlobal(name);Marshal.FreeHGlobal(vdata);Marshal.FreeHGlobal(weapon);}
 }
}
