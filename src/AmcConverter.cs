using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace RecoilProbe {
 internal sealed class RecoilPoint {
  internal double Tick, ObservedMs, Pitch, Yaw;
  internal int Shot;
 }
 internal sealed class AmcResult {
  public string AmcPath, ReportPath, Weapon, SourcePath;
  public int Shots, MoveCommands, AntiRepeatMs = 30000, TotalX, TotalY;
  public double SourceSensitivity, TargetSensitivity, ActiveDurationMs, MaximumTimingResidualMs;
  public bool WeaponAndSensitivityAutomaticallyRead, InstantaneousRecoilVerified = false;
  public string Method = "Recorded shot-time base-angle anchors; two estimated linear steps per interval.";
  public string Assumptions = "m_pitch=0.022; m_yaw=0.022; weapon_recoil_scale=2.0; game_tick_hz=64; MoveR uses raw mouse counts.";
 }
 internal static class AmcConverter {
  private sealed class Group {
   internal Dictionary<string,int> Counts = new Dictionary<string,int>();
   internal Dictionary<string,Sample> States = new Dictionary<string,Sample>();
   internal double FirstTime = Double.PositiveInfinity;
   internal int Shot;
   internal void Add(Sample sample) {
    if (Shot != 0 && Shot != sample.shots_fired)
     throw new InvalidOperationException("Tick recoil associato a numeri di colpo diversi.");
    Shot = sample.shots_fired; FirstTime = Math.Min(FirstTime,sample.observed_ms);
    string state = sample.predictable_angle.pitch.ToString("R",CultureInfo.InvariantCulture) + "/" +
     sample.predictable_angle.yaw.ToString("R",CultureInfo.InvariantCulture) + "/" +
     sample.predictable_velocity.pitch.ToString("R",CultureInfo.InvariantCulture) + "/" +
     sample.predictable_velocity.yaw.ToString("R",CultureInfo.InvariantCulture);
    if (!Counts.ContainsKey(state)) { Counts.Add(state,0); States.Add(state,sample); }
    Counts[state]++;
   }
   internal Sample Stable() {
    string best=null;int count=0;
    foreach(KeyValuePair<string,int> pair in Counts)
     if(pair.Value>count) {best=pair.Key;count=pair.Value;}
    return States[best];
   }
  }
  internal static List<RecoilPoint> Points(RecordingData data) {
   if(data==null || data.Samples.Count<2)throw new InvalidOperationException("Registrazione insufficiente.");
   Dictionary<string,Group> groups=new Dictionary<string,Group>();
   uint hash=0; bool sawPress=false; int previousPositive=0;
   Vector eye=null;
   foreach(Sample s in data.Samples) {
    if(s.left_down && s.observed_ms>=0)sawPress=true;
    if(!sawPress || s.shots_fired<=0) {
     if(sawPress && previousPositive>1 && s.left_down)
      throw new InvalidOperationException("Contatore colpi azzerato durante lo spray.");
     continue;
    }
    previousPositive=Math.Max(previousPositive,s.shots_fired);
    if(s.weapon_hash!=0) {
     if(hash==0)hash=s.weapon_hash;
     else if(hash!=s.weapon_hash)throw new InvalidOperationException("Hash arma cambiato nel file.");
    }
    if(s.unpredictable_angle!=null && (Math.Abs(s.unpredictable_angle.pitch)>0.001 ||
     Math.Abs(s.unpredictable_angle.yaw)>0.001 || Math.Abs(s.unpredictable_angle.roll)>0.001))
     throw new InvalidOperationException("Aim punch esterno rilevato. Ripeti senza ricevere danni.");
    if(s.eye_angle!=null) {
     if(eye==null)eye=s.eye_angle;
     else if(Math.Abs(DeltaAngle(s.eye_angle.pitch,eye.pitch))>0.02 ||
      Math.Abs(DeltaAngle(s.eye_angle.yaw,eye.yaw))>0.02)
      throw new InvalidOperationException("Mouse/visuale mossi durante lo spray. Ripeti tenendo fermo il mouse.");
    }
    if(s.predictable_tick<=0 || s.predictable_tick_fraction<0 || s.predictable_tick_fraction>=1)
     throw new InvalidOperationException("Tick recoil non valido.");
    string key=s.predictable_tick.ToString(CultureInfo.InvariantCulture)+"/"+
     s.predictable_tick_fraction.ToString("R",CultureInfo.InvariantCulture);
    Group group;
    if(!groups.TryGetValue(key,out group)){group=new Group();groups.Add(key,group);}
    group.Add(s);
   }
   List<RecoilPoint> points=new List<RecoilPoint>();
   foreach(Group group in groups.Values) {
    Sample s=group.Stable();
    points.Add(new RecoilPoint {Tick=s.predictable_tick+(double)s.predictable_tick_fraction,
     ObservedMs=group.FirstTime,Pitch=s.predictable_angle.pitch,Yaw=s.predictable_angle.yaw,Shot=group.Shot});
   }
   points.Sort(delegate(RecoilPoint a,RecoilPoint b){return a.Tick.CompareTo(b.Tick);});
   if(points.Count<2)throw new InvalidOperationException("Servono almeno due colpi nello stesso spray.");
   if(points.Count>1000)throw new InvalidOperationException("Troppi colpi per una singola registrazione.");
   if(points[0].Shot!=1 || Math.Abs(points[0].Pitch)>0.02 || Math.Abs(points[0].Yaw)>0.02)
    throw new InvalidOperationException("Primo colpo o reset recoil mancanti. Ripeti da recoil azzerato.");
   for(int i=0;i<points.Count;i++) {
    if(points[i].Shot!=i+1)
     throw new InvalidOperationException("Manca un aggiornamento di colpo. Nessun AMC inventato per colpi non registrati.");
    if(i>0) {
     double delta=(points[i].Tick-points[i-1].Tick)*15.625;
     if(delta<20 || delta>2000)
      throw new InvalidOperationException("Intervallo tra colpi non plausibile.");
    }
   }
   double duration=(points[points.Count-1].Tick-points[0].Tick)*15.625;
   if(duration>20000)throw new InvalidOperationException("Spray oltre il limite di 20 secondi.");
   return points;
  }
  private static double DeltaAngle(double a,double b) {
   double delta=(a-b)%360; if(delta>180)delta-=360;if(delta< -180)delta+=360;return delta;
  }
  private static int Round(double value) {
   if(Double.IsNaN(value)||Double.IsInfinity(value)||Math.Abs(value)>10000000)
    throw new InvalidOperationException("Movimento AMC fuori scala.");
   return checked((int)Math.Round(value,MidpointRounding.AwayFromZero));
  }
  private static void Delay(List<string> commands,int ms) {
   if(ms<0)throw new InvalidOperationException("Timeline AMC non valida.");
   while(ms>0){int step=Math.Min(999,ms);commands.Add("Delay "+step+" ms");ms-=step;}
  }
  private static void Move(List<string> commands,int dx,int dy,AmcResult result) {
   // Larger deltas are split at the same scheduled time, not spread into micro delays.
   while(dx!=0 || dy!=0) {
    int x=Math.Max(-127,Math.Min(127,dx)),y=Math.Max(-127,Math.Min(127,dy));
    commands.Add("MoveR "+x+" "+y);result.MoveCommands++;dx-=x;dy-=y;
    if(result.MoveCommands>10000)throw new InvalidOperationException("Troppi comandi AMC.");
   }
  }
  internal static AmcResult Convert(RecordingData data,string output,double targetSensitivity) {
   if(!IdentityReader.ValidSensitivity(targetSensitivity))
    throw new InvalidOperationException("Sensibilita' destinazione non valida.");
   List<RecoilPoint> points=Points(data);
   AmcResult result=new AmcResult {Weapon=data.WeaponName,Shots=points.Count,
    SourcePath=data.SourcePath,SourceSensitivity=data.Sensitivity,TargetSensitivity=targetSensitivity,
    WeaponAndSensitivityAutomaticallyRead=data.AutomaticIdentity};
   List<string> commands=new List<string>();commands.Add("LeftDown 1");
   double firstTime=points[0].ObservedMs,firstTick=points[0].Tick;
   if(firstTime<0 || firstTime>2000)throw new InvalidOperationException("Origine del primo colpo non valida.");
   double scale=2.0/(targetSensitivity*0.022);
   int lastTime=0,lastX=0,lastY=0;
   List<double> gaps=new List<double>();
   for(int i=0;i<points.Count;i++) {
    double nativeTime=firstTime+(points[i].Tick-firstTick)*15.625;
    result.MaximumTimingResidualMs=Math.Max(result.MaximumTimingResidualMs,
     Math.Abs(nativeTime-points[i].ObservedMs));
    if(i==0)continue;
    gaps.Add((points[i].Tick-points[i-1].Tick)*15.625);
    for(int step=1;step<=2;step++) {
     double f=step/2.0;
     double tick=points[i-1].Tick+(points[i].Tick-points[i-1].Tick)*f;
     double pitch=points[i-1].Pitch+(points[i].Pitch-points[i-1].Pitch)*f;
     double yaw=points[i-1].Yaw+(points[i].Yaw-points[i-1].Yaw)*f;
     int time=Round(firstTime+(tick-firstTick)*15.625),x=Round(yaw*scale),y=Round(-pitch*scale);
     int dx=x-lastX,dy=y-lastY;
     if(dx==0 && dy==0)continue;
     Delay(commands,time-lastTime);Move(commands,dx,dy,result);
     lastTime=time;lastX=x;lastY=y;
    }
   }
   if(result.MaximumTimingResidualMs>30)
    throw new InvalidOperationException("Tempi osservati e tick troppo diversi. Ripeti la registrazione.");
   gaps.Sort();double cycle=gaps[gaps.Count/2];
   int end=Round(firstTime+(points[points.Count-1].Tick-firstTick)*15.625+cycle);
   Delay(commands,end-lastTime);commands.Add("LeftUp 1");Delay(commands,30000);
   result.TotalX=lastX;result.TotalY=lastY;result.ActiveDurationMs=end;
   string path=Path.GetFullPath(output);
   if(!String.Equals(Path.GetExtension(path),".amc",StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("L'output deve avere estensione .amc.");
   result.AmcPath=path;result.ReportPath=Path.ChangeExtension(path,".report.json");
   if(File.Exists(path)||File.Exists(result.ReportPath))
    throw new InvalidOperationException("Output gia' esistente. Scegli un nome nuovo.");
   Directory.CreateDirectory(Path.GetDirectoryName(path));
   // Finish the report in memory before creating output files.
   string report=RecordingIO.Serializer().Serialize(result);
   using(MemoryStream bytes=new MemoryStream()) {
    XmlWriterSettings settings=new XmlWriterSettings {Encoding=Encoding.Unicode,Indent=true,
     NewLineChars="\r\n",NewLineHandling=NewLineHandling.Replace};
    using(XmlWriter writer=XmlWriter.Create(bytes,settings)) {
     writer.WriteStartDocument();writer.WriteStartElement("Root");writer.WriteStartElement("DefaultMacro");
     writer.WriteElementString("Major","");
     writer.WriteElementString("Description",data.WeaponName+" · SENS "+
      targetSensitivity.ToString("0.000###",CultureInfo.InvariantCulture)+" · PROVA NON VALIDATA IN GIOCO");
     writer.WriteElementString("Comment",result.Method+" "+result.Assumptions+
      " Source: "+(data.SourcePath==null?"recording":Path.GetFileName(data.SourcePath))+
      ". Weapon/sensitivity automatically read: "+data.AutomaticIdentity+
      ". 30000ms tail delays repetition of the active invocation; a new press can start a new run.");
     writer.WriteStartElement("GUIOption");writer.WriteElementString("RepeatType","1");writer.WriteEndElement();
     writer.WriteStartElement("KeyUp");writer.WriteElementString("Syntax","LeftUp 1\r\n");writer.WriteEndElement();
     writer.WriteStartElement("KeyDown");writer.WriteElementString("Syntax",String.Join("\r\n",commands.ToArray())+"\r\n");
     writer.WriteEndElement();writer.WriteElementString("Software","Counter-Strike 2");
     writer.WriteEndElement();writer.WriteEndElement();writer.WriteEndDocument();
    }
    byte[] content=bytes.ToArray();
    using(FileStream file=new FileStream(path,FileMode.CreateNew,FileAccess.Write))
     file.Write(content,0,content.Length);
   }
   try {
    using(FileStream stream=new FileStream(result.ReportPath,FileMode.CreateNew,FileAccess.Write))
    using(StreamWriter writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(report);
   } catch { if(File.Exists(path))File.Delete(path);throw; }
   return result;
  }
  internal static string SuggestedFileName(RecordingData data,double sensitivity) {
   string name=System.Text.RegularExpressions.Regex.Replace(data.WeaponName,@"[^A-Za-z0-9_-]","_");
   if(name.Length>40)name=name.Substring(0,40);
   return name+"_MEMORY_TEST_SENS_"+sensitivity.ToString("0.000###",CultureInfo.InvariantCulture)+"_"+
    DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff",CultureInfo.InvariantCulture)+".amc";
  }
 }
}
