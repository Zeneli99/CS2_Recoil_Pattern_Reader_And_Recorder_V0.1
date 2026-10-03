using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using RecoilProbe;

internal static class CoreChecks {
 private static int count;
 private static void Check(bool ok,string name) {
  if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);
 }
 private static void Reject(Action action,string name) {
  bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}
  Check(rejected,name);
 }
 private sealed class Timeline {
  internal int Time,X,Y,Release=-1,Tail,Commands;internal bool Left;
  internal List<int[]> Moves=new List<int[]>();
 }
 private static Timeline ParseAmc(string path) {
  byte[] bytes=File.ReadAllBytes(path);Check(bytes[0]==255&&bytes[1]==254,"AMC has UTF-16LE BOM");
  XmlDocument xml=new XmlDocument();xml.Load(path);
  Check(xml.SelectSingleNode("//GUIOption/RepeatType").InnerText=="1","Hold repeat type retained");
  Check(xml.SelectSingleNode("//KeyUp/Syntax").InnerText.Trim()=="LeftUp 1","Release handler lifts left button");
  Timeline t=new Timeline();
  string syntax=xml.SelectSingleNode("//KeyDown/Syntax").InnerText;
  foreach(string line in syntax.Split(new string[] {"\r\n","\n"},StringSplitOptions.RemoveEmptyEntries)) {
   string[] parts=line.Split(' ');
   if(parts[0]=="LeftDown"){Check(!t.Left,"One left-down at start");t.Left=true;}
   else if(parts[0]=="LeftUp"){Check(t.Left,"Left-up follows held click");t.Left=false;t.Release=t.Time;}
   else if(parts[0]=="Delay"){
    int ms=Int32.Parse(parts[1],CultureInfo.InvariantCulture);
    if(parts.Length!=3||parts[2]!="ms"||ms<1||ms>999)throw new Exception("Invalid delay");
    t.Time+=ms;if(t.Release>=0)t.Tail+=ms;
   } else if(parts[0]=="MoveR"){
    if(!t.Left)throw new Exception("Movement after left-up");
    int x=Int32.Parse(parts[1],CultureInfo.InvariantCulture),y=Int32.Parse(parts[2],CultureInfo.InvariantCulture);
    if(Math.Abs(x)>127||Math.Abs(y)>127||(x==0&&y==0))throw new Exception("Invalid MoveR");
    t.X+=x;t.Y+=y;t.Commands++;t.Moves.Add(new int[] {t.Time,t.X,t.Y});
   } else throw new Exception("Unknown AMC command");
  }
  Check(!t.Left&&t.Tail==30000,"Fixed anti-repeat tail is 30000ms after left-up");return t;
 }
 private static void Preview(Form form,string output) {
  using(form){
   form.Show();Application.DoEvents();form.Refresh();Application.DoEvents();
   foreach(Control child in form.Controls)
    if(child.Right>540||child.Bottom>360||child.Left<0||child.Top<0)
     throw new Exception("Control outside compact form");
   using(Bitmap bitmap=new Bitmap(form.Width,form.Height)){
    form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(output);
   }
  }
 }
 private static IntPtr Allocate(List<IntPtr> allocations,int size) {
  IntPtr pointer=Marshal.AllocHGlobal(size);allocations.Add(pointer);return pointer;
 }
 private static void PutFloat(IntPtr pointer,int offset,float value) {
  Marshal.Copy(BitConverter.GetBytes(value),0,IntPtr.Add(pointer,offset),4);
 }
 private static MemoryFieldException PointerFailure(Action action) {
  try{action();}catch(MemoryFieldException ex){return ex;}
  throw new Exception("Expected a named memory-field failure.");
 }
 private static void StartupDiagnostics(string output) {
  List<IntPtr> allocations=new List<IntPtr>();
  try {
   IntPtr client=Allocate(allocations,0x2800000),pawn=Allocate(allocations,0x3000);
   IntPtr weapons=Allocate(allocations,0x100),sensitivity=Allocate(allocations,0x100);
   IntPtr engine=Allocate(allocations,0x920000),network=Allocate(allocations,0x400);
   IntPtr rules=Allocate(allocations,0x200);
   IntPtr list=Allocate(allocations,0x300),chunk=Allocate(allocations,512*0x70);
   IntPtr weapon=Allocate(allocations,0x2000),name=Marshal.StringToHGlobalAnsi("weapon_ak47");
   allocations.Add(name);
   const int index=1200,slot=0x70*(index&0x1FF);
   uint handle=(3U<<15)|index;
   Marshal.WriteIntPtr(pawn,Layout.WeaponServices,weapons);
   Marshal.WriteInt32(weapons,Layout.ActiveWeapon,unchecked((int)handle));
   Marshal.WriteIntPtr(client,Layout.Sensitivity,sensitivity);
   Marshal.WriteIntPtr(client,Layout.EntityList,list);
   Marshal.WriteIntPtr(list,0x10+8*(index>>9),chunk);
   Marshal.WriteIntPtr(chunk,slot,weapon);
   Marshal.WriteInt32(chunk,slot+0x10,unchecked((int)(handle+(1U<<15))));
   Marshal.WriteInt32(chunk,slot+0x30,1);
   Marshal.WriteIntPtr(chunk,slot+Layout.DesignerName,name);
   Marshal.WriteIntPtr(weapon,Layout.EntityIdentity,IntPtr.Add(chunk,slot));
   Marshal.WriteInt16(weapon,Layout.AttributeManager+Layout.ItemView+Layout.ItemDefinitionIndex,7);
   Marshal.WriteInt32(weapon,Layout.WeaponClip,30);
   PutFloat(sensitivity,Layout.SensitivityValue,1.234567F);
   PutFloat(pawn,Layout.PawnMouseSensitivity,1F);
   PutFloat(pawn,Layout.FovSensitivityAdjust,1F);Marshal.WriteByte(pawn,Layout.Scoped,0);
   int pid;using(Process self=Process.GetCurrentProcess()){pid=self.Id;}
   using(ReadMemory memory=new ReadMemory(pid)) {
    GameIdentity info=IdentityReader.Read(memory,client.ToInt64(),pawn.ToInt64(),null);
    Check(info.WeaponName=="AK47"&&info.ItemDefinitionIndex==7&&info.DesignerName=="weapon_ak47"&&info.Ammo==30,
     "Native identity chain reads a test-owned weapon through a non-first entity chunk");
    Check(info.WeaponHandle==handle,"Entity reference validates serial with the invalid-handle flag adjustment");
    Check(info.Sensitivity==1.234567F,"Native sensitivity read preserves the original float precision");
    Marshal.WriteIntPtr(client,Layout.LocalPawn,pawn);
    Marshal.WriteIntPtr(engine,Layout.NetworkClient,network);
    Marshal.WriteInt32(network,Layout.SignOnState,6);
    Marshal.WriteInt32(pawn,Layout.Health,100);Marshal.WriteByte(pawn,Layout.LifeState,0);
    Marshal.WriteByte(rules,Layout.IsValveServer,0);
    using(Process own=Process.GetCurrentProcess()) {
     Game session=(Game)FormatterServices.GetUninitializedObject(typeof(Game));
     session.Process=own;session.Memory=memory;session.Client=client.ToInt64();
     session.Engine=engine.ToInt64();session.Pawn=pawn.ToInt64();session.Rules=rules.ToInt64();
     session.Services=0;session.VerifySession();
     Check(true,"Core session verification permits detection before recoil services are required");
     Reject(delegate{session.Read();},"Recording still refuses absent recoil services instead of fabricating values");
    }
    Marshal.WriteIntPtr(client,Layout.Sensitivity,IntPtr.Zero);
    MemoryFieldException settings=PointerFailure(delegate{
     IdentityReader.Read(memory,client.ToInt64(),pawn.ToInt64(),null);
    });
    Check(settings.Field=="Impostazione sensibilita'"&&settings.Value==0&&
     settings.Message.IndexOf("mappa",StringComparison.OrdinalIgnoreCase)<0,
     "Null sensitivity is identified by field instead of claiming that no map is loaded");
    Diagnostics.ReportDirectory=output;
    Diagnostics.Record(settings,"Build letta: 14188; TEST PROCESS ONLY",memory.PointerTrace);
    string diagnostic=File.ReadAllText(Diagnostics.LastPath);
    Check(diagnostic.Contains("Impostazione sensibilita'")&&diagnostic.Contains("Valore: 0x0")&&
     diagnostic.Contains("Build letta: 14188")&&diagnostic.Contains("PUNTATORI LETTI"),
     "Startup report preserves the failed field, raw value, build and preceding reads");
    Diagnostics.Record(settings);
    Check(File.ReadAllText(Diagnostics.LastPath)==diagnostic,
     "UI error handling does not replace the detailed startup report with a generic one");
    Marshal.WriteIntPtr(pawn,Layout.WeaponServices,IntPtr.Zero);
    MemoryFieldException services=PointerFailure(delegate{
     IdentityReader.Read(memory,client.ToInt64(),pawn.ToInt64(),null);
    });
    Check(services.Field=="Servizi delle armi"&&services.Field!=settings.Field,
     "Different null fields produce distinct useful startup messages");
    MemoryFieldException unreadable=PointerFailure(delegate{memory.NamedPointer(0,"Campo inaccessibile");});
    Check(!unreadable.Value.HasValue&&unreadable.InnerException!=null,
     "Unreadable memory is distinguished from a successfully read null pointer");
   }
  } finally {foreach(IntPtr allocation in allocations)Marshal.FreeHGlobal(allocation);}
 }

 [STAThread] private static int Main(string[] args) {
  try{
   if(args.Length!=2)throw new Exception("Usage: CoreChecks.exe FIXTURE_DIRECTORY OUTPUT_DIRECTORY");
   string fixtures=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);
   Directory.CreateDirectory(output);
   StartupDiagnostics(output);
   string csv=File.ReadAllText(Path.Combine(fixtures,"AK47_30_SHOTS_V01.csv"));
   string json=File.ReadAllText(Path.Combine(fixtures,"AK47_30_SHOTS_V01.json"));
   RecordingData legacy=RecordingIO.Parse(csv,json);
   Check(legacy.Sensitivity==1.25&&legacy.WeaponName=="AK47"&&!legacy.AutomaticIdentity,
    "V0.1 imports manual labels without claiming automatic verification");
   Check(AmcConverter.Points(legacy).Count==30,"Recorded rollback 29->28->29 deduplicates to 30 shots");
   CaptureResult stats=Recorder.Analyze(legacy.Samples,"recorded AK fixture");
   Check(stats.Shots==30&&stats.MissedShotUpdates==0,"Recorder no longer reports 31 shots after rollback");
   AmcResult result=AmcConverter.Convert(legacy,Path.Combine(output,"AK47_FIXTURE_TEST.amc"),1.25);
   Timeline timeline=ParseAmc(result.AmcPath);
   Check(result.Shots==30&&result.MoveCommands==58,"Two estimated steps per 29 shot intervals");
   Check(timeline.X==-130&&timeline.Y==363,"Recorded AK geometry retains -130,+363 endpoint");
   Check(timeline.Release==3006&&timeline.Moves[0][0]==56&&timeline.Moves[57][0]==2906,
    "Game ticks preserve 56ms first move, 2906ms last move and 3006ms release");
   bool timing=true;
   for(int i=1;i<timeline.Moves.Count;i++)timing&=timeline.Moves[i][0]-timeline.Moves[i-1][0]==50;
   Check(timing,"No frame-by-frame micro delays; all AK movement gaps are 50ms");
   List<RecoilPoint> points=AmcConverter.Points(legacy);bool anchors=true;
   foreach(RecoilPoint p in points){
    int at=(int)Math.Round(points[0].ObservedMs+(p.Tick-points[0].Tick)*15.625,MidpointRounding.AwayFromZero);
    int x=0,y=0;foreach(int[] move in timeline.Moves)if(move[0]<=at){x=move[1];y=move[2];}
    anchors&=Math.Abs(x-p.Yaw*2/(1.25*0.022))<=0.500001&&Math.Abs(y+p.Pitch*2/(1.25*0.022))<=0.500001;
   }
   Check(anchors,"Every measured shot anchor is within half a mouse count");
   Check(!result.InstantaneousRecoilVerified&&File.Exists(result.ReportPath),"Report marks interpolation unverified");
   Reject(delegate{AmcConverter.Convert(legacy,result.AmcPath,1.25);},"Existing AMC is never overwritten");
   AmcResult scaled=AmcConverter.Convert(legacy,Path.Combine(output,"AK47_SENS_2.500.amc"),2.5);
   Timeline half=ParseAmc(scaled.AmcPath);
   Check(half.X==-65&&half.Y==181&&half.Release==3006&&half.Tail==30000,
    "Target sensitivity changes geometry while timing and anti-repeat stay fixed");
   double target;
   Check(ConverterForm.TryTarget("1.250",out target)&&target==1.25,"Dot and three decimals accepted");
   Check(!ConverterForm.TryTarget("1,250",out target)&&!ConverterForm.TryTarget("1.25",out target),
    "Comma and incomplete precision rejected");
   Check(!ConverterForm.TryTarget("0.000",out target),"Zero sensitivity rejected");
   Reject(delegate{RecordingIO.Parse(csv,"{\"weapon_label_user_supplied\":\"AK47\"}");},
    "Missing sensitivity cannot be guessed");
   Reject(delegate{RecordingIO.Parse(csv.Replace(",0.4547455,",",NaN,"),json);},
    "NaN in recoil tick rejected");
   RecordingData missing=RecordingIO.Parse(csv,json);
   missing.Samples.RemoveAll(delegate(Sample s){return s.shots_fired==10;});
   Reject(delegate{AmcConverter.Points(missing);},"Missing bullet state cannot be silently interpolated");
   RecordingData moved=RecordingIO.Parse(csv,json);moved.Samples[10].eye_angle.yaw+=0.1F;
   Reject(delegate{AmcConverter.Points(moved);},"Mouse movement contaminating recoil capture rejected");
   RecordingData damaged=RecordingIO.Parse(csv,json);damaged.Samples[10].unpredictable_angle.pitch=0.1F;
   Reject(delegate{AmcConverter.Points(damaged);},"External aim punch rejected");
   RecordingData changed=RecordingIO.Parse(csv,json);changed.Samples[10].weapon_hash++;
   Reject(delegate{AmcConverter.Points(changed);},"Weapon hash change rejected");
   RecordingData automatic=RecordingIO.Parse(csv,json);
   foreach(Sample s in automatic.Samples)s.identity=new GameIdentity {WeaponName="AK47",DesignerName="weapon_ak47",
    ItemDefinitionIndex=7,WeaponHandle=32775,Sensitivity=1.25F,FovSensitivityAdjust=1,PawnMouseSensitivity=1,Ammo=30};
   automatic.Metadata["weapon_identity_automatically_verified"]=true;
   automatic.Metadata["weapon_definition_index"]=7;automatic.Metadata["sensitivity_detected"]=1.25;
   automatic.ResolveMetadata();
   Check(automatic.AutomaticIdentity&&automatic.WeaponDefinitionIndex==7,
    "New recordings use item definition and detected sensitivity");
   automatic.Samples[10].identity.Sensitivity=1.3F;
   Reject(delegate{automatic.ResolveMetadata();},"Sensitivity change within capture rejected");
   automatic.Samples[10].identity.Sensitivity=1.25F;automatic.Samples[10].identity.Scoped=true;
   Reject(delegate{automatic.ResolveMetadata();},"Scoped capture cannot use unscoped conversion");
   automatic.Samples[10].identity.Scoped=false;
   string roundtrip=Path.Combine(output,"AUTOMATIC.csv");RecordingIO.WriteCsv(roundtrip,automatic.Samples);
   RecordingIO.WriteJson(Path.ChangeExtension(roundtrip,".json"),automatic.Metadata);
   RecordingData read=RecordingIO.Load(roundtrip);
   Check(read.AutomaticIdentity&&read.Sensitivity==1.25&&read.WeaponName=="AK47","V0.2.1 CSV/JSON identity roundtrip");
   Check(File.ReadAllLines(roundtrip)[0].Split(',').Length==34,"New CSV includes all seven identity/settings fields");
   automatic.Metadata["weapon_definition_index"]=16;
   Reject(delegate{automatic.ResolveMetadata();},"Disagreement between CSV and JSON weapon rejected");
   automatic.Metadata["weapon_definition_index"]=7;
   string zip=Path.Combine(output,"LEGACY.zip");
   using(ZipArchive archive=ZipFile.Open(zip,ZipArchiveMode.Create)){
    WriteEntry(archive,"nested/record.csv",csv);WriteEntry(archive,"nested/record.json",json);
   }
   Check(AmcConverter.Points(RecordingIO.Load(zip)).Count==30,"Nested ZIP loads paired CSV and JSON without extraction");
   string ambiguous=Path.Combine(output,"AMBIGUOUS.zip");
   using(ZipArchive archive=ZipFile.Open(ambiguous,ZipArchiveMode.Create)){
    WriteEntry(archive,"one.csv",csv);WriteEntry(archive,"one.json",json);
    WriteEntry(archive,"two.csv",csv);WriteEntry(archive,"two.json",json);
   }
   Reject(delegate{RecordingIO.Load(ambiguous);},"ZIP with multiple recordings requires an explicit selection");
   string missingJson=Path.Combine(output,"MISSING_JSON.zip");
   using(ZipArchive archive=ZipFile.Open(missingJson,ZipArchiveMode.Create))WriteEntry(archive,"record.csv",csv);
   Reject(delegate{RecordingIO.Load(missingJson);},"ZIP missing metadata is rejected");
   CultureInfo saved=Thread.CurrentThread.CurrentCulture;
   try{
    Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("it-IT");
    Check(RecordingIO.Parse(csv,json).Sensitivity==1.25,"Import is culture independent in Italian Windows");
   }finally{Thread.CurrentThread.CurrentCulture=saved;}
   Environment.SetEnvironmentVariable("CS2_PROBE_TEST_MODE","1");
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   Preview(new ConverterForm(),Path.Combine(output,"UI_CONVERTER.png"));
   Check(File.Exists(Path.Combine(output,"UI_CONVERTER.png")),"Converter preview fits the compact window");
   Console.WriteLine("PASS: "+count+" conversion and recording regression checks.");
   Console.WriteLine("No CS2 process was opened. Live weapon/sensitivity detection and recoil accuracy need an in-game test.");
   return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
 private static void WriteEntry(ZipArchive archive,string name,string text) {
  using(Stream stream=archive.CreateEntry(name).Open())
  using(StreamWriter writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(text);
 }
}
