using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("CS2 Recoil Probe")]
[assembly: System.Reflection.AssemblyVersion("0.3.4.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.3.4.0")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CoreChecks")]

namespace RecoilProbe {
 internal static class Native {
  internal const uint ReadOnlyRights = 0x0400 | 0x0010;
  [DllImport("kernel32.dll", SetLastError = true)]
  internal static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  internal static extern bool ReadProcessMemory(IntPtr process, IntPtr address,
   [Out] byte[] buffer, UIntPtr size, out UIntPtr read);
  [DllImport("kernel32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  internal static extern bool CloseHandle(IntPtr handle);
  [DllImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
  [DllImport("user32.dll")]
  internal static extern bool UnregisterHotKey(IntPtr window, int id);
  [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
  [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
  [DllImport("winmm.dll")] internal static extern uint timeBeginPeriod(uint period);
  [DllImport("winmm.dll")] internal static extern uint timeEndPeriod(uint period);
 }

 internal sealed class ReadMemory : IDisposable {
  private IntPtr handle;
  private readonly Dictionary<string,string> pointerTrace = new Dictionary<string,string>();
  private readonly Dictionary<string,long> pointerValues = new Dictionary<string,long>();
  internal string PointerTrace { get { return String.Join("\r\n",new List<string>(pointerTrace.Values).ToArray()); } }
  internal ReadMemory(int pid) {
   handle = Native.OpenProcess(Native.ReadOnlyRights, false, pid);
   if (handle == IntPtr.Zero)
    throw new InvalidOperationException("OpenProcess: " + new System.ComponentModel.Win32Exception(
     Marshal.GetLastWin32Error()).Message);
  }
  internal byte[] Bytes(long address, int count) {
   if (address < 0x10000 || address > 0x00007FFFFFFFFFFF || count < 1 || count > 4096)
    throw new InvalidOperationException("Indirizzo di memoria non valido.");
   byte[] data = new byte[count];
   UIntPtr got;
   if (!Native.ReadProcessMemory(handle, new IntPtr(address), data, new UIntPtr((uint)count), out got)
    || got.ToUInt64() != (ulong)count)
    throw new InvalidOperationException("Lettura incompleta. Gioco chiuso, mappa cambiata o layout diverso.");
   return data;
  }
  internal long Pointer(long at) { return NamedPointer(at,"Puntatore senza nome"); }
  internal long NamedPointer(long at,string field) {
   long p;
   try {p=BitConverter.ToInt64(Bytes(at,8),0);}
   catch(Exception ex) {throw new MemoryFieldException(field,at,null,ex);}
   long previousValue;
   if(!pointerValues.TryGetValue(field,out previousValue)||previousValue!=p) {
    pointerValues[field]=p;
    pointerTrace[field]=field+" @0x"+at.ToString("X",CultureInfo.InvariantCulture)+
     " ->0x"+p.ToString("X",CultureInfo.InvariantCulture);
   }
   if(p<0x10000 || p>0x00007FFFFFFFFFFF)
    throw new MemoryFieldException(field,at,p,null);
   return p;
  }
  internal int Int(long at) { return BitConverter.ToInt32(Bytes(at, 4), 0); }
  internal byte Byte(long at) { return Bytes(at, 1)[0]; }
  internal float Float(long at) { return BitConverter.ToSingle(Bytes(at, 4), 0); }
  public void Dispose() {
   IntPtr old = Interlocked.Exchange(ref handle, IntPtr.Zero);
   if (old != IntPtr.Zero) Native.CloseHandle(old);
  }
 }

 internal sealed class Vector {
  public float pitch, yaw, roll;
  internal static Vector From(byte[] bytes, int offset) {
   return new Vector { pitch = BitConverter.ToSingle(bytes, offset),
    yaw = BitConverter.ToSingle(bytes, offset + 4), roll = BitConverter.ToSingle(bytes, offset + 8) };
  }
  internal void Validate(float limit) {
   foreach (float v in new float[] { pitch, yaw, roll })
    if (float.IsNaN(v) || float.IsInfinity(v) || Math.Abs(v) > limit)
     throw new InvalidOperationException("Valori angolari non plausibili. Nessuna conversione AMC.");
  }
 }
 internal sealed class Sample {
  public double observed_ms, read_duration_ms;
  public int shots_fired, client_tick, controller_tick, predictable_tick, unpredictable_tick;
  public int camera_view_punch_tick, weapon_recoil_index;
  public uint weapon_hash;
  public float game_last_fired_time, predictable_tick_fraction, camera_view_punch_tick_ratio;
  public float weapon_recoil_index_float, weapon_last_shot_time;
  public Vector predictable_angle, predictable_velocity, unpredictable_angle, view_angle, eye_angle;
  public Vector input_angle, camera_view_punch;
  public bool left_down;
  public GameIdentity identity;
 }

 internal sealed class Game : IDisposable {
  internal Process Process;
  internal ReadMemory Memory;
  internal int Pid, Build;
  internal long Client, Engine, Pawn, Controller, Services, Camera, Rules;
  internal string ClientVersion, EngineVersion;
  private GameIdentity currentIdentity;
  internal GameIdentity ReadIdentity() {
   currentIdentity = IdentityReader.Read(Memory, Client, Pawn, currentIdentity);
   return currentIdentity;
  }
  internal Game() : this(true) { }
  internal Game(bool requireRecoil) {
   Process[] list = System.Diagnostics.Process.GetProcessesByName("cs2");
   if (list.Length != 1) {
    foreach (Process p in list) p.Dispose();
    throw new InvalidOperationException("Apri una sola istanza di CS2 in pratica locale.");
   }
   Process = list[0]; Pid = Process.Id;
   try {
    string command = null;
    using (ManagementObjectSearcher search = new ManagementObjectSearcher(
     "SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + Pid.ToString(CultureInfo.InvariantCulture))) {
     using (ManagementObjectCollection rows = search.Get()) {
      foreach (ManagementObject row in rows) {
       using (row) { command = row["CommandLine"] as string; }
      }
     }
    }
    if (String.IsNullOrWhiteSpace(command) ||
     !System.Text.RegularExpressions.Regex.IsMatch(command, @"(?:^|\s)-insecure(?:\s|$)",
      System.Text.RegularExpressions.RegexOptions.IgnoreCase))
     throw new InvalidOperationException("Avvia CS2 con -insecure nelle opzioni di avvio di Steam.");
    foreach (ProcessModule module in Process.Modules) {
     if (String.Equals(module.ModuleName, "client.dll", StringComparison.OrdinalIgnoreCase)) {
      Client = module.BaseAddress.ToInt64(); ClientVersion = module.FileVersionInfo.FileVersion;
     }
     if (String.Equals(module.ModuleName, "engine2.dll", StringComparison.OrdinalIgnoreCase)) {
      Engine = module.BaseAddress.ToInt64(); EngineVersion = module.FileVersionInfo.FileVersion;
     }
    }
    if (Client == 0 || Engine == 0) throw new InvalidOperationException("Moduli CS2 non trovati.");
    Memory = new ReadMemory(Pid);
    Build = Memory.Int(Engine + Layout.BuildNumber);
    if (Build != Layout.TargetBuild)
     throw new InvalidOperationException("Build gioco " + Build + "; layout disponibile " +
      Layout.TargetBuild + ". Lettura interrotta: servono dati della stessa build.");
    Pawn = Memory.NamedPointer(Client + Layout.LocalPawn,"Giocatore locale");
    Controller = Memory.NamedPointer(Client + Layout.LocalController,"Controller locale");
    Rules = Memory.NamedPointer(Client + Layout.GameRules,"Regole della sessione");
    if (Memory.Byte(Controller + Layout.IsLocalController) != 1)
     throw new InvalidOperationException("Controller locale non validato.");
    VerifySession();
    ReadIdentity();
    if(requireRecoil) {
     Services=Memory.NamedPointer(Pawn+Layout.AimPunchServices,"Servizi del recoil");
     Camera=Memory.NamedPointer(Pawn+Layout.CameraServices,"Servizi della telecamera");
     VerifySession();
    }
   } catch(Exception ex) {
    Diagnostics.Record(ex,StartupContext(),Memory==null?null:Memory.PointerTrace);
    Dispose();throw;
   }
  }
  internal bool Foreground {
   get {
    uint pid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
    return pid == (uint)Pid;
   }
  }
  internal void VerifySession() {
   if (Process.HasExited) throw new InvalidOperationException("CS2 chiuso.");
   if (Memory.NamedPointer(Client + Layout.LocalPawn,"Giocatore locale") != Pawn ||
    (Services!=0 && Memory.NamedPointer(Pawn + Layout.AimPunchServices,"Servizi del recoil") != Services) ||
    (Camera!=0 && Memory.NamedPointer(Pawn + Layout.CameraServices,"Servizi della telecamera") != Camera))
    throw new InvalidOperationException("Pawn o mappa cambiati. Ripeti la prova.");
   if (Memory.Byte(Rules + Layout.IsValveServer) != 0)
    throw new InvalidOperationException("Server Valve rilevato. Usa una mappa di pratica locale.");
   int health = Memory.Int(Pawn + Layout.Health);
   if (health <= 0 || health > 1000 || Memory.Byte(Pawn + Layout.LifeState) != 0)
    throw new InvalidOperationException("Giocatore locale non vivo o dati non validi.");
   long network = Memory.NamedPointer(Engine + Layout.NetworkClient,"Client della sessione");
   int signon = Memory.Int(network + Layout.SignOnState);
   if (signon != 6) throw new InvalidOperationException("La mappa non e' ancora pronta (sign-on " + signon + ").");
  }
  internal Sample Read() {
   if(Services==0)throw new InvalidOperationException("I servizi del recoil non sono stati inizializzati per la registrazione.");
   for (int attempt = 0; attempt < 3; attempt++) {
    long begin = Stopwatch.GetTimestamp();
    int shotsBefore = Memory.Int(Pawn + Layout.ShotsFired);
    byte[] angle = Memory.Bytes(Services + Layout.PredictableTick,
     Layout.UnpredictableAngle + 12 - Layout.PredictableTick);
    Sample s = new Sample();
    s.predictable_tick = BitConverter.ToInt32(angle, 0);
    s.predictable_tick_fraction = BitConverter.ToSingle(angle,
     Layout.PredictableTickFraction - Layout.PredictableTick);
    s.predictable_angle = Vector.From(angle, Layout.PredictableAngle - Layout.PredictableTick);
    s.predictable_velocity = Vector.From(angle, Layout.PredictableVelocity - Layout.PredictableTick);
    s.unpredictable_tick = BitConverter.ToInt32(angle, Layout.UnpredictableTick - Layout.PredictableTick);
    s.unpredictable_angle = Vector.From(angle, Layout.UnpredictableAngle - Layout.PredictableTick);
    s.game_last_fired_time = Memory.Float(Pawn + Layout.LastFiredTime);
    s.weapon_hash = BitConverter.ToUInt32(Memory.Bytes(Pawn + Layout.WeaponHash, 4), 0);
    s.identity = ReadIdentity();
    s.weapon_recoil_index=Memory.Int(s.identity.WeaponAddress+Layout.WeaponRecoilIndex);
    s.weapon_recoil_index_float=Memory.Float(s.identity.WeaponAddress+Layout.WeaponRecoilIndexFloat);
    s.weapon_last_shot_time=Memory.Float(s.identity.WeaponAddress+Layout.WeaponLastShotTime);
    s.view_angle = Vector.From(Memory.Bytes(Client + Layout.ViewAngles, 12), 0);
    s.eye_angle = Vector.From(Memory.Bytes(Pawn + Layout.EyeAngles, 12), 0);
    s.input_angle = Vector.From(Memory.Bytes(Pawn + Layout.InputViewAngle, 12), 0);
    byte[] camera=Memory.Bytes(Camera+Layout.CameraViewPunchAngle,20);
    s.camera_view_punch=Vector.From(camera,0);
    s.camera_view_punch_tick=BitConverter.ToInt32(camera,Layout.CameraViewPunchTick-Layout.CameraViewPunchAngle);
    s.camera_view_punch_tick_ratio=BitConverter.ToSingle(camera,
     Layout.CameraViewPunchTickRatio-Layout.CameraViewPunchAngle);
    s.controller_tick = Memory.Int(Controller + Layout.TickBase);
    s.client_tick = Memory.Int(Memory.NamedPointer(Engine + Layout.NetworkClient,"Client della sessione") + Layout.ClientTick);
    int shotsAfter = Memory.Int(Pawn + Layout.ShotsFired);
    long end = Stopwatch.GetTimestamp();
    if (shotsBefore != shotsAfter) continue;
    s.shots_fired = shotsAfter;
    s.observed_ms = (begin / 2.0 + end / 2.0) * 1000.0 / Stopwatch.Frequency;
    s.read_duration_ms = (end - begin) * 1000.0 / Stopwatch.Frequency;
    s.left_down = (Native.GetAsyncKeyState(1) & 0x8000) != 0;
    s.predictable_angle.Validate(180);
    s.unpredictable_angle.Validate(180);
    s.predictable_velocity.Validate(2000);
    s.view_angle.Validate(1000); s.eye_angle.Validate(1000);s.input_angle.Validate(1000);
    s.camera_view_punch.Validate(180);
    if (s.shots_fired < 0 || s.shots_fired > 1000 || !Finite(s.game_last_fired_time) ||
     !Finite(s.predictable_tick_fraction) || Math.Abs(s.predictable_tick_fraction) > 2 ||
     !Finite(s.camera_view_punch_tick_ratio)||Math.Abs(s.camera_view_punch_tick_ratio)>2||
     !Finite(s.weapon_recoil_index_float)||!Finite(s.weapon_last_shot_time)||
     s.weapon_recoil_index<0||s.weapon_recoil_index>1000||
     s.predictable_tick < -1 || s.unpredictable_tick < -1 || s.camera_view_punch_tick < -1)
     throw new InvalidOperationException("Campi di memoria non plausibili.");
    return s;
   }
   throw new InvalidOperationException("Campione incoerente durante il cambio di colpo.");
  }
  internal string StartupContext() {
   return "PID: "+Pid+"\r\nBuild letta: "+Build+"\r\nBuild supportata: "+Layout.TargetBuild+
    "\r\nclient.dll: "+ClientVersion+" @0x"+Client.ToString("X",CultureInfo.InvariantCulture)+
    "\r\nengine2.dll: "+EngineVersion+" @0x"+Engine.ToString("X",CultureInfo.InvariantCulture);
  }
  private static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
  public void Dispose() {
   if (Memory != null) { Memory.Dispose(); Memory = null; }
   if (Process != null) { Process.Dispose(); Process = null; }
  }
 }

}
