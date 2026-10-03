using System;
using System.Collections.Generic;

namespace RecoilProbe {
 internal sealed class RecoilDynamicsFit {
  public double ExponentialDecayPerSecond, LinearDecayDegreesPerSecond;
  public double VelocityDecayPerSecond, RootMeanSquareResidualDegrees, MaximumResidualDegrees;
  public int Transitions;
  public bool SearchBoundaryReached;
 }

 internal static class RecoilDynamics {
  private const double IntegrationStepSeconds = 1.0 / 128.0;
  private sealed class State {
   internal double Pitch, Yaw, PitchVelocity, YawVelocity;
  }

  private static bool Finite(double value) {
   return !Double.IsNaN(value) && !Double.IsInfinity(value);
  }
  private static State Start(RecoilPoint point) {
   return new State { Pitch=point.Pitch, Yaw=point.Yaw,
    PitchVelocity=point.PitchVelocity, YawVelocity=point.YawVelocity };
  }
  private static void Step(State state,double seconds,double exponential,double linear,double velocityDecay) {
   double angleFactor=Math.Exp(-exponential*seconds);
   state.Pitch*=angleFactor;state.Yaw*=angleFactor;
   double magnitude=Math.Sqrt(state.Pitch*state.Pitch+state.Yaw*state.Yaw);
   double subtract=linear*seconds;
   if(magnitude>subtract&&magnitude>0.000000000001) {
    double factor=1.0-subtract/magnitude;state.Pitch*=factor;state.Yaw*=factor;
   } else {state.Pitch=0;state.Yaw=0;}
   state.Pitch+=state.PitchVelocity*seconds*0.5;
   state.Yaw+=state.YawVelocity*seconds*0.5;
   double velocityFactor=Math.Exp(-velocityDecay*seconds);
   state.PitchVelocity*=velocityFactor;state.YawVelocity*=velocityFactor;
   state.Pitch+=state.PitchVelocity*seconds*0.5;
   state.Yaw+=state.YawVelocity*seconds*0.5;
  }
  private static State Advance(RecoilPoint point,double seconds,double exponential,double linear,double velocityDecay) {
   State state=Start(point);double remaining=Math.Max(0,seconds);
   while(remaining>0.0000000001) {
    double step=Math.Min(IntegrationStepSeconds,remaining);
    Step(state,step,exponential,linear,velocityDecay);remaining-=step;
   }
   return state;
  }
  private static RecoilDynamicsFit Score(List<RecoilPoint> points,double exponential,double linear,double velocityDecay) {
   double squared=0,maximum=0;int transitions=0;
   for(int i=0;i+1<points.Count;i++) {
    double seconds=(points[i+1].Tick-points[i].Tick)/64.0;
    if(seconds<=0||seconds>2)continue;
    State predicted=Advance(points[i],seconds,exponential,linear,velocityDecay);
    double dp=predicted.Pitch-points[i+1].Pitch,dy=predicted.Yaw-points[i+1].Yaw;
    double distance=Math.Sqrt(dp*dp+dy*dy);
    squared+=distance*distance;maximum=Math.Max(maximum,distance);transitions++;
   }
   if(transitions==0)return null;
   return new RecoilDynamicsFit {ExponentialDecayPerSecond=exponential,
    LinearDecayDegreesPerSecond=linear,VelocityDecayPerSecond=velocityDecay,
    RootMeanSquareResidualDegrees=Math.Sqrt(squared/transitions),MaximumResidualDegrees=maximum,
    Transitions=transitions};
  }
  private static bool Better(RecoilDynamicsFit candidate,RecoilDynamicsFit best) {
   return candidate!=null&&(best==null||candidate.RootMeanSquareResidualDegrees<best.RootMeanSquareResidualDegrees);
  }
  private static RecoilDynamicsFit Candidate(List<RecoilPoint> points,RecoilDynamicsFit best,
   int parameter,double difference) {
   double exponential=best.ExponentialDecayPerSecond,linear=best.LinearDecayDegreesPerSecond;
   double velocity=best.VelocityDecayPerSecond;
   if(parameter==0)exponential+=difference;else if(parameter==1)linear+=difference;else velocity+=difference;
   if(exponential<=0||linear<0||velocity<=0)return null;
   return Score(points,exponential,linear,velocity);
  }
  internal static RecoilDynamicsFit Fit(List<RecoilPoint> points) {
   if(points==null||points.Count<5)
    throw new InvalidOperationException("Servono almeno cinque colpi per ricostruire la dinamica interna del recoil.");
   RecoilDynamicsFit best=null;
   for(double exponential=4;exponential<=14.0001;exponential+=1)
    for(double linear=8;linear<=30.0001;linear+=2)
     for(double velocity=1;velocity<=8.0001;velocity+=0.5) {
      RecoilDynamicsFit candidate=Score(points,exponential,linear,velocity);
      if(Better(candidate,best))best=candidate;
     }
   foreach(double step in new double[] {0.2,0.05,0.01,0.002,0.0005}) {
    bool improved=true;int passes=0;
    while(improved&&passes++<1000) {
     improved=false;
     for(int parameter=0;parameter<3;parameter++)
      foreach(double direction in new double[] {-step,step}) {
       RecoilDynamicsFit candidate=Candidate(points,best,parameter,direction);
       if(Better(candidate,best)){best=candidate;improved=true;}
      }
    }
   }
   if(best==null||!Finite(best.RootMeanSquareResidualDegrees)||!Finite(best.MaximumResidualDegrees))
    throw new InvalidOperationException("Dinamica del recoil non calcolabile.");
   best.SearchBoundaryReached=best.ExponentialDecayPerSecond<=4.001||best.ExponentialDecayPerSecond>=13.999||
    best.LinearDecayDegreesPerSecond<=8.001||best.LinearDecayDegreesPerSecond>=29.999||
    best.VelocityDecayPerSecond<=1.001||best.VelocityDecayPerSecond>=7.999;
   if(best.SearchBoundaryReached)
    throw new InvalidOperationException("Dinamica recoil fuori dai limiti verificabili. Conserva CSV/JSON e ripeti lo spray.");
   if(best.RootMeanSquareResidualDegrees>0.03||best.MaximumResidualDegrees>0.08)
    throw new InvalidOperationException("Lo stato interno non ricostruisce i punti dello spray con precisione sufficiente. Conserva CSV/JSON.");
   return best;
  }
  internal static void EvaluateRaw(RecoilPoint from,double seconds,double exponential,double linear,
   double velocityDecay,out double pitch,out double yaw) {
   if(from==null)throw new InvalidOperationException("Stato recoil assente.");
   State state=Advance(from,seconds,exponential,linear,velocityDecay);
   pitch=state.Pitch;yaw=state.Yaw;
  }
  internal static void EvaluateCorrected(RecoilPoint from,RecoilPoint to,double fraction,
   RecoilDynamicsFit fit,out double pitch,out double yaw) {
   if(from==null||to==null||fit==null)throw new InvalidOperationException("Stato recoil incompleto.");
   double f=Math.Max(0,Math.Min(1,fraction));
   double total=(to.Tick-from.Tick)/64.0;
   State value=Advance(from,total*f,fit.ExponentialDecayPerSecond,
    fit.LinearDecayDegreesPerSecond,fit.VelocityDecayPerSecond);
   State predictedEnd=Advance(from,total,fit.ExponentialDecayPerSecond,
    fit.LinearDecayDegreesPerSecond,fit.VelocityDecayPerSecond);
   double correction=f*f*(3.0-2.0*f);
   pitch=value.Pitch+(to.Pitch-predictedEnd.Pitch)*correction;
   yaw=value.Yaw+(to.Yaw-predictedEnd.Yaw)*correction;
   if(f>=1){pitch=to.Pitch;yaw=to.Yaw;}
  }
 }
}
