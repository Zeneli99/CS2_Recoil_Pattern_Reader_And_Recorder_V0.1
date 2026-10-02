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
[assembly: System.Reflection.AssemblyVersion("0.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.1.0.0")]

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
  internal long Pointer(long at) {
   long p = BitConverter.ToInt64(Bytes(at, 8), 0);
   if (p < 0x10000 || p > 0x00007FFFFFFFFFFF)
    throw new InvalidOperationException("Puntatore assente. Entra prima in una mappa.");
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
  public uint weapon_hash;
  public float game_last_fired_time, predictable_tick_fraction;
  public Vector predictable_angle, predictable_velocity, unpredictable_angle, view_angle, eye_angle;
  public bool left_down;
 }

 internal sealed class Game : IDisposable {
  internal Process Process;
  internal ReadMemory Memory;
  internal int Pid, Build;
  internal long Client, Engine, Pawn, Controller, Services, Rules;
  internal string ClientVersion, EngineVersion;
  internal Game() {
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
    Pawn = Memory.Pointer(Client + Layout.LocalPawn);
    Controller = Memory.Pointer(Client + Layout.LocalController);
    Services = Memory.Pointer(Pawn + Layout.AimPunchServices);
    Rules = Memory.Pointer(Client + Layout.GameRules);
    if (Memory.Byte(Controller + Layout.IsLocalController) != 1)
     throw new InvalidOperationException("Controller locale non validato.");
    VerifySession();
   } catch { Dispose(); throw; }
  }
  internal bool Foreground {
   get {
    uint pid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
    return pid == (uint)Pid;
   }
  }
  internal void VerifySession() {
   if (Process.HasExited) throw new InvalidOperationException("CS2 chiuso.");
   if (Memory.Pointer(Client + Layout.LocalPawn) != Pawn ||
    Memory.Pointer(Pawn + Layout.AimPunchServices) != Services)
    throw new InvalidOperationException("Pawn o mappa cambiati. Ripeti la prova.");
   if (Memory.Byte(Rules + Layout.IsValveServer) != 0)
    throw new InvalidOperationException("Server Valve rilevato. Usa una mappa di pratica locale.");
   int health = Memory.Int(Pawn + Layout.Health);
   if (health <= 0 || health > 1000 || Memory.Byte(Pawn + Layout.LifeState) != 0)
    throw new InvalidOperationException("Giocatore locale non vivo o dati non validi.");
   long network = Memory.Pointer(Engine + Layout.NetworkClient);
   int signon = Memory.Int(network + Layout.SignOnState);
   if (signon != 6) throw new InvalidOperationException("La mappa non e' ancora pronta (sign-on " + signon + ").");
  }
  internal Sample Read() {
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
    s.view_angle = Vector.From(Memory.Bytes(Client + Layout.ViewAngles, 12), 0);
    s.eye_angle = Vector.From(Memory.Bytes(Pawn + Layout.EyeAngles, 12), 0);
    s.controller_tick = Memory.Int(Controller + Layout.TickBase);
    s.client_tick = Memory.Int(Memory.Pointer(Engine + Layout.NetworkClient) + Layout.ClientTick);
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
    s.view_angle.Validate(1000); s.eye_angle.Validate(1000);
    if (s.shots_fired < 0 || s.shots_fired > 1000 || !Finite(s.game_last_fired_time) ||
     !Finite(s.predictable_tick_fraction) || Math.Abs(s.predictable_tick_fraction) > 2 ||
     s.predictable_tick < -1 || s.unpredictable_tick < -1)
     throw new InvalidOperationException("Campi di memoria non plausibili.");
    return s;
   }
   throw new InvalidOperationException("Campione incoerente durante il cambio di colpo.");
  }
  private static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
  public void Dispose() {
   if (Memory != null) { Memory.Dispose(); Memory = null; }
   if (Process != null) { Process.Dispose(); Process = null; }
  }
 }

 internal sealed class CaptureResult {
  public string CsvPath, MetadataPath, Reason;
  public int Samples, Shots, MissedShotUpdates;
  public double MaxGapMs, MedianGapMs, DurationMs, MaxReadMs;
 }
 internal sealed class Recorder {
  internal volatile bool StopRequested;
  private readonly string directory, weapon;
  private readonly decimal sensitivity;
  private readonly Action<string> status;
  internal Recorder(string folder, string name, decimal sens, Action<string> progress) {
   directory = folder; weapon = name; sensitivity = sens; status = progress;
  }
  internal CaptureResult Run() {
   using (Game game = new Game()) {
    bool timerActive = Native.timeBeginPeriod(1) == 0;
    try {
     status("Build " + game.Build + " · F8 pronto. Torna al gioco, poi tieni premuto il sinistro.");
     List<Sample> samples = new List<Sample>();
     Sample baseline = null;
     double pressTime = 0;
     int attempts = 0, stableReads = 0;
     string reason = "Rilascio del pulsante sinistro";
     while (!StopRequested) {
      if (!game.Foreground) { baseline = null; stableReads = 0; Thread.Sleep(10); continue; }
      game.VerifySession();
      Sample s = game.Read();
      if (s.left_down) {
       if (baseline == null || stableReads < 10)
        throw new InvalidOperationException("Prima di sparare attendi almeno 1 secondo con il sinistro rilasciato.");
       if (baseline.shots_fired != 0)
        throw new InvalidOperationException("Il recoil non e' ancora azzerato. Aspetta e premi F8 di nuovo.");
       pressTime = s.observed_ms;
       samples.Add(baseline); samples.Add(s);
       break;
      }
      baseline = s; stableReads++;
      if (++attempts % 200 == 0) status("Pronto · non muovere il mouse. Tieni il sinistro per un caricatore.");
      Thread.Sleep(1);
     }
     if (samples.Count == 0) return null;
     status("REGISTRAZIONE · rilascia il sinistro o premi F8 per terminare.");
     while (true) {
      if (StopRequested) { reason = "Interruzione F8"; break; }
      try {
       if (!game.Foreground) { reason = "Finestra CS2 non attiva"; break; }
       game.VerifySession();
       Sample s = game.Read();
       if (s.weapon_hash != baseline.weapon_hash) { reason = "Arma cambiata"; break; }
       samples.Add(s);
       if (!s.left_down) break;
       if (s.observed_ms - pressTime >= 20000 || samples.Count >= 30000) {
        reason = "Limite registrazione 20 secondi"; break;
       }
      } catch (Exception ex) { reason = "Lettura interrotta: " + ex.Message; break; }
      Thread.Sleep(1);
     }
     foreach (Sample s in samples) s.observed_ms -= pressTime;
     return Save(game, samples, reason);
    } finally { if (timerActive) Native.timeEndPeriod(1); }
   }
  }
  private CaptureResult Save(Game game, List<Sample> samples, string reason) {
   Directory.CreateDirectory(directory);
   string safe = System.Text.RegularExpressions.Regex.Replace(weapon, @"[^A-Za-z0-9_-]", "_");
   if (safe.Length > 40) safe = safe.Substring(0, 40);
   string name = safe + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)
    + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
   string csv = Path.Combine(directory, name + ".csv");
   CaptureResult result = Analyze(samples, reason);
   result.CsvPath = csv; result.MetadataPath = Path.ChangeExtension(csv, ".json");
   using (FileStream stream = new FileStream(csv, FileMode.CreateNew, FileAccess.Write))
   using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))) {
    writer.WriteLine("observed_ms,read_duration_ms,shots_fired,client_tick,controller_tick," +
     "weapon_hash,game_last_fired_time,predictable_tick,predictable_tick_fraction," +
     "predictable_pitch,predictable_yaw,predictable_roll," +
     "predictable_velocity_pitch,predictable_velocity_yaw,predictable_velocity_roll," +
     "unpredictable_tick,unpredictable_pitch,unpredictable_yaw,unpredictable_roll," +
     "view_pitch,view_yaw,view_roll,eye_pitch,eye_yaw,eye_roll,left_down,shot_update");
    int previous = samples[0].shots_fired;
    foreach (Sample s in samples) {
     bool update = s.shots_fired > previous; previous = s.shots_fired;
     List<string> v = new List<string>();
     Add(v, s.observed_ms); Add(v, s.read_duration_ms); Add(v, s.shots_fired);
     Add(v, s.client_tick); Add(v, s.controller_tick); Add(v, s.weapon_hash);
     Add(v, s.game_last_fired_time); Add(v, s.predictable_tick); Add(v, s.predictable_tick_fraction);
     AddVector(v, s.predictable_angle); AddVector(v, s.predictable_velocity);
     Add(v, s.unpredictable_tick); AddVector(v, s.unpredictable_angle);
     AddVector(v, s.view_angle); AddVector(v, s.eye_angle);
     v.Add(s.left_down ? "1" : "0"); v.Add(update ? "1" : "0");
     writer.WriteLine(String.Join(",", v.ToArray()));
    }
   }
   Dictionary<string, object> meta = new Dictionary<string, object>();
   meta["tool"] = "CS2 Recoil Probe 0.1";
   meta["source_commit"] = Layout.SourceCommit;
   meta["target_build"] = Layout.TargetBuild; meta["observed_build"] = game.Build;
   meta["client_file_version"] = game.ClientVersion; meta["engine_file_version"] = game.EngineVersion;
   meta["weapon_label_user_supplied"] = weapon; meta["weapon_hash"] = samples[0].weapon_hash;
   meta["sensitivity_user_supplied"] = sensitivity;
   meta["requested_poll_interval_ms"] = 1; meta["qpc_frequency_hz"] = Stopwatch.Frequency;
   meta["process_access"] = "PROCESS_VM_READ | PROCESS_QUERY_INFORMATION";
   meta["time_origin"] = "First observed left-down sample; preceding baseline is negative.";
   meta["no_game_memory_writes"] = true; meta["no_mouse_injection"] = true;
   meta["session_checks"] = "-insecure, not Valve DS, same pawn, foreground, alive, sign-on full";
   meta["local_only_guaranteed_by_checks"] = false;
   meta["angle_fields_are_raw_base_values"] = true;
   meta["instantaneous_bullet_recoil_reconstruction_verified"] = false;
   meta["amc_generated"] = false; meta["result"] = result;
   using (FileStream stream = new FileStream(result.MetadataPath, FileMode.CreateNew, FileAccess.Write))
   using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
    writer.Write(new JavaScriptSerializer().Serialize(meta));
   return result;
  }
  private static CaptureResult Analyze(List<Sample> samples, string reason) {
   CaptureResult r = new CaptureResult { Samples = samples.Count, Reason = reason };
   List<double> gaps = new List<double>();
   int previous = samples[0].shots_fired;
   foreach (Sample s in samples) {
    if (s.shots_fired > previous) {
     int delta = s.shots_fired - previous;
     r.Shots += delta;
     if (delta > 1) r.MissedShotUpdates += delta - 1;
    }
    previous = s.shots_fired; r.MaxReadMs = Math.Max(r.MaxReadMs, s.read_duration_ms);
   }
   for (int i = 1; i < samples.Count; i++) {
    double g = samples[i].observed_ms - samples[i - 1].observed_ms;
    gaps.Add(g); r.MaxGapMs = Math.Max(r.MaxGapMs, g);
   }
   gaps.Sort();
   if (gaps.Count > 0) {
    int middle = gaps.Count / 2;
    r.MedianGapMs = gaps.Count % 2 == 0 ? (gaps[middle - 1] + gaps[middle]) / 2 : gaps[middle];
   }
   r.DurationMs = Math.Max(0, samples[samples.Count - 1].observed_ms);
   return r;
  }
  private static void Add(List<string> list, object value) {
   list.Add(Convert.ToString(value, CultureInfo.InvariantCulture));
  }
  private static void AddVector(List<string> list, Vector v) {
   Add(list, v.pitch); Add(list, v.yaw); Add(list, v.roll);
  }
 }

 internal sealed class MainForm : Form {
  private TextBox weapon, folder, log;
  private NumericUpDown sensitivity;
  private Button record, open;
  private Recorder recorder;
  private Thread worker;
  private bool running, closing;
  private const int HotkeyId = 8118;
  internal MainForm() {
   Text = "CS2 RECOIL PROBE · 0.1";
   ClientSize = new Size(540, 360);
   FormBorderStyle = FormBorderStyle.FixedSingle;
   MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
   BackColor = Color.FromArgb(18, 18, 20); ForeColor = Color.Gainsboro;
   Font = new Font("Segoe UI", 9F);
   Label title = new Label { Text = "CS2 RECOIL PROBE", ForeColor = Color.FromArgb(235, 63, 75),
    Font = new Font("Segoe UI", 15F, FontStyle.Bold), Location = new Point(20, 17), Size = new Size(490, 32) };
   Controls.Add(title);
   Controls.Add(new Label { Text = "Prima prova: memoria reale → CSV. Solo pratica locale con -insecure.",
    Location = new Point(20, 55), Size = new Size(500, 32) });
   Controls.Add(new Label { Text = "Arma (etichetta)", Location = new Point(20, 99), Size = new Size(135, 23) });
   weapon = new TextBox { Text = "AK47", Location = new Point(20, 125), Size = new Size(195, 25), MaxLength = 40 };
   Controls.Add(weapon);
   Controls.Add(new Label { Text = "Sensibilità (annotazione)", Location = new Point(240, 99), Size = new Size(260, 23) });
   sensitivity = new NumericUpDown { Minimum = 0.001M, Maximum = 100M, DecimalPlaces = 3,
    Increment = 0.001M, Value = 1.250M, Location = new Point(240, 125), Size = new Size(160, 25) };
   Controls.Add(sensitivity);
   folder = new TextBox { Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Registrazioni"),
    Location = new Point(20, 168), Size = new Size(395, 25) };
   Controls.Add(folder);
   Button choose = new Button { Text = "Cartella", Location = new Point(425, 166), Size = new Size(95, 28) };
   choose.Click += delegate {
    using (FolderBrowserDialog d = new FolderBrowserDialog()) {
     d.SelectedPath = folder.Text;
     if (d.ShowDialog(this) == DialogResult.OK) folder.Text = d.SelectedPath;
    }
   };
   Controls.Add(choose);
   record = new Button { Text = "ARMA / FERMA · F8", Location = new Point(20, 210), Size = new Size(260, 37) };
   record.Click += delegate { Toggle(); }; Controls.Add(record);
   open = new Button { Text = "Apri registrazioni", Location = new Point(300, 210), Size = new Size(220, 37) };
   open.Click += delegate {
    try { Directory.CreateDirectory(folder.Text); Process.Start(folder.Text); }
    catch (Exception ex) { ShowStatus(ex.Message); }
   };
   Controls.Add(open);
   log = new TextBox { ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.FixedSingle,
    Location = new Point(20, 264), Size = new Size(500, 78), BackColor = Color.FromArgb(28, 28, 31),
    ForeColor = Color.Gainsboro, Text = "Apri una mappa locale. F8, torna al gioco, attendi 1 secondo, poi spara un caricatore senza muovere il mouse." };
   Controls.Add(log);
   foreach (Control c in Controls) {
    Button b = c as Button;
    if (b != null) { b.FlatStyle = FlatStyle.Flat; b.BackColor = Color.FromArgb(58, 24, 28);
     b.ForeColor = Color.Gainsboro; b.FlatAppearance.BorderColor = Color.FromArgb(157, 45, 55); }
   }
   FormClosing += delegate(object sender, FormClosingEventArgs args) {
    if (running) { closing = true; recorder.StopRequested = true; args.Cancel = true;
     ShowStatus("Termino la registrazione e salvo i dati..."); }
   };
  }
  protected override void OnHandleCreated(EventArgs e) {
   base.OnHandleCreated(e);
   if (!Native.RegisterHotKey(Handle, HotkeyId, 0x4000, 0x77))
    ShowStatus("F8 già occupato da un altro programma. Usa il pulsante ARMA.");
  }
  protected override void OnHandleDestroyed(EventArgs e) {
   Native.UnregisterHotKey(Handle, HotkeyId); base.OnHandleDestroyed(e);
  }
  protected override void WndProc(ref Message m) {
   if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotkeyId) Toggle();
   base.WndProc(ref m);
  }
  private void Toggle() {
   if (running) { recorder.StopRequested = true; record.Enabled = false; return; }
   string path;
   try { path = Path.GetFullPath(folder.Text); }
   catch (Exception ex) { ShowStatus(ex.Message); return; }
   if (String.IsNullOrWhiteSpace(weapon.Text)) { ShowStatus("Scrivi il nome dell'arma."); return; }
   recorder = new Recorder(path, weapon.Text.Trim(), sensitivity.Value, ShowStatus);
   running = true; weapon.Enabled = false; sensitivity.Enabled = false; folder.Enabled = false;
   record.Text = "FERMA · F8";
   worker = new Thread(delegate() {
    CaptureResult result = null; string error = null;
    try { result = recorder.Run(); } catch (Exception ex) { error = ex.Message; }
    Post(delegate {
     running = false; record.Enabled = true; record.Text = "ARMA / FERMA · F8";
     weapon.Enabled = true; sensitivity.Enabled = true; folder.Enabled = true;
     if (error != null) log.Text = error;
     else if (result == null) log.Text = "Registrazione annullata prima del primo colpo.";
     else log.Text = result.Shots + " colpi · " + result.Samples + " campioni · gap max " +
      result.MaxGapMs.ToString("F2", CultureInfo.CurrentCulture) + " ms" +
      (result.MissedShotUpdates > 0 ? " · aggiornamenti saltati: " + result.MissedShotUpdates : "") +
      "\r\n" + Path.GetFileName(result.CsvPath) + "\r\n" + result.Reason;
     if (closing) Close();
    });
   });
   worker.IsBackground = true; worker.Name = "CS2 read-only recorder"; worker.Start();
  }
  private void Post(Action action) {
   try { if (!IsDisposed && IsHandleCreated) BeginInvoke(action); }
   catch (InvalidOperationException) { }
  }
  private void ShowStatus(string message) { Post(delegate { log.Text = message; }); }
 }
 internal static class Program {
  [STAThread] private static void Main() {
   if (IntPtr.Size != 8) { MessageBox.Show("Serve la versione x64."); return; }
   Application.EnableVisualStyles();
   Application.SetCompatibleTextRenderingDefault(false);
   Application.Run(new MainForm());
  }
 }
}
