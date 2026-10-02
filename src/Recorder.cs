using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace RecoilProbe {
 internal sealed class CaptureResult {
  public string CsvPath, MetadataPath, AmcPath, AmcError, Reason;
  public int Samples, Shots, MissedShotUpdates;
  public double MaxGapMs, MedianGapMs, DurationMs, MaxReadMs;
 }
 internal sealed class Recorder {
  internal volatile bool StopRequested;
  private readonly string directory;
  private readonly bool autoAmc;
  private readonly Action<string> status;
  private readonly Action<GameIdentity> identity;
  internal Recorder(string folder,bool exportAmc,Action<string> progress)
   : this(folder,exportAmc,progress,delegate(GameIdentity value){}) { }
  internal Recorder(string folder,bool exportAmc,Action<string> progress,Action<GameIdentity> detected) {
   directory=folder;autoAmc=exportAmc;status=progress;identity=detected;
  }
  internal CaptureResult Run() {
   using(Game game=new Game()) {
    bool timerActive=Native.timeBeginPeriod(1)==0;
    try {
     GameIdentity initial=game.ReadIdentity();identity(initial);
     status(initial.WeaponName+" · sens "+initial.Sensitivity.ToString("0.000###",CultureInfo.InvariantCulture)+
      " · torna al gioco e spara quando il recoil e' azzerato.");
     List<Sample> samples=new List<Sample>();Sample baseline=null;
     double pressTime=0;int stableReads=0,attempts=0;
     string reason="Rilascio del pulsante sinistro";bool settingsChanged=false;
     while(!StopRequested) {
      if(!game.Foreground){baseline=null;stableReads=0;Thread.Sleep(10);continue;}
      game.VerifySession();Sample s=game.Read();
      if(s.left_down) {
       if(baseline==null || stableReads<10)
        throw new InvalidOperationException("Attendi un secondo con il sinistro rilasciato prima di sparare.");
       if(baseline.shots_fired!=0)
        throw new InvalidOperationException("Recoil non azzerato. Attendi e premi F8 di nuovo.");
       if(baseline.identity.WeaponHandle!=s.identity.WeaponHandle ||
        Math.Abs(baseline.identity.Sensitivity-s.identity.Sensitivity)>0.000001)
        throw new InvalidOperationException("Arma o sensibilita' cambiate durante l'inizio dello spray.");
       if(s.identity.Scoped)
        throw new InvalidOperationException("Per questa conversione registra senza zoom/ADS.");
       pressTime=s.observed_ms;samples.Add(baseline);samples.Add(s);identity(s.identity);break;
      }
      baseline=s;stableReads++;
      if(++attempts%300==0)identity(s.identity);
      Thread.Sleep(1);
     }
     if(samples.Count==0)return null;
     status("REGISTRAZIONE · "+baseline.identity.WeaponName+" · rilascia il sinistro o premi F8.");
     while(true) {
      if(StopRequested){reason="Interruzione F8";break;}
      try {
       if(!game.Foreground){reason="Finestra CS2 non attiva";break;}
       game.VerifySession();Sample s=game.Read();
       if(s.identity.WeaponHandle!=baseline.identity.WeaponHandle ||
        s.identity.ItemDefinitionIndex!=baseline.identity.ItemDefinitionIndex ||
        s.weapon_hash!=baseline.weapon_hash) {
        reason="Arma cambiata";settingsChanged=true;break;
       }
       if(Math.Abs(s.identity.Sensitivity-baseline.identity.Sensitivity)>0.000001 || s.identity.Scoped) {
        reason="Sensibilita' o zoom cambiati";settingsChanged=true;break;
       }
       samples.Add(s);if(!s.left_down)break;
       if(s.observed_ms-pressTime>=20000 || samples.Count>=30000) {
        reason="Limite registrazione 20 secondi";break;
       }
      } catch(Exception ex){reason="Lettura interrotta: "+ex.Message;break;}
      Thread.Sleep(1);
     }
     foreach(Sample s in samples)s.observed_ms-=pressTime;
     return Save(game,samples,reason,settingsChanged);
    } finally {if(timerActive)Native.timeEndPeriod(1);}
   }
  }
  internal CaptureResult Save(Game game,List<Sample> samples,string reason,bool settingsChanged) {
   Directory.CreateDirectory(directory);
   GameIdentity info=samples[0].identity;
   if(info==null)throw new InvalidOperationException("Arma e sensibilita' automatiche mancanti.");
   string name=info.WeaponName+"_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff",CultureInfo.InvariantCulture)+
    "_"+Guid.NewGuid().ToString("N").Substring(0,8);
   CaptureResult result=Analyze(samples,reason);
   result.CsvPath=Path.Combine(directory,name+".csv");
   result.MetadataPath=Path.Combine(directory,name+".json");
   RecordingIO.WriteCsv(result.CsvPath,samples);
   Dictionary<string,object> meta=new Dictionary<string,object>();
   meta["tool"]="CS2 Recoil Probe 0.2";meta["source_commit"]=Layout.SourceCommit;
   meta["target_build"]=Layout.TargetBuild;meta["observed_build"]=game.Build;
   meta["client_file_version"]=game.ClientVersion;meta["engine_file_version"]=game.EngineVersion;
   meta["weapon_detected"]=info.WeaponName;meta["weapon_definition_index"]=info.ItemDefinitionIndex;
   meta["weapon_designer_name"]=info.DesignerName;meta["weapon_handle"]=info.WeaponHandle;
   meta["weapon_hash"]=samples[0].weapon_hash;meta["weapon_identity_automatically_verified"]=true;
   meta["sensitivity_detected"]=info.Sensitivity;meta["sensitivity_source"]="dwSensitivity + dwSensitivity_sensitivity";
   meta["capture_settings_changed"]=settingsChanged;meta["pawn_mouse_sensitivity"]=info.PawnMouseSensitivity;
   meta["fov_sensitivity_adjust"]=info.FovSensitivityAdjust;meta["scoped"]=info.Scoped;
   meta["requested_poll_interval_ms"]=1;meta["qpc_frequency_hz"]=Stopwatch.Frequency;
   meta["process_access"]="PROCESS_VM_READ | PROCESS_QUERY_INFORMATION";
   meta["time_origin"]="First observed left-down sample; preceding baseline is negative.";
   meta["no_game_memory_writes"]=true;meta["no_mouse_injection"]=true;
   meta["session_checks"]="-insecure, not Valve DS, same pawn, foreground, alive, sign-on full";
   meta["local_only_guaranteed_by_checks"]=false;meta["angle_fields_are_raw_base_values"]=true;
   meta["instantaneous_bullet_recoil_reconstruction_verified"]=false;
   meta["amc_generated"]=false;meta["conversion_assumptions"]="m_pitch=m_yaw=0.022; recoil_scale=2; 64Hz ticks.";
   meta["result"]=result;
   if(autoAmc) {
    try {
     if(settingsChanged || !(reason=="Rilascio del pulsante sinistro" || reason=="Interruzione F8"))
      throw new InvalidOperationException("Spray interrotto: dati grezzi conservati; AMC automatico non creato.");
     RecordingData data=RecordingData.FromSamples(samples,meta);data.SourcePath=result.CsvPath;
     string amcPath=Path.Combine(directory,"AMC",name+"_SENS_"+
      info.Sensitivity.ToString("0.000###",CultureInfo.InvariantCulture)+".amc");
     AmcResult converted=AmcConverter.Convert(data,amcPath,info.Sensitivity);
     result.AmcPath=converted.AmcPath;meta["amc_generated"]=true;meta["amc_result"]=converted;
    } catch(Exception ex){result.AmcError=ex.Message;}
   }
   RecordingIO.WriteJson(result.MetadataPath,meta);
   return result;
  }
  internal static CaptureResult Analyze(List<Sample> samples,string reason) {
   if(samples.Count==0)throw new InvalidOperationException("Nessun campione.");
   CaptureResult result=new CaptureResult {Samples=samples.Count,Reason=reason};
   List<double> gaps=new List<double>();int highWater=samples[0].shots_fired;
   foreach(Sample s in samples) {
    if(s.shots_fired>highWater) {
     int delta=s.shots_fired-highWater;result.Shots+=delta;
     if(delta>1)result.MissedShotUpdates+=delta-1;
     highWater=s.shots_fired;
    }
    result.MaxReadMs=Math.Max(result.MaxReadMs,s.read_duration_ms);
   }
   for(int i=1;i<samples.Count;i++) {
    double gap=samples[i].observed_ms-samples[i-1].observed_ms;
    gaps.Add(gap);result.MaxGapMs=Math.Max(result.MaxGapMs,gap);
   }
   gaps.Sort();if(gaps.Count>0) {
    int middle=gaps.Count/2;
    result.MedianGapMs=gaps.Count%2==0?(gaps[middle-1]+gaps[middle])/2:gaps[middle];
   }
   result.DurationMs=Math.Max(0,samples[samples.Count-1].observed_ms);return result;
  }
 }
}
