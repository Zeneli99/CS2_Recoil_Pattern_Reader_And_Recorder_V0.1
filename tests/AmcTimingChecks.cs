using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using RecoilProbe;

internal static class AmcTimingChecks {
 private sealed class Trace {
  internal int Time,DelaySum,X,Y,Release=-1,Tail,MoveCount,MinimumGap=Int32.MaxValue;
  internal readonly List<int[]> Positions=new List<int[]>();
 }
 // Independently evaluate emitted commands, with the explicitly assumed MoveR cost.
 private static Trace Evaluate(List<string> commands,int cost) {
  Trace trace=new Trace();int previousMove=0;
  foreach(string line in commands) {
   string[] fields=line.Split(' ');
   if(fields[0]=="Delay") {
    int delay=Int32.Parse(fields[1],CultureInfo.InvariantCulture);
    if(delay<1||delay>999)throw new Exception("Invalid emitted delay");
    trace.Time+=delay;trace.DelaySum+=delay;
    if(trace.Release>=0)trace.Tail+=delay;
   } else if(fields[0]=="MoveR") {
    int x=Int32.Parse(fields[1],CultureInfo.InvariantCulture),y=Int32.Parse(fields[2],CultureInfo.InvariantCulture);
    if(Math.Abs(x)>127||Math.Abs(y)>127)throw new Exception("Invalid emitted movement");
    trace.Time+=cost;trace.X+=x;trace.Y+=y;trace.MoveCount++;
    trace.MinimumGap=Math.Min(trace.MinimumGap,trace.Time-previousMove);previousMove=trace.Time;
    trace.Positions.Add(new int[] {trace.Time,trace.X,trace.Y});
   } else if(fields[0]=="LeftUp")trace.Release=trace.Time;
  }
  return trace;
 }
 internal static void Run(string output,Action<bool,string> check,Action<Action,string> reject) {
  AmcResult result=new AmcResult {Weapon="AK47",SourceSensitivity=1.25,TargetSensitivity=1.25,
   MoveRCommandCostMs=1,CommandTimingConvention=AmcConverter.CompensatedTimingConvention};
  List<string> commands=new List<string> {"LeftDown 1"};int lastTime=0;
  AmcConverter.ScheduledMove(commands,10,-1,2,ref lastTime,result);
  AmcConverter.ScheduledMove(commands,20,2,3,ref lastTime,result);
  AmcConverter.ScheduledMove(commands,40,-3,-1,ref lastTime,result);
  check(String.Join("|",commands.ToArray())=="LeftDown 1|Delay 9 ms|MoveR -1 2|Delay 9 ms|MoveR 2 3|Delay 19 ms|MoveR -3 -1",
   "Golden AMC command stream compensates 1ms once per MoveR, including a 20ms gap");
  Trace golden=Evaluate(commands,1);
  check(golden.Positions[0][0]==10&&golden.Positions[1][0]==20&&golden.Positions[2][0]==40&&
   golden.X==-2&&golden.Y==4,"Command timing compensation preserves the scheduled times and exact X/Y path");

  List<string> longCommands=new List<string> {"LeftDown 1"};
  AmcResult longResult=new AmcResult {MoveRCommandCostMs=1};lastTime=0;
  for(int i=1;i<=300;i++)AmcConverter.ScheduledMove(longCommands,i*10,i%2==0?1:-1,2,ref lastTime,longResult);
  Trace longTrace=Evaluate(longCommands,1);bool aligned=true;
  for(int i=0;i<longTrace.Positions.Count;i++)aligned&=longTrace.Positions[i][0]==(i+1)*10;
  check(aligned&&longTrace.Time==3000&&longTrace.DelaySum==2700&&longTrace.MinimumGap==10&&longTrace.Y==600,
   "300 commands keep a 10ms cadence without accumulating 300ms of command-cost drift");

  List<string> split=new List<string>();AmcResult splitResult=new AmcResult {MoveRCommandCostMs=1};lastTime=0;
  AmcConverter.ScheduledMove(split,10,300,-130,ref lastTime,splitResult);Trace splitTrace=Evaluate(split,1);
  check(splitTrace.MoveCount==3&&splitTrace.Time==10&&splitTrace.DelaySum==7&&splitTrace.X==300&&splitTrace.Y==-130,
   "Large raw deltas account for every split MoveR while preserving the final scheduled anchor");
  List<string> legacy=new List<string>();AmcResult legacyResult=new AmcResult();lastTime=0;
  AmcConverter.ScheduledMove(legacy,10,-1,2,ref lastTime,legacyResult);
  check(String.Join("|",legacy.ToArray())=="Delay 10 ms|MoveR -1 2","Legacy delay-only convention remains explicit and unchanged");

  AmcConverter.Delay(commands,50-golden.Time);commands.Add("LeftUp 1");AmcConverter.Delay(commands,30000);
  result.ActiveDurationMs=50;result.TotalX=-2;result.TotalY=4;
  string file=Path.Combine(output,"AMC_TIMING_GOLDEN.amc");
  AmcConverter.Save(result,commands,file);
  AmcInput loaded=AmcInput.Load(file);
  check(loaded.MoveRCommandCostMs==1&&loaded.Anchors[0].Time==10&&loaded.Anchors[2].Time==40&&
   loaded.ReleaseTime==50&&loaded.TailMs==30000,"Timing metadata restores intended movement and release timestamps during import");
  AmcResult smooth=AmcConverter.Smooth(loaded,Path.Combine(output,"AMC_TIMING_SMOOTH.amc"),1.25);
  AmcInput reread=AmcInput.Load(smooth.AmcPath);
  bool preserved=reread.ReleaseTime==50&&reread.MoveRCommandCostMs==1;
  foreach(MouseAnchor anchor in loaded.Anchors) {
   int x=0,y=0;foreach(MouseAnchor candidate in reread.Anchors)if(candidate.Time<=anchor.Time){x=candidate.X;y=candidate.Y;}
   preserved&=x==anchor.X&&y==anchor.Y;
  }
  check(preserved,"Smoothing and reimport preserve compensated anchors without applying a second speed-up");
  AmcInput scaled=AmcInput.Load(AmcConverter.Smooth(loaded,Path.Combine(output,"AMC_TIMING_SCALED.amc"),2.5).AmcPath);
  check(scaled.MoveRCommandCostMs==1&&scaled.ReleaseTime==50&&scaled.Anchors[scaled.Anchors.Count-1].X==-1&&
   scaled.Anchors[scaled.Anchors.Count-1].Y==2,"Sensitivity conversion preserves the compensated timeline and cumulative rounding");
  check(!result.CommandTimingMeasured&&!smooth.CommandTimingMeasured,
   "Reports do not claim that the assumed 1ms convention was measured from hardware");
  string text=File.ReadAllText(file);string bad=Path.Combine(output,"AMC_BAD_TIMING.amc");
  File.WriteAllText(bad,text.Replace("MoveRCommandCostMs=1;","MoveRCommandCostMs=2;"),Encoding.Unicode);
  reject(delegate{AmcInput.Load(bad);},"Unsupported command-cost metadata cannot silently alter the timeline");
  File.WriteAllText(bad,text.Replace("MoveRCommandCostMs=1;","MoveRCommandCostMs=1; MoveRCommandCostMs=0;"),Encoding.Unicode);
  reject(delegate{AmcInput.Load(bad);},"Conflicting timing metadata is rejected");
  reject(delegate {
   int clock=0;AmcResult impossible=new AmcResult {MoveRCommandCostMs=1};
   AmcConverter.ScheduledMove(new List<string>(),1,300,0,ref clock,impossible);
  },"An impossible movement budget fails instead of adding hidden micro delays");
 }
}
