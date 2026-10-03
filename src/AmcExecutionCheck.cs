using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RecoilProbe {
 internal sealed class ExecutionTracePoint {
  public double TimeMs, ExpectedX, ExpectedY, MeasuredX, MeasuredY;
 }
 internal sealed class ExecutionCheckResult {
  public string Kind = "AMC_EXECUTION_TEST", ExpectedAmc, Weapon, CaptureReason;
  public string Method = "Least-squares timing/axis-gain fit of AMC step positions against recorded eye-angle changes.";
  public string Assumptions = "m_pitch=m_yaw=0.022; eye angles represent player input; no zoom; mouse held stationary while Bloody executes the selected AMC.";
  public bool InstantaneousBulletRecoilVerified = false, RawMouseCountsDirectlyRead = false;
  public bool CompleteTimelineObserved, SearchBoundaryReached, HorizontalMovementPresent, VerticalMovementPresent;
  public int SamplesCompared, ExpectedReleaseMs, AntiRepeatMs, MoveCommands;
  public double Sensitivity, LagMs, DurationFactor, HorizontalGain, VerticalGain;
  public double RootMeanSquareErrorCounts, MaximumErrorCounts, CaptureDurationMs, ExpectedLastMoveMs;
  public double MedianObservationGapMs;
  public string TimingInterpretation = "Positive lag means recorded input follows the AMC. DurationFactor > 1 means its timeline is slower. This includes game input/prediction and observation delay; it is not a direct measurement of the mouse firmware.";
  public string GainInterpretation = "Gain compares eye-angle changes to the AMC under the assumed pitch/yaw values. A different m_pitch/m_yaw can also change gain.";
  public string Conclusion;
  public List<int[]> ExpectedAnchors = new List<int[]>();
  public List<ExecutionTracePoint> Trace = new List<ExecutionTracePoint>();
 }
 internal static class AmcExecutionCheck {
  private sealed class Actual {
   internal double Time,X,Y;
  }
  private sealed class Fit {
   internal double Lag,Duration,GainX,GainY,Error,Max;
  }
  private static double Delta(double a,double b) {
   double d=(a-b)%360;if(d>180)d-=360;if(d< -180)d+=360;return d;
  }
  internal static void ValidateIdentity(AmcInput amc,GameIdentity identity) {
   if(amc==null||identity==null)throw new InvalidOperationException("AMC o identita' mancanti.");
   if(amc.WeaponName!=identity.WeaponName)
    throw new InvalidOperationException("Per il test equipaggia "+amc.WeaponName+".");
   if(Math.Abs(amc.Sensitivity-identity.Sensitivity)>Math.Max(0.000005,amc.Sensitivity*0.000005))
    throw new InvalidOperationException("Per il test usa la sensibilita' dell'AMC: "+
     amc.Sensitivity.ToString("0.000###",CultureInfo.InvariantCulture)+".");
   if(identity.Scoped)throw new InvalidOperationException("Esegui il test senza zoom/ADS.");
  }
  private static Fit Evaluate(List<Actual> actual,List<MouseAnchor> anchors,double lag,double duration) {
   if(duration<0.75||duration>1.50||lag< -100||lag>250)return null;
   double xx=0,xy=0,yy=0,yz=0;
   int index=0,x=0,y=0;
   foreach(Actual a in actual) {
    while(index<anchors.Count && lag+duration*anchors[index].Time<=a.Time) {
     x=anchors[index].X;y=anchors[index].Y;index++;
    }
    xx+=(double)x*x;xy+=x*a.X;yy+=(double)y*y;yz+=y*a.Y;
   }
   double gx=xx>0.000001?xy/xx:1,gy=yy>0.000001?yz/yy:1;
   if((xx>0.000001&&(gx<0.25||gx>4))||(yy>0.000001&&(gy<0.25||gy>4)))return null;
   double error=0,maximum=0;index=0;x=0;y=0;
   foreach(Actual a in actual) {
    while(index<anchors.Count && lag+duration*anchors[index].Time<=a.Time) {
     x=anchors[index].X;y=anchors[index].Y;index++;
    }
    double dx=a.X-x*gx,dy=a.Y-y*gy,d=dx*dx+dy*dy;
    error+=d;maximum=Math.Max(maximum,Math.Sqrt(d));
   }
   return new Fit {Lag=lag,Duration=duration,GainX=gx,GainY=gy,
    Error=error/actual.Count,Max=maximum};
  }
  private static void Prefer(ref Fit best,Fit candidate) {
   if(candidate!=null&&(best==null||candidate.Error<best.Error))best=candidate;
  }
  private static void Refine(List<Actual> actual,List<MouseAnchor> anchors,ref Fit best,
   double lagRadius,double lagStep,double durationRadius,double durationStep) {
   double lag=best.Lag,duration=best.Duration;
   for(double l=lag-lagRadius;l<=lag+lagRadius+0.00000001;l+=lagStep)
    for(double d=duration-durationRadius;d<=duration+durationRadius+0.00000001;d+=durationStep)
     Prefer(ref best,Evaluate(actual,anchors,l,d));
  }
  internal static ExecutionCheckResult Analyze(List<Sample> samples,AmcInput amc,
   double sensitivity,string reason) {
   if(samples==null||samples.Count<20||amc==null||amc.Anchors.Count<3)
    throw new InvalidOperationException("Dati insufficienti per misurare l'esecuzione AMC.");
   if(!IdentityReader.ValidSensitivity(sensitivity))
    throw new InvalidOperationException("Sensibilita' della prova non valida.");
   Sample baseline=null;
   foreach(Sample s in samples)
    if(s.observed_ms<0&&!s.left_down)baseline=s;
   if(baseline==null||baseline.eye_angle==null)
    throw new InvalidOperationException("Baseline precedente al click assente.");
   baseline.eye_angle.Validate(1000);
   List<Actual> actual=new List<Actual>();List<double> gaps=new List<double>();
   double previousTime=Double.NegativeInfinity,movement=0;
   int stride=Math.Max(1,(samples.Count+2499)/2500);
   double angularCount=sensitivity*0.022;
   for(int i=0;i<samples.Count;i++) {
    Sample s=samples[i];
    if(Double.IsNaN(s.observed_ms)||Double.IsInfinity(s.observed_ms)||s.observed_ms<previousTime)
     throw new InvalidOperationException("Tempi della prova non ordinati o non validi.");
    if(i>0)gaps.Add(s.observed_ms-previousTime);previousTime=s.observed_ms;
    if(s.eye_angle==null)throw new InvalidOperationException("Angoli visuale assenti nella prova.");
    s.eye_angle.Validate(1000);
    if(i%stride!=0&&i!=samples.Count-1)continue;
    Actual a=new Actual {Time=s.observed_ms,
     X=-Delta(s.eye_angle.yaw,baseline.eye_angle.yaw)/angularCount,
     Y=Delta(s.eye_angle.pitch,baseline.eye_angle.pitch)/angularCount};
    movement=Math.Max(movement,Math.Sqrt(a.X*a.X+a.Y*a.Y));actual.Add(a);
   }
   if(movement<2)throw new InvalidOperationException("Nessun movimento AMC misurato. Esegui la macro in Bloody durante il test.");
   if(actual[actual.Count-1].Time<300)
    throw new InvalidOperationException("Prova troppo breve. Registra lo spray completo.");
   Fit best=null;
   for(int d=0;d<=30;d++)
    for(int l= -100;l<=250;l+=10)
     Prefer(ref best,Evaluate(actual,amc.Anchors,l,0.75+d*0.025));
   if(best==null)throw new InvalidOperationException("Movimento incompatibile con l'AMC selezionato.");
   Refine(actual,amc.Anchors,ref best,10,2,0.025,0.005);
   Refine(actual,amc.Anchors,ref best,2,0.5,0.005,0.001);
   MouseAnchor last=amc.Anchors[amc.Anchors.Count-1];
   ExecutionCheckResult result=new ExecutionCheckResult {
    ExpectedAmc=Path.GetFileName(amc.SourcePath??"selected.amc"),Weapon=amc.WeaponName,
    CaptureReason=reason,Sensitivity=sensitivity,SamplesCompared=actual.Count,
    ExpectedReleaseMs=amc.ReleaseTime,AntiRepeatMs=amc.TailMs,MoveCommands=amc.MoveCommands,
    LagMs=best.Lag,DurationFactor=best.Duration,HorizontalGain=best.GainX,VerticalGain=best.GainY,
    RootMeanSquareErrorCounts=Math.Sqrt(best.Error),MaximumErrorCounts=best.Max,
    CaptureDurationMs=actual[actual.Count-1].Time,ExpectedLastMoveMs=last.Time,
    CompleteTimelineObserved=actual[actual.Count-1].Time>=best.Lag+best.Duration*last.Time+20,
    SearchBoundaryReached=best.Lag<= -99.5||best.Lag>=249.5||best.Duration<=0.751||best.Duration>=1.499
   };
   foreach(MouseAnchor p in amc.Anchors) {
    result.ExpectedAnchors.Add(new int[] {p.Time,p.X,p.Y});
    result.HorizontalMovementPresent|=p.X!=0;result.VerticalMovementPresent|=p.Y!=0;
   }
   gaps.Sort();int middle=gaps.Count/2;
   result.MedianObservationGapMs=gaps.Count%2==0?(gaps[middle-1]+gaps[middle])/2:gaps[middle];
   result.Conclusion=!result.CompleteTimelineObserved?"Prova interrotta prima dell'ultima parte della macro.":
    result.SearchBoundaryReached?"Stima al limite della ricerca; scarto troppo grande o macro diversa.":
    result.RootMeanSquareErrorCounts>3?"Esecuzione non descritta bene da un ritardo e una durata costanti. Controllare macro selezionata e mouse fermo.":
    "Allineamento dell'esecuzione stimato. Il report non verifica la direzione reale dei proiettili.";
   int index=0,x=0,y=0;double nextTrace=Double.NegativeInfinity;
   foreach(Actual a in actual) {
    while(index<amc.Anchors.Count&&best.Lag+best.Duration*amc.Anchors[index].Time<=a.Time) {
     x=amc.Anchors[index].X;y=amc.Anchors[index].Y;index++;
    }
    if(a.Time<nextTrace&&a!=actual[actual.Count-1])continue;
    result.Trace.Add(new ExecutionTracePoint {TimeMs=a.Time,ExpectedX=x,ExpectedY=y,
     MeasuredX=a.X,MeasuredY=a.Y});nextTrace=a.Time+10;
   }
   return result;
  }
 }
}
