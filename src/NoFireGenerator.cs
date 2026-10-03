using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace RecoilProbe {
 // Native input and model assumptions deliberately live in separate objects.
 internal sealed class WeaponParameters {
  public string DesignerName;
  public int ItemDefinitionIndex, MaxClip, BulletsPerShot, RecoilSeed, Mode;
  public bool FullAuto;
  public float CycleSeconds, RecoilAngle, AngleVariance, RecoilMagnitude, MagnitudeVariance;
 }
 internal sealed class NoFireModel {
  public string Algorithm = "SOURCE_LEGACY_EXPERIMENTAL_V1";
  public double MousePitch = 0.022, MouseYaw = 0.022, RecoilScale = 2;
  public int SuppressionShots = 4, TableLength = 64;
  public double SuppressionFactor = 0.75, Variance = 0.55;
  public double AngleExponentialDecay = 8, AngleLinearDecay = 18, VelocityDecay = 4.5;
  public double IntegrationStepSeconds = 1.0 / 128;
  public string Provenance = "Assumed legacy constants, NOT read from this CS2 session. Current engine dynamics and first-shot latency are not verified.";
 }
 internal sealed class WeaponSnapshot {
  public string Format = "CS2_NO_FIRE_V1", Version = "0.3.0", CreatedUtc, Weapon, SourcePath;
  public int Build;
  public string SchemaCommit, VDataAddress, VDataPointerLocator = "weapon+0x388 candidate; validated against active entity name and stable values";
  public float Sensitivity;
  public bool NativeParametersRead, CurrentEngineAlgorithmVerified = false;
  public WeaponParameters Native;
  public NoFireModel Model = new NoFireModel();
  public string Warning = "EXPERIMENTAL. Only weapon parameters are extracted. Impulses, trajectory and timing are reconstructed, not a native ballistic path; no guarantee of centered bullets. Random spread and view-punch shake are excluded.";
 }
 internal static class WeaponDataReader {
  private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
  internal static void Validate(WeaponSnapshot data) {
   if(data==null||data.Format!="CS2_NO_FIRE_V1"||data.Native==null||data.Model==null)
    throw new InvalidOperationException("Snapshot estrazione assente o formato non supportato.");
   WeaponParameters p=data.Native;NoFireModel m=data.Model;
   if(data.Build!=Layout.TargetBuild||data.SchemaCommit!=Layout.SourceCommit)
    throw new InvalidOperationException("Snapshot di una build/layout non supportata.");
   string weapon=WeaponCatalog.Name(p.ItemDefinitionIndex);
   if(data.Weapon!=weapon||String.IsNullOrEmpty(p.DesignerName)||
    !p.DesignerName.StartsWith("weapon_",StringComparison.Ordinal)||p.DesignerName.Length>80)
    throw new InvalidOperationException("Identita' arma incoerente nello snapshot.");
   if(!WeaponCatalog.IsFullAuto(p.ItemDefinitionIndex)||!p.FullAuto||p.BulletsPerShot!=1||p.Mode!=0)
    throw new InvalidOperationException("Prova senza sparare: solo armi full-auto, modalita' primaria, senza zoom o burst.");
   if(p.MaxClip<2||p.MaxClip>200||!Finite(p.CycleSeconds)||p.CycleSeconds<0.02||p.CycleSeconds>0.5||
    p.MaxClip*(double)p.CycleSeconds>20||p.RecoilSeed<0)
    throw new InvalidOperationException("Capacita', ciclo o seed dell'arma non plausibili.");
   if(!Finite(p.RecoilAngle)||Math.Abs(p.RecoilAngle)>180||!Finite(p.AngleVariance)||p.AngleVariance<0||p.AngleVariance>180||
    !Finite(p.RecoilMagnitude)||p.RecoilMagnitude<=0||p.RecoilMagnitude>200||!Finite(p.MagnitudeVariance)||
    p.MagnitudeVariance<0||p.MagnitudeVariance>p.RecoilMagnitude)
    throw new InvalidOperationException("Parametri recoil VData fuori scala.");
   if(!IdentityReader.ValidSensitivity(data.Sensitivity)||m.Algorithm!="SOURCE_LEGACY_EXPERIMENTAL_V1"||m.TableLength!=64||
    m.SuppressionShots<0||m.SuppressionShots>64||!Finite(m.SuppressionFactor)||m.SuppressionFactor<0||m.SuppressionFactor>1||
    !Finite(m.Variance)||m.Variance<0||m.Variance>1||!Finite(m.MousePitch)||m.MousePitch<0.001||m.MousePitch>1||
    !Finite(m.MouseYaw)||m.MouseYaw<0.001||m.MouseYaw>1||!Finite(m.RecoilScale)||m.RecoilScale<=0||m.RecoilScale>10||
    !Finite(m.AngleExponentialDecay)||m.AngleExponentialDecay<0||m.AngleExponentialDecay>100||
    !Finite(m.AngleLinearDecay)||m.AngleLinearDecay<0||m.AngleLinearDecay>100||!Finite(m.VelocityDecay)||
    m.VelocityDecay<0||m.VelocityDecay>100||m.IntegrationStepSeconds!=1.0/128)
    throw new InvalidOperationException("Sensibilita' o ipotesi del modello non valide.");
   // Input JSON is editable: never accept an imported assertion of algorithm verification.
   data.CurrentEngineAlgorithmVerified=false;
  }
  internal static WeaponParameters ReadParameters(ReadMemory memory,long weapon,string expectedName,int definition) {
   long vdata=memory.NamedPointer(weapon+Layout.WeaponVData,"VData arma (candidato +0x388)");
   long name=memory.NamedPointer(vdata+Layout.VDataName,"Nome VData arma");
   string actual=IdentityReader.ReadName(memory,name);
   if(actual!=expectedName)throw new InvalidOperationException("VData non validata: nome '"+actual+"' diverso da '"+expectedName+"'.");
   byte full=memory.Byte(vdata+Layout.VDataFullAuto);
   if(full>1)throw new InvalidOperationException("Flag full-auto VData non valido.");
   return new WeaponParameters {DesignerName=actual,ItemDefinitionIndex=definition,
    MaxClip=memory.Int(vdata+Layout.VDataMaxClip),BulletsPerShot=memory.Int(vdata+Layout.VDataBullets),
    FullAuto=full==1,Mode=memory.Int(weapon+Layout.WeaponMode),RecoilSeed=memory.Int(vdata+Layout.VDataRecoilSeed),
    CycleSeconds=memory.Float(vdata+Layout.VDataCycle),RecoilAngle=memory.Float(vdata+Layout.VDataRecoilAngle),
    AngleVariance=memory.Float(vdata+Layout.VDataRecoilAngleVariance),RecoilMagnitude=memory.Float(vdata+Layout.VDataRecoilMagnitude),
    MagnitudeVariance=memory.Float(vdata+Layout.VDataRecoilMagnitudeVariance)};
  }
  private static void Idle(Game game,GameIdentity identity) {
   float index=game.Memory.Float(identity.WeaponAddress+Layout.WeaponRecoilIndexFloat);
   if(identity.Scoped||identity.Ammo<2||game.Memory.Byte(identity.WeaponAddress+Layout.WeaponBurst)!=0)
    throw new InvalidOperationException("Togli zoom/burst ed equipaggia un'arma automatica carica.");
   if((Native.GetAsyncKeyState(1)&0x8000)!=0||!Finite(index)||index<0||index>0.001||
    game.Memory.Int(game.Pawn+Layout.ShotsFired)!=0||game.Memory.Byte(identity.WeaponAddress+Layout.WeaponReloading)!=0)
    throw new InvalidOperationException("Non sparare: attendi il reset recoil e la fine della ricarica, poi premi F8.");
  }
  internal static WeaponSnapshot Extract(Game game) {
   try {return ExtractStable(game);}
   catch(Exception ex) {Diagnostics.Record(ex,game.StartupContext(),game.Memory.PointerTrace);throw;}
  }
  private static WeaponSnapshot ExtractStable(Game game) {
   game.VerifySession();
   bool localServer=false;
   foreach(ProcessModule module in game.Process.Modules)
    if(String.Equals(module.ModuleName,"server.dll",StringComparison.OrdinalIgnoreCase))localServer=true;
   if(!localServer)throw new InvalidOperationException("Modulo server locale non trovato. Apri una mappa offline in questo processo CS2.");
   GameIdentity before=game.ReadIdentity();Idle(game,before);
   WeaponParameters p=ReadParameters(game.Memory,before.WeaponAddress,before.DesignerName,before.ItemDefinitionIndex);
   long pointer=game.Memory.Pointer(before.WeaponAddress+Layout.WeaponVData);
   WeaponSnapshot data=new WeaponSnapshot {CreatedUtc=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture),
    Weapon=before.WeaponName,Sensitivity=before.Sensitivity,Build=game.Build,SchemaCommit=Layout.SourceCommit,
    Native=p,NativeParametersRead=true,VDataAddress="0x"+pointer.ToString("X",CultureInfo.InvariantCulture)};
   Validate(data);
   if(before.Ammo!=p.MaxClip)throw new InvalidOperationException("Ricarica completamente l'arma: il generatore parte da un caricatore pieno.");
   WeaponParameters second=ReadParameters(game.Memory,before.WeaponAddress,before.DesignerName,before.ItemDefinitionIndex);
   game.VerifySession();GameIdentity after=game.ReadIdentity();Idle(game,after);
   if(before.WeaponHandle!=after.WeaponHandle||before.WeaponAddress!=after.WeaponAddress||
    before.Sensitivity!=after.Sensitivity||before.Ammo!=after.Ammo||
    pointer!=game.Memory.Pointer(after.WeaponAddress+Layout.WeaponVData)||
    RecordingIO.Serializer().Serialize(p)!=RecordingIO.Serializer().Serialize(second))
    throw new InvalidOperationException("Arma o parametri cambiati durante l'estrazione. Ripeti F8.");
   return data;
  }
 }
 // Park-Miller/shuffle stream, used only by the explicitly unverified legacy model.
 internal sealed class LegacyRecoilRandom {
  private int seed,shuffleValue;
  private readonly int[] shuffle=new int[32];
  internal LegacyRecoilRandom(int value) {
   if(value<0)throw new InvalidOperationException("Seed negativo non supportato.");
   seed=-value;
  }
  private void AdvanceSeed() {
   int k=seed/127773;seed=16807*(seed-k*127773)-2836*k;if(seed<0)seed+=2147483647;
  }
  internal int NextInteger() {
   if(seed<=0||shuffleValue==0) {
    seed=-seed<1?1:-seed;
    for(int j=39;j>=0;j--){AdvanceSeed();if(j<32)shuffle[j]=seed;}
    shuffleValue=shuffle[0];
   }
   AdvanceSeed();int index=shuffleValue/67108864;
   shuffleValue=shuffle[index];shuffle[index]=seed;return shuffleValue;
  }
  internal float NextFloat(float low,float high) {
   float value=(float)((1.0/2147483647)*NextInteger());
   if(value>1.0-1.2e-7)value=(float)(1.0-1.2e-7);
   return value*(high-low)+low;
  }
 }
 internal sealed class ReconstructedShot {
  public int Shot;
  public double TimeMs,Pitch,Yaw,PitchVelocity,YawVelocity;
  public float ImpulseAngle,ImpulseMagnitude;
 }
 internal static class NoFireGenerator {
  internal static List<ReconstructedShot> Reconstruct(WeaponSnapshot data) {
   WeaponDataReader.Validate(data);WeaponParameters p=data.Native;NoFireModel m=data.Model;
   LegacyRecoilRandom random=new LegacyRecoilRandom(p.RecoilSeed);
   float[] angles=new float[64],magnitudes=new float[64];float a=0,mag=0;
   for(int i=0;i<64;i++) {
    float nextAngle=p.RecoilAngle+random.NextFloat(-p.AngleVariance,p.AngleVariance);
    float nextMagnitude=p.RecoilMagnitude+random.NextFloat(-p.MagnitudeVariance,p.MagnitudeVariance);
    if(i>0){a=a+(nextAngle-a)*(float)m.Variance;mag=mag+(nextMagnitude-mag)*(float)m.Variance;}
    else {a=nextAngle;mag=nextMagnitude;}
    if(i<m.SuppressionShots)mag*=(float)m.SuppressionFactor+
     (i/(float)m.SuppressionShots)*(1-(float)m.SuppressionFactor);
    angles[i]=a;magnitudes[i]=mag;
   }
   double pitch=0,yaw=0,pv=0,yv=0;
   List<ReconstructedShot> shots=new List<ReconstructedShot>();
   for(int i=0;i<p.MaxClip;i++) {
    if(i>0) {
     double left=p.CycleSeconds;
     while(left>1e-10) {
      double dt=Math.Min(m.IntegrationStepSeconds,left),angleFactor=Math.Exp(-m.AngleExponentialDecay*dt);
      pitch*=angleFactor;yaw*=angleFactor;double length=Math.Sqrt(pitch*pitch+yaw*yaw),subtract=m.AngleLinearDecay*dt;
      if(length>subtract&&length>1e-12){double f=1-subtract/length;pitch*=f;yaw*=f;}else{pitch=0;yaw=0;}
      pitch+=pv*dt*0.5;yaw+=yv*dt*0.5;
      double vf=Math.Exp(-m.VelocityDecay*dt);pv*=vf;yv*=vf;
      pitch+=pv*dt*0.5;yaw+=yv*dt*0.5;left-=dt;
     }
    }
    int table=i%64;double radians=angles[table]*(Math.PI/180);
    pv-=Math.Cos(radians)*magnitudes[table];yv-=Math.Sin(radians)*magnitudes[table];
    shots.Add(new ReconstructedShot {Shot=i+1,TimeMs=i*(double)p.CycleSeconds*1000,Pitch=pitch,Yaw=yaw,
     PitchVelocity=pv,YawVelocity=yv,ImpulseAngle=angles[table],ImpulseMagnitude=magnitudes[table]});
   }
   return shots;
  }
  internal static AmcResult Convert(WeaponSnapshot data,string output,double sensitivity) {
   if(!IdentityReader.ValidSensitivity(sensitivity))throw new InvalidOperationException("Sensibilita' destinazione non valida.");
   List<ReconstructedShot> shots=Reconstruct(data);NoFireModel m=data.Model;
   AmcResult result=new AmcResult {Weapon=data.Weapon,Shots=shots.Count,SourceSensitivity=data.Sensitivity,
    TargetSensitivity=sensitivity,SourcePath=data.SourcePath,SourceKind="CS2_VDATA_NO_FIRE_EXPERIMENTAL",
    WeaponAndSensitivityAutomaticallyRead=data.NativeParametersRead,NoFireInput=data,
    RecoilSequenceReconstructed=true,CurrentEngineAlgorithmVerified=false,
    DeterministicRecoilStateDirectlyRead=false,BallisticTrajectoryDirectlyRead=false,
    Method="Read-only VData parameters plus an unverified legacy seed/impulse/damping model. Simulated shot anchors interpolated around 10ms; cumulative raw mouse rounding, no 1ms motion. No shot was recorded to create this file.",
    Assumptions=data.Model.Provenance+" "+data.Warning,
    ReleaseTimingSource="Assumed immediate first shot, VData cycle; left-up 1ms before the next scheduled shot after a full magazine"};
   List<string> commands=new List<string> {"LeftDown 1"};int lastTime=0,lastX=0,lastY=0;
   double scaleX=m.RecoilScale/(sensitivity*m.MouseYaw),scaleY=m.RecoilScale/(sensitivity*m.MousePitch);
   for(int i=1;i<shots.Count;i++) {
    ReconstructedShot from=shots[i-1],to=shots[i];
    int start=AmcConverter.Round(from.TimeMs),end=AmcConverter.Round(to.TimeMs),span=end-start;
    int parts=Math.Max(1,span/AmcConverter.SmoothStepMs);
    for(int part=1;part<=parts;part++) {
     double f=part/(double)parts;
     int time=start+AmcConverter.Round(span*f);
     int x=AmcConverter.Round((from.Yaw+(to.Yaw-from.Yaw)*f)*scaleX);
     int y=AmcConverter.Round(-(from.Pitch+(to.Pitch-from.Pitch)*f)*scaleY);
     if(x==lastX&&y==lastY)continue;
     AmcConverter.Delay(commands,time-lastTime);AmcConverter.Move(commands,x-lastX,y-lastY,result);
     lastTime=time;lastX=x;lastY=y;
    }
   }
   int release=AmcConverter.Round(shots.Count*(double)data.Native.CycleSeconds*1000)-1;
   AmcConverter.Delay(commands,release-lastTime);commands.Add("LeftUp 1");AmcConverter.Delay(commands,30000);
   result.TotalX=lastX;result.TotalY=lastY;result.ActiveDurationMs=release;
   return AmcConverter.Save(result,commands,output);
  }
  internal static string SuggestedFileName(WeaponSnapshot data,double sensitivity) {
   return data.Weapon+"_NO_FIRE_TEST_SENS_"+sensitivity.ToString("0.000###",CultureInfo.InvariantCulture)+"_"+
    DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff",CultureInfo.InvariantCulture)+".amc";
  }
 }
 internal static class WeaponSnapshotIO {
  internal static bool TryLoad(string path,out WeaponSnapshot data) {
   data=null;
   if(!String.Equals(Path.GetExtension(path),".json",StringComparison.OrdinalIgnoreCase))return false;
   FileInfo file=new FileInfo(path);if(file.Length>1024*1024)return false;
   string text=File.ReadAllText(path,Encoding.UTF8);
   Dictionary<string,object> header=RecordingIO.Serializer().Deserialize<Dictionary<string,object>>(text);
   object format;if(header==null||!header.TryGetValue("Format",out format)||Convert.ToString(format)!="CS2_NO_FIRE_V1")return false;
   data=RecordingIO.Serializer().Deserialize<WeaponSnapshot>(text);WeaponDataReader.Validate(data);
   data.SourcePath=Path.GetFullPath(path);return true;
  }
  internal static string Save(WeaponSnapshot data,string amcPath) {
   WeaponDataReader.Validate(data);string path=Path.ChangeExtension(Path.GetFullPath(amcPath),".recoil.json");
   if(File.Exists(path))throw new InvalidOperationException("Parametri gia' salvati. Scegli un nome nuovo.");
   Directory.CreateDirectory(Path.GetDirectoryName(path));
   using(FileStream stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write))
   using(StreamWriter writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(RecordingIO.Serializer().Serialize(data));
   data.SourcePath=path;return path;
  }
 }
}
