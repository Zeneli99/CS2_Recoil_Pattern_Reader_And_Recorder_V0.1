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
 private static Timeline ParseAmc(string path) {return ParseAmc(path,true);}
 private static Timeline ParseAmc(string path,bool checkOutputEncoding) {
  byte[] bytes=File.ReadAllBytes(path);
  if(checkOutputEncoding)Check(bytes[0]==255&&bytes[1]==254,"AMC has UTF-16LE BOM");
  XmlDocument xml=new XmlDocument();xml.Load(path);
  Check(xml.SelectSingleNode("//GUIOption/RepeatType").InnerText=="1","Hold repeat type retained");
  Check(xml.SelectSingleNode("//KeyUp/Syntax").InnerText.Trim()=="LeftUp 1","Release handler lifts left button");
  Timeline t=new Timeline();
  XmlNode comment=xml.SelectSingleNode("//Comment");
  // Independent evaluator: a timing tag is an explicit assumption, not measured elapsed time.
  int moveCost=comment!=null&&comment.InnerText.Contains("MoveRCommandCostMs=1;")?1:0;
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
    t.Time+=moveCost;t.X+=x;t.Y+=y;t.Commands++;t.Moves.Add(new int[] {t.Time,t.X,t.Y});
   } else throw new Exception("Unknown AMC command");
  }
  Check(!t.Left&&t.Tail==30000,"Fixed anti-repeat tail is 30000ms after left-up");return t;
 }
 private static void Preview(Form form,string output,string amcPath=null) {
  using(form){
   form.Show();Application.DoEvents();
   if(amcPath!=null) {
    System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    typeof(ConverterForm).GetMethod("LoadRecording",flags).Invoke(form,new object[] {amcPath});
    Stopwatch loading=Stopwatch.StartNew();
    while((bool)typeof(ConverterForm).GetField("busy",flags).GetValue(form)&&loading.ElapsedMilliseconds<3000) {
     Application.DoEvents();Thread.Sleep(10);
    }
    string inputField=amcPath.EndsWith(".recoil.json",StringComparison.OrdinalIgnoreCase)?"snapshot":"amc";
    Check(typeof(ConverterForm).GetField(inputField,flags).GetValue(form)!=null&&
     ((Button)typeof(ConverterForm).GetField("convert",flags).GetValue(form)).Enabled,
     "Converter opens "+inputField+" asynchronously and enables export without a running game");
   }
   form.Refresh();Application.DoEvents();
   foreach(Control child in form.Controls)
    if(child.Right>540||child.Bottom>360||child.Left<0||child.Top<0)
     throw new Exception("Control outside compact form");
   using(Bitmap bitmap=new Bitmap(form.Width,form.Height)){
    form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(output);
   }
  }
 }
 private static int[] Position(Timeline t,int at) {
  int x=0,y=0;foreach(int[] move in t.Moves)if(move[0]<=at){x=move[1];y=move[2];}
  return new int[] {x,y};
 }
 private static void ProvidedAmcChecks(string fixtures,string output) {
  string source=Path.Combine(fixtures,"AK47_ORIGINAL_50MS_V022.amc");
  AmcInput input=AmcInput.Load(source);Timeline original=ParseAmc(source,false);
  AmcResult result=AmcConverter.Smooth(input,Path.Combine(output,"AK47_SENS_1.250_SMOOTH_10MS.amc"),1.25);
  Timeline smooth=ParseAmc(result.AmcPath);
  Check(original.Commands==58&&smooth.Commands==286,
   "Provided AK AMC converts 58 original moves into 286 moderate steps");
  bool anchors=true,timing=true;int maxDelta=0,px=0,py=0;
  foreach(int[] anchor in original.Moves) {
   int[] p=Position(smooth,anchor[0]);anchors&=p[0]==anchor[1]&&p[1]==anchor[2];
  }
  for(int i=0;i<smooth.Moves.Count;i++) {
   int[] move=smooth.Moves[i];maxDelta=Math.Max(maxDelta,Math.Max(Math.Abs(move[1]-px),Math.Abs(move[2]-py)));
   if(i>0)timing&=move[0]-smooth.Moves[i-1][0]>=10;
   px=move[1];py=move[2];
  }
  Check(anchors,"All 58 provided AMC anchors retain their exact position and timestamp");
  Check(timing&&maxDelta<=9,"Provided AMC uses at least 10ms gaps and at most 9-count jumps");
  Check(smooth.Moves[0][0]==52&&smooth.Moves[smooth.Moves.Count-1][0]==2902&&
   smooth.Release==3002&&smooth.X== -130&&smooth.Y==363&&smooth.Tail==30000,
   "Provided AMC preserves first move, last anchor, release, total X/Y and anti-repeat");
 }
 private static string SyntheticAmc() {
  List<string> commands=new List<string> {"LeftDown 1","Delay 52 ms","MoveR -2 4",
   "Delay 50 ms","MoveR -2 3","Delay 50 ms","MoveR 43 -17",
   "Delay 50 ms","MoveR -43 0","Delay 13 ms","MoveR -1 1",
   "Delay 29 ms","MoveR 12 -1","Delay 50 ms","MoveR -12 1",
   "Delay 100 ms","LeftUp 1"};
  for(int i=0;i<30;i++)commands.Add("Delay 999 ms");commands.Add("Delay 30 ms");
  return "<?xml version=\"1.0\" encoding=\"UTF-16\"?><Root><DefaultMacro>"+
   "<Major/><Description>AK47 · SENS 1.250 · PROVA NON VALIDATA IN GIOCO</Description>"+
   "<Comment>Synthetic rounding, reversal and timing fixture.</Comment>"+
   "<GUIOption><RepeatType>1</RepeatType></GUIOption><KeyUp><Syntax>LeftUp 1\n</Syntax></KeyUp>"+
   "<KeyDown><Syntax>"+String.Join("\n",commands.ToArray())+"\n</Syntax></KeyDown>"+
   "<Software>Counter-Strike 2</Software></DefaultMacro></Root>";
 }
 private static void AmcSmoothingChecks(string output) {
  string input=Path.Combine(output,"SYNTHETIC_ORIGINAL.amc"),text=SyntheticAmc();
  File.WriteAllText(input,text,Encoding.Unicode);
  AmcInput data=AmcInput.Load(input);Timeline before=ParseAmc(input);
  Check(data.WeaponName=="AK47"&&data.Sensitivity==1.25&&data.MoveCommands==7,
   "AMC importer reads source sensitivity and timed cumulative positions");
  AmcResult result=AmcConverter.Smooth(data,Path.Combine(output,"SYNTHETIC_SMOOTH.amc"),1.25);
  Timeline after=ParseAmc(result.AmcPath);
  bool anchors=true,small=true,direction=true;
  int previousTime=before.Moves[0][0],startX=before.Moves[0][1],startY=before.Moves[0][2];
  foreach(int[] anchor in before.Moves) {
   int[] p=Position(after,anchor[0]);anchors&=p[0]==anchor[1]&&p[1]==anchor[2];
   int px=startX,py=startY;
   foreach(int[] move in after.Moves) {
    if(move[0]<=previousTime||move[0]>anchor[0])continue;
    direction&=(anchor[1]>=startX?move[1]>=px:move[1]<=px)&&
     (anchor[2]>=startY?move[2]>=py:move[2]<=py);
    px=move[1];py=move[2];
   }
   previousTime=anchor[0];startX=anchor[1];startY=anchor[2];
  }
  for(int i=1;i<after.Moves.Count;i++)small&=after.Moves[i][0]-after.Moves[i-1][0]>=5;
  Check(anchors,"AMC smoothing preserves every original cumulative anchor at its exact original time");
  Check(direction,"Imported AMC direction reversals and zero-axis plateaus are preserved");
  Check(small&&after.Commands>before.Commands,"Irregular AMC gaps use moderate steps without 1ms bursts");
  int maxDelta=0,oldX=0,oldY=0;
  foreach(int[] move in after.Moves){maxDelta=Math.Max(maxDelta,Math.Max(Math.Abs(move[1]-oldX),Math.Abs(move[2]-oldY)));oldX=move[1];oldY=move[2];}
  Check(maxDelta<=9,"Original 43-count jumps become at most 9-count steps");
  Check(after.Moves[0][0]==52&&after.Release==394&&after.X==before.X&&after.Y==before.Y,
   "Imported AMC initial delay, release and total geometry are unchanged");
  Check(result.SourceKind=="AMC"&&result.Shots==0&&!result.WeaponAndSensitivityAutomaticallyRead,
   "AMC import does not invent shot counts or claim current game-memory verification");
  AmcResult second=AmcConverter.Smooth(AmcInput.Load(result.AmcPath),
   Path.Combine(output,"SYNTHETIC_SMOOTH_TWICE.amc"),1.25);
  Timeline twice=ParseAmc(second.AmcPath);
  bool repeat=after.Moves.Count==twice.Moves.Count;
  for(int i=0;repeat&&i<after.Moves.Count;i++)for(int c=0;c<3;c++)repeat&=after.Moves[i][c]==twice.Moves[i][c];
  Check(repeat&&twice.Release==after.Release,"Reimporting a smooth AMC adds no further resampling or geometry drift");
  AmcResult scaled=AmcConverter.Smooth(data,Path.Combine(output,"SYNTHETIC_SMOOTH_SENS_2.500.amc"),2.5);
  Timeline half=ParseAmc(scaled.AmcPath);bool scaledAnchors=true;
  foreach(int[] anchor in before.Moves) {
   int[] p=Position(half,anchor[0]);
   scaledAnchors&=p[0]==(int)Math.Round(anchor[1]/2.0,MidpointRounding.AwayFromZero)&&
    p[1]==(int)Math.Round(anchor[2]/2.0,MidpointRounding.AwayFromZero);
  }
  Check(scaledAnchors&&half.Release==before.Release,"AMC sensitivity conversion scales cumulative anchors without altering timing");
  Reject(delegate{AmcConverter.Smooth(data,result.AmcPath,1.25);},"AMC smoothing refuses to overwrite its existing output");
  Reject(delegate{AmcConverter.Smooth(data,Path.Combine(output,"INVALID_SENS.amc"),0);},"AMC smoothing rejects invalid sensitivity");
  string bad=Path.Combine(output,"INVALID_INPUT.amc");
  File.WriteAllText(bad,text.Replace("SENS 1.250","SENS missing"),Encoding.Unicode);
  Reject(delegate{AmcInput.Load(bad);},"AMC with missing source sensitivity is rejected");
  File.WriteAllText(bad,text.Replace("MoveR 43 -17","KeyDown 42"),Encoding.Unicode);
  Reject(delegate{AmcInput.Load(bad);},"Unsupported AMC commands are rejected rather than discarded");
  File.WriteAllText(bad,text.Replace("MoveR 43 -17","MoveR 128 -17"),Encoding.Unicode);
  Reject(delegate{AmcInput.Load(bad);},"Out-of-range AMC deltas are rejected");
  File.WriteAllText(bad,text.Replace("RepeatType>1","RepeatType>2"),Encoding.Unicode);
  Reject(delegate{AmcInput.Load(bad);},"AMC import requires the original hold-repeat mode");
  File.WriteAllText(bad,text.Replace("Delay 30 ms","Delay 29 ms"),Encoding.Unicode);
  Reject(delegate{AmcInput.Load(bad);},"AMC import requires the original complete 30000ms anti-repeat tail");
  File.WriteAllText(bad,text.Replace("<Root>","<!DOCTYPE Root [<!ENTITY bad 'data'>]><Root>"),Encoding.Unicode);
  Reject(delegate{AmcInput.Load(bad);},"AMC XML external or declared entities are prohibited");
  File.WriteAllText(bad,text.Replace("<Software>Counter-Strike 2","<Software>Other"),Encoding.Unicode);
  Reject(delegate{AmcInput.Load(bad);},"AMC import reports incompatible software");
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
 private static void SetupIdentity(IntPtr address,IntPtr entity,uint handle,
  IntPtr previous,IntPtr next,uint flags) {
  Marshal.Copy(new byte[0x70],0,address,0x70);
  Marshal.WriteIntPtr(address,0,entity);
  Marshal.WriteInt32(address,Layout.EntityReferenceHandle,
   unchecked((int)(handle+((flags&1U)<<15))));
  Marshal.WriteInt32(address,Layout.EntityFlags,unchecked((int)flags));
  Marshal.WriteIntPtr(address,Layout.EntityPrevious,previous);
  Marshal.WriteIntPtr(address,Layout.EntityNext,next);
 }
 private static void StartupDiagnostics(string output) {
  List<IntPtr> allocations=new List<IntPtr>();
  try {
   IntPtr client=Allocate(allocations,0x2800000),pawn=Allocate(allocations,0x3000);
   IntPtr weapons=Allocate(allocations,0x100),sensitivity=Allocate(allocations,0x100);
   IntPtr engine=Allocate(allocations,0x920000),network=Allocate(allocations,0x400);
   IntPtr rules=Allocate(allocations,0x200);
   IntPtr root=Allocate(allocations,0x70),decoy=Allocate(allocations,0x70),target=Allocate(allocations,0x70);
   IntPtr weapon=Allocate(allocations,0x2000),name=Marshal.StringToHGlobalAnsi("weapon_ak47");
   allocations.Add(name);
   const uint handle=(3U<<15)|1200U,wrongSerial=(6U<<15)|1200U;
   SetupIdentity(root,pawn,(2U<<15)|65U,IntPtr.Zero,decoy,0);
   SetupIdentity(decoy,IntPtr.Zero,wrongSerial,root,target,0);
   SetupIdentity(target,weapon,handle,decoy,IntPtr.Zero,1);
   Marshal.WriteIntPtr(pawn,Layout.EntityIdentity,root);
   Marshal.WriteIntPtr(pawn,Layout.WeaponServices,weapons);
   Marshal.WriteInt32(weapons,Layout.ActiveWeapon,unchecked((int)handle));
   Marshal.WriteIntPtr(client,Layout.Sensitivity,sensitivity);
   // Reproduce the reported dump-global failure without using any user process addresses.
   Marshal.WriteInt64(client,0x2715828,1L);
   Marshal.WriteIntPtr(target,Layout.DesignerName,name);
   Marshal.WriteIntPtr(weapon,Layout.EntityIdentity,target);
   Marshal.WriteInt16(weapon,Layout.AttributeManager+Layout.ItemView+Layout.ItemDefinitionIndex,7);
   Marshal.WriteInt32(weapon,Layout.WeaponClip,30);
   PutFloat(sensitivity,Layout.SensitivityValue,1.234567F);
   PutFloat(pawn,Layout.PawnMouseSensitivity,1F);
   PutFloat(pawn,Layout.FovSensitivityAdjust,1F);Marshal.WriteByte(pawn,Layout.Scoped,0);
   int pid;using(Process self=Process.GetCurrentProcess()){pid=self.Id;}
   using(ReadMemory memory=new ReadMemory(pid)) {
    GameIdentity info=IdentityReader.Read(memory,client.ToInt64(),pawn.ToInt64(),null);
    Check(info.WeaponName=="AK47"&&info.ItemDefinitionIndex==7&&info.DesignerName=="weapon_ak47"&&info.Ammo==30,
     "Weapon detection succeeds when the old entity-list global reads 0x1");
    Check(info.WeaponAddress==weapon.ToInt64(),"Same entity index with another serial is skipped");
    Check(info.WeaponHandle==handle,"Entity reference validates serial with the invalid-handle flag adjustment");
    Check(info.Sensitivity==1.234567F,"Native sensitivity read preserves the original float precision");
    Marshal.WriteIntPtr(root,Layout.EntityNext,IntPtr.Zero);
    Marshal.WriteIntPtr(root,Layout.EntityPrevious,target);
    Marshal.WriteIntPtr(target,Layout.EntityPrevious,IntPtr.Zero);
    Marshal.WriteIntPtr(target,Layout.EntityNext,root);
    Check(IdentityReader.Resolve(memory,pawn.ToInt64(),handle)==weapon.ToInt64(),
     "An active weapon before the pawn is found through the previous identity links");
    Marshal.WriteIntPtr(root,Layout.EntityPrevious,IntPtr.Zero);
    Marshal.WriteIntPtr(root,Layout.EntityNext,decoy);
    Marshal.WriteIntPtr(target,Layout.EntityPrevious,decoy);
    Marshal.WriteIntPtr(target,Layout.EntityNext,IntPtr.Zero);
    Marshal.WriteInt32(target,Layout.EntityReferenceHandle,unchecked((int)(wrongSerial+(1U<<15))));
    Reject(delegate{IdentityReader.Read(memory,client.ToInt64(),pawn.ToInt64(),info);},
     "A cached weapon with a recycled reference is rejected");
    Reject(delegate{IdentityReader.Resolve(memory,pawn.ToInt64(),handle);},
     "No entity with the full active handle produces an explicit lookup failure");
    Marshal.WriteInt32(target,Layout.EntityReferenceHandle,unchecked((int)(handle+(1U<<15))));
    Marshal.WriteIntPtr(target,Layout.EntityPrevious,root);
    Reject(delegate{IdentityReader.Resolve(memory,pawn.ToInt64(),handle);},
     "An inconsistent previous/next link is rejected instead of selecting an arbitrary weapon");
    Marshal.WriteIntPtr(target,Layout.EntityPrevious,decoy);
    Marshal.WriteIntPtr(root,Layout.EntityPrevious,decoy);
    Marshal.WriteIntPtr(decoy,Layout.EntityNext,root);
    Stopwatch cycle=Stopwatch.StartNew();
    Reject(delegate{IdentityReader.Resolve(memory,pawn.ToInt64(),handle);},
     "A cyclic identity chain fails without hanging the recorder");
    Check(cycle.ElapsedMilliseconds<1000,"Cycle detection exits before the lookup timeout");
    Marshal.WriteIntPtr(root,Layout.EntityPrevious,IntPtr.Zero);
    Marshal.WriteIntPtr(decoy,Layout.EntityNext,target);
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

 private static List<Sample> ExecutionSamples(AmcInput expected,double lag,double duration,
  double gainX,double gainY,double end,bool quantize) {
  List<Sample> samples=new List<Sample>();int index=0,x=0,y=0;
  for(double at= -2;at<=end;at+=2) {
   while(index<expected.Anchors.Count&&lag+duration*expected.Anchors[index].Time<=at) {
    x=expected.Anchors[index].X;y=expected.Anchors[index].Y;index++;
   }
   double actualX=x*gainX,actualY=y*gainY;
   if(quantize){actualX=Math.Round(actualX);actualY=Math.Round(actualY);}
   double yaw=179.5-actualX*1.25*0.022;
   if(yaw>180)yaw-=360;if(yaw< -180)yaw+=360;
   Vector measured=new Vector {pitch=(float)(10+actualY*1.25*0.022),yaw=(float)yaw,roll=0};
   samples.Add(new Sample {observed_ms=at,left_down=at>=0&&at<end,
    eye_angle=new Vector {pitch=measured.pitch,yaw=measured.yaw,roll=0},
    input_angle=measured});
  }
  return samples;
 }
 private static void ExecutionChecks(string output) {
  AmcInput macro=new AmcInput {SourcePath="AK47_EXAMPLE.amc",WeaponName="AK47",
   Sensitivity=1.25,ReleaseTime=2500,TailMs=30000,MoveCommands=9};
  int[][] values=new int[][] {
   new int[] {50,-4,8},new int[] {120,-22,45},new int[] {270,15,90},
   new int[] {410,80,130},new int[] {635,-35,170},new int[] {820,110,225},
   new int[] {1135,-140,285},new int[] {1680,45,355},new int[] {2100,-130,363}
  };
  foreach(int[] p in values)macro.Anchors.Add(new MouseAnchor {Time=p[0],X=p[1],Y=p[2]});
  List<Sample> samples=ExecutionSamples(macro,18,1.08,0.92,1.12,2450,true);
  ExecutionCheckResult result=AmcExecutionCheck.Analyze(samples,macro,1.25,"synthetic macro execution");
  Check(Math.Abs(result.LagMs-18)<=2.1&&Math.Abs(result.DurationFactor-1.08)<=0.0021,
   "AMC execution comparison recovers known delay and duration from quantized, 2ms samples");
  Check(Math.Abs(result.HorizontalGain-0.92)<0.003&&Math.Abs(result.VerticalGain-1.12)<0.003,
   "Independent horizontal and vertical gains are measured through yaw wraparound");
  Check(result.CompleteTimelineObserved&&!result.SearchBoundaryReached&&result.RootMeanSquareErrorCounts<1,
   "Complete execution has a small measured residual under the declared input-angle assumptions");
  Check(!result.InstantaneousBulletRecoilVerified&&!result.RawMouseCountsDirectlyRead,
   "Execution fit never claims direct raw-input or bullet-direction verification");
  Check(result.AngleSource=="C_BasePlayerPawn.v_angle",
   "AMC execution comparison prefers the directly sampled player input angle");
  Check(result.ExpectedAnchors.Count==9&&result.ExpectedReleaseMs==2500&&result.AntiRepeatMs==30000&&
   result.Trace.Count>100&&result.SamplesCompared==samples.Count&&result.MedianObservationGapMs==2,
   "Execution report retains the selected AMC timeline and measured comparison trace");
  RecordingIO.WriteJson(Path.Combine(output,"EXECUTION_SYNTHETIC.execution.json"),result);
  List<Sample> exact=ExecutionSamples(macro,0,1,1,1,2400,false);
  ExecutionCheckResult identity=AmcExecutionCheck.Analyze(exact,macro,1.25,"identity execution");
  Check(Math.Abs(identity.LagMs)<=2&&Math.Abs(identity.DurationFactor-1)<=0.002&&
   Math.Abs(identity.HorizontalGain-1)<0.0001&&Math.Abs(identity.VerticalGain-1)<0.0001,
   "Matching AMC execution does not invent gain or a changed duration");
  List<Sample> stopped=ExecutionSamples(macro,18,1.08,0.92,1.12,1900,true);
  Check(!AmcExecutionCheck.Analyze(stopped,macro,1.25,"early release").CompleteTimelineObserved,
   "Early release is reported as an incomplete comparison");
  List<Sample> idle=ExecutionSamples(macro,18,1.08,0,0,2450,false);
  Reject(delegate{AmcExecutionCheck.Analyze(idle,macro,1.25,"no macro");},
   "No recorded movement cannot be fitted as a successful AMC execution");
  List<Sample> noBaseline=ExecutionSamples(macro,18,1.08,0.92,1.12,2450,true);
  noBaseline.RemoveAt(0);
  Reject(delegate{AmcExecutionCheck.Analyze(noBaseline,macro,1.25,"no baseline");},
   "Execution measurement requires a baseline before the click");
  List<Sample> unsorted=ExecutionSamples(macro,18,1.08,0.92,1.12,2450,true);
  unsorted[30].observed_ms= -3;
  Reject(delegate{AmcExecutionCheck.Analyze(unsorted,macro,1.25,"time reversal");},
   "Unordered execution samples are rejected");
  GameIdentity equipped=new GameIdentity {WeaponName="AK47",Sensitivity=1.25F};
  AmcExecutionCheck.ValidateIdentity(macro,equipped);
  equipped.Sensitivity=2.5F;
  Reject(delegate{AmcExecutionCheck.ValidateIdentity(macro,equipped);},
   "Execution test requires the selected AMC sensitivity in the game");
  equipped.Sensitivity=1.25F;equipped.WeaponName="M4A4";
  Reject(delegate{AmcExecutionCheck.ValidateIdentity(macro,equipped);},
   "Execution test requires the selected AMC weapon");
  equipped.WeaponName="AK47";equipped.Scoped=true;
  Reject(delegate{AmcExecutionCheck.ValidateIdentity(macro,equipped);},
   "Scoped execution cannot be compared against an unscoped AMC");
  Environment.SetEnvironmentVariable("CS2_PROBE_TEST_MODE","1");
  using(RecorderForm form=new RecorderForm()) {
   System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
   System.Reflection.MethodInfo mode=typeof(RecorderForm).GetMethod("SetExecutionTest",flags);
   mode.Invoke(form,new object[] {macro});
   Check(((Button)typeof(RecorderForm).GetField("record",flags).GetValue(form)).Text=="PROVA AMC · F8"&&
    !((CheckBox)typeof(RecorderForm).GetField("autoAmc",flags).GetValue(form)).Enabled,
    "Test mode identifies F8 as an AMC test and disables automatic recoil export");
   mode.Invoke(form,new object[] {null});
   Check(((CheckBox)typeof(RecorderForm).GetField("autoAmc",flags).GetValue(form)).Enabled&&
    ((Button)typeof(RecorderForm).GetField("record",flags).GetValue(form)).Text=="ARMA / FERMA · F8",
    "Leaving AMC test restores the regular recorder controls");
  }
 }
 private static void RecoilDynamicsChecks() {
  const double exponential=8.6,linear=19.2,velocityDecay=3.6;
  List<RecoilPoint> points=new List<RecoilPoint>();
  RecoilPoint first=new RecoilPoint {Tick=1000,ObservedMs=0,Pitch=0,Yaw=0,
   PitchVelocity= -20,YawVelocity= -10,Shot=1};
  points.Add(first);
  for(int shot=2;shot<=20;shot++) {
   RecoilPoint previous=points[points.Count-1];double pitch,yaw;
   RecoilDynamics.EvaluateRaw(previous,0.1,exponential,linear,velocityDecay,out pitch,out yaw);
   points.Add(new RecoilPoint {Tick=previous.Tick+6.4,ObservedMs=(shot-1)*100,
    Pitch=pitch,Yaw=yaw,PitchVelocity= -18-shot*1.7+(shot%3)*4.2,
    YawVelocity= -12+shot*0.9-(shot%4)*3.1,Shot=shot});
  }
  RecoilDynamicsFit fit=RecoilDynamics.Fit(points);
  Check(Math.Abs(fit.ExponentialDecayPerSecond-exponential)<0.03&&
   Math.Abs(fit.LinearDecayDegreesPerSecond-linear)<0.05&&
   Math.Abs(fit.VelocityDecayPerSecond-velocityDecay)<0.04,
   "Internal recoil-state fit recovers known angle and velocity dynamics");
  Check(fit.RootMeanSquareResidualDegrees<0.0005&&fit.MaximumResidualDegrees<0.001,
   "Internal recoil-state fit retains a sub-millidegree synthetic residual");
  double startPitch,startYaw,endPitch,endYaw;
  RecoilDynamics.EvaluateCorrected(points[4],points[5],0,fit,out startPitch,out startYaw);
  RecoilDynamics.EvaluateCorrected(points[4],points[5],1,fit,out endPitch,out endYaw);
  Check(Math.Abs(startPitch-points[4].Pitch)<0.0000001&&Math.Abs(startYaw-points[4].Yaw)<0.0000001&&
   Math.Abs(endPitch-points[5].Pitch)<0.0000001&&Math.Abs(endYaw-points[5].Yaw)<0.0000001,
   "Dynamics replay keeps both recorded shot anchors exact");
 }
 private static void ObservedReleaseChecks(RecordingData legacy,string output) {
  RecordingData released=RecordingData.FromSamples(new List<Sample>(legacy.Samples),
   new Dictionary<string,object>(legacy.Metadata));
  Sample last=legacy.Samples[legacy.Samples.Count-1];
  released.Samples.Add(new Sample {
   observed_ms=3282.965,left_down=false,shots_fired=last.shots_fired,
   predictable_tick=last.predictable_tick,predictable_tick_fraction=last.predictable_tick_fraction,
   predictable_angle=last.predictable_angle,predictable_velocity=last.predictable_velocity,
   unpredictable_angle=last.unpredictable_angle,eye_angle=last.eye_angle,weapon_hash=last.weapon_hash
  });
  AmcResult result=AmcConverter.Convert(released,Path.Combine(output,"OBSERVED_RELEASE.amc"),1.25);
  Timeline timeline=ParseAmc(result.AmcPath);
  Check(timeline.Release==3283&&result.ReleaseTimingSource=="Observed left-button release"&&
   timeline.X== -130&&timeline.Y==363&&timeline.Tail==30000,
   "CSV conversion retains actual click release without changing geometry or the anti-repeat tail");
  released.Metadata["capture_mode"]="AMC_EXECUTION_TEST";
  Reject(delegate{AmcConverter.Points(released);},
   "AMC execution recordings cannot be silently converted into a reference recoil pattern");
 }

 [STAThread] private static int Main(string[] args) {
  try{
   if(args.Length!=2)throw new Exception("Usage: CoreChecks.exe FIXTURE_DIRECTORY OUTPUT_DIRECTORY");
   string fixtures=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);
   Directory.CreateDirectory(output);
   LayoutChecks.Run(fixtures,Check);
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   StartupDiagnostics(output);
   RecoilDynamicsChecks();
   string csv=File.ReadAllText(Path.Combine(fixtures,"AK47_30_SHOTS_V01.csv"));
   string json=File.ReadAllText(Path.Combine(fixtures,"AK47_30_SHOTS_V01.json"));
   RecordingData legacy=RecordingIO.Parse(csv,json);
   AmcTimingChecks.Run(output,Check,Reject);
   NoFireChecks.Run(output,legacy,Check,Reject);
   Check(legacy.Sensitivity==1.25&&legacy.WeaponName=="AK47"&&!legacy.AutomaticIdentity,
    "V0.1 imports manual labels without claiming automatic verification");
   Check(AmcConverter.Points(legacy).Count==30,"Recorded rollback 29->28->29 deduplicates to 30 shots");
   CaptureResult stats=Recorder.Analyze(legacy.Samples,"recorded AK fixture");
   Check(stats.Shots==30&&stats.MissedShotUpdates==0,"Recorder no longer reports 31 shots after rollback");
   AmcResult result=AmcConverter.Convert(legacy,Path.Combine(output,"AK47_FIXTURE_TEST.amc"),1.25);
   Timeline timeline=ParseAmc(result.AmcPath);
   Check(result.Shots==30&&result.MoveCommands>58&&result.MoveCommands<=400&&result.SmoothingStepMs==10,
    "Internal recoil dynamics emit moderate movement without generating 1ms steps");
   Check(result.Dynamics!=null&&result.DeterministicRecoilStateDirectlyRead&&
    !result.BallisticTrajectoryDirectlyRead&&!result.ServerSpreadIncluded&&
    result.SourceKind=="CS2_INTERNAL_RECOIL_STATE",
    "Report distinguishes direct deterministic recoil state from server trajectory and spread");
   Check(Math.Abs(result.Dynamics.ExponentialDecayPerSecond-8.582)<0.03&&
    Math.Abs(result.Dynamics.LinearDecayDegreesPerSecond-19.223)<0.05&&
    Math.Abs(result.Dynamics.VelocityDecayPerSecond-3.595)<0.03&&
    result.Dynamics.RootMeanSquareResidualDegrees<0.003&&result.Dynamics.MaximumResidualDegrees<0.012,
    "Recorded AK fixture produces a precise self-fitted internal recoil model");
   Check(timeline.X==-130&&timeline.Y==363,"Recorded AK geometry retains -130,+363 endpoint");
   Check(timeline.Release==3006&&timeline.Moves[0][0]>=6&&timeline.Moves[0][0]<=40&&
    timeline.Moves[timeline.Moves.Count-1][0]==2906,
    "Internal replay preserves the 2906ms last anchor and 3006ms release");
   bool timing=true;
   for(int i=1;i<timeline.Moves.Count;i++)timing&=timeline.Moves[i][0]-timeline.Moves[i-1][0]>=5;
   Check(timing,"AK movement remains moderately spaced rather than 1ms micro steps");
   List<RecoilPoint> points=AmcConverter.Points(legacy);bool anchors=true;
   foreach(RecoilPoint p in points){
    int at=(int)Math.Round(points[0].ObservedMs+(p.Tick-points[0].Tick)*15.625,MidpointRounding.AwayFromZero);
    int x=0,y=0;foreach(int[] move in timeline.Moves)if(move[0]<=at){x=move[1];y=move[2];}
    anchors&=Math.Abs(x-p.Yaw*2/(1.25*0.022))<=0.500001&&Math.Abs(y+p.Pitch*2/(1.25*0.022))<=0.500001;
   }
   Check(anchors,"Every measured shot anchor is within half a mouse count");
   bool monotonic=true,nonLinear=false;
   for(int i=1;i<points.Count;i++) {
    RecoilPoint a=points[i-1],b=points[i];
    int from=(int)Math.Round(points[0].ObservedMs+(a.Tick-points[0].Tick)*15.625,
     MidpointRounding.AwayFromZero);
    int to=(int)Math.Round(points[0].ObservedMs+(b.Tick-points[0].Tick)*15.625,
     MidpointRounding.AwayFromZero);
    int previousX=(int)Math.Round(a.Yaw*2/(1.25*0.022),MidpointRounding.AwayFromZero);
    int previousY=(int)Math.Round(-a.Pitch*2/(1.25*0.022),MidpointRounding.AwayFromZero);
    foreach(int[] move in timeline.Moves) {
     if(move[0]<=from||move[0]>to)continue;
     monotonic&=(b.Yaw>=a.Yaw?move[1]>=previousX:move[1]<=previousX)&&
      (b.Pitch<=a.Pitch?move[2]>=previousY:move[2]<=previousY);
     double fraction=(move[0]-from)/(double)(to-from);
     int linearX=(int)Math.Round((a.Yaw+(b.Yaw-a.Yaw)*fraction)*2/(1.25*0.022),
      MidpointRounding.AwayFromZero);
     int linearY=(int)Math.Round(-(a.Pitch+(b.Pitch-a.Pitch)*fraction)*2/(1.25*0.022),
      MidpointRounding.AwayFromZero);
     nonLinear|=move[1]!=linearX||move[2]!=linearY;previousX=move[1];previousY=move[2];
    }
   }
   Check(monotonic,"Each shot interval is monotonic and cannot reintroduce small up/down or left/right shakes");
   Check(nonLinear,"Intermediate AMC positions use the fitted recoil dynamics instead of the old straight line");
   AmcSmoothingChecks(output);
   ExecutionChecks(output);
   ObservedReleaseChecks(legacy,output);
   ProvidedAmcChecks(fixtures,output);
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
   Check(read.AutomaticIdentity&&read.Sensitivity==1.25&&read.WeaponName=="AK47","V0.2.5 direct-state CSV/JSON identity roundtrip");
   Check(File.ReadAllLines(roundtrip)[0].Split(',').Length==45,
    "New CSV includes input, camera and weapon recoil state in addition to identity/settings fields");
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
   Preview(new ConverterForm(),Path.Combine(output,"UI_CONVERTER.png"));
   Preview(new ConverterForm(),Path.Combine(output,"UI_CONVERTER_AMC.png"),
    Path.Combine(fixtures,"AK47_ORIGINAL_50MS_V022.amc"));
   Preview(new ConverterForm(),Path.Combine(output,"UI_CONVERTER_NO_FIRE.png"),
    Path.Combine(output,"AK47_NO_FIRE_SYNTHETIC_TEST.recoil.json"));
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
