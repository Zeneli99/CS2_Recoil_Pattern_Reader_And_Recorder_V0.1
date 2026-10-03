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
  public int Shots, MoveCommands, OriginalMoveCommands, AntiRepeatMs = 30000, TotalX, TotalY;
  public int SmoothingStepMs = 10;
  public string SourceKind = "CSV/JSON";
  public double SourceSensitivity, TargetSensitivity, ActiveDurationMs, MaximumTimingResidualMs;
  public bool WeaponAndSensitivityAutomaticallyRead, InstantaneousRecoilVerified = false;
  public string Method = "Recorded shot-time base-angle anchors; moderate linear steps around 10ms, retaining original midpoint and shot anchors.";
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
  internal const int SmoothStepMs = 10;
  private static void Segment(List<string> commands,int fromTime,int toTime,
   double fromX,double fromY,double toX,double toY,
   ref int lastTime,ref int lastX,ref int lastY,AmcResult result) {
   if(toTime<fromTime)throw new InvalidOperationException("Timeline AMC non valida.");
   int span=toTime-fromTime;
   int parts=Math.Max(1,(span+SmoothStepMs-1)/SmoothStepMs);
   for(int part=1;part<=parts;part++) {
    double f=part/(double)parts;
    int time=fromTime+Round(span*f);
    int x=Round(part==parts?toX:fromX+(toX-fromX)*f);
    int y=Round(part==parts?toY:fromY+(toY-fromY)*f);
    int dx=x-lastX,dy=y-lastY;
    if(dx==0&&dy==0)continue;
    Delay(commands,time-lastTime);Move(commands,dx,dy,result);
    lastTime=time;lastX=x;lastY=y;
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
    // Keep both old anchor boundaries, subdividing each half independently.
    double tickGap=points[i].Tick-points[i-1].Tick;
    for(int half=0;half<2;half++) {
     double f0=half/2.0,f1=(half+1)/2.0;
     int from=Round(firstTime+(points[i-1].Tick+tickGap*f0-firstTick)*15.625);
     int to=Round(firstTime+(points[i-1].Tick+tickGap*f1-firstTick)*15.625);
     double fromPitch=points[i-1].Pitch+(points[i].Pitch-points[i-1].Pitch)*f0;
     double toPitch=points[i-1].Pitch+(points[i].Pitch-points[i-1].Pitch)*f1;
     double fromYaw=points[i-1].Yaw+(points[i].Yaw-points[i-1].Yaw)*f0;
     double toYaw=points[i-1].Yaw+(points[i].Yaw-points[i-1].Yaw)*f1;
     Segment(commands,from,to,fromYaw*scale,-fromPitch*scale,toYaw*scale,-toPitch*scale,
      ref lastTime,ref lastX,ref lastY,result);
    }
   }
   if(result.MaximumTimingResidualMs>30)
    throw new InvalidOperationException("Tempi osservati e tick troppo diversi. Ripeti la registrazione.");
   gaps.Sort();double cycle=gaps[gaps.Count/2];
   int end=Round(firstTime+(points[points.Count-1].Tick-firstTick)*15.625+cycle);
   Delay(commands,end-lastTime);commands.Add("LeftUp 1");Delay(commands,30000);
   result.TotalX=lastX;result.TotalY=lastY;result.ActiveDurationMs=end;
   return Save(result,commands,output);
  }
  internal static AmcResult Smooth(AmcInput data,string output,double targetSensitivity) {
   if(data==null||data.Anchors.Count==0)throw new InvalidOperationException("AMC insufficiente.");
   if(!IdentityReader.ValidSensitivity(targetSensitivity))
    throw new InvalidOperationException("Sensibilita' destinazione non valida.");
   AmcResult result=new AmcResult {Weapon=data.WeaponName,SourceKind="AMC",
    SourcePath=data.SourcePath,SourceSensitivity=data.Sensitivity,TargetSensitivity=targetSensitivity,
    OriginalMoveCommands=data.MoveCommands,
    Method="Existing AMC cumulative positions resampled around 10ms; every original timed anchor retained. First movement kept at its original time.",
    Assumptions="Existing AMC raw mouse counts; sensitivity from its header; no new game-memory verification. Cumulative rounding avoids drift."};
   List<string> commands=new List<string>();commands.Add("LeftDown 1");
   double ratio=data.Sensitivity/targetSensitivity;
   int lastTime=0,lastX=0,lastY=0;
   MouseAnchor first=data.Anchors[0];
   // An AMC has no recorded zero-recoil timestamp: do not guess its initial ramp.
   Segment(commands,first.Time,first.Time,first.X*ratio,first.Y*ratio,first.X*ratio,first.Y*ratio,
    ref lastTime,ref lastX,ref lastY,result);
   for(int i=1;i<data.Anchors.Count;i++) {
    MouseAnchor a=data.Anchors[i-1],b=data.Anchors[i];
    Segment(commands,a.Time,b.Time,a.X*ratio,a.Y*ratio,b.X*ratio,b.Y*ratio,
     ref lastTime,ref lastX,ref lastY,result);
   }
   Delay(commands,data.ReleaseTime-lastTime);commands.Add("LeftUp 1");Delay(commands,data.TailMs);
   result.TotalX=lastX;result.TotalY=lastY;result.ActiveDurationMs=data.ReleaseTime;
   return Save(result,commands,output);
  }
  private static AmcResult Save(AmcResult result,List<string> commands,string output) {
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
     writer.WriteElementString("Description",result.Weapon+" · SENS "+
      result.TargetSensitivity.ToString("0.000###",CultureInfo.InvariantCulture)+" · SMOOTH 10 ms · PROVA NON VALIDATA IN GIOCO");
     writer.WriteElementString("Comment",result.Method+" "+result.Assumptions+
      " Source: "+(result.SourcePath==null?"recording":Path.GetFileName(result.SourcePath))+
      ". Source kind: "+result.SourceKind+". Weapon/sensitivity automatically read: "+result.WeaponAndSensitivityAutomaticallyRead+
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
  internal static string SuggestedFileName(AmcInput data,double sensitivity) {
   return data.WeaponName+"_SMOOTH_SENS_"+sensitivity.ToString("0.000###",CultureInfo.InvariantCulture)+"_"+
    DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff",CultureInfo.InvariantCulture)+".amc";
  }
  internal static string SuggestedFileName(RecordingData data,double sensitivity) {
   string name=System.Text.RegularExpressions.Regex.Replace(data.WeaponName,@"[^A-Za-z0-9_-]","_");
   if(name.Length>40)name=name.Substring(0,40);
   return name+"_MEMORY_TEST_SENS_"+sensitivity.ToString("0.000###",CultureInfo.InvariantCulture)+"_"+
    DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff",CultureInfo.InvariantCulture)+".amc";
  }
 }
}
