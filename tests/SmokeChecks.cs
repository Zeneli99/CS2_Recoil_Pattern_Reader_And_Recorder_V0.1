using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class SmokeChecks {
 private static int count;
 private const BindingFlags Member = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
 private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
 private static void Check(bool ok, string name) {
  if (!ok) throw new Exception("FAIL: " + name);
  count++; Console.WriteLine("PASS: " + name);
 }
 private static void Set(object value, string field, object data) {
  value.GetType().GetField(field, Member).SetValue(value, data);
 }
 private static T Field<T>(object value, string field) {
  return (T)value.GetType().GetField(field, Member).GetValue(value);
 }
 [STAThread]
 private static int Main(string[] args) {
  try {
   if (args.Length != 2) throw new Exception("Usage: SmokeChecks.exe PROGRAM_EXE UI_PNG");
   string executable = Path.GetFullPath(args[0]);
   Assembly program = Assembly.LoadFrom(executable);
   Check(IntPtr.Size == 8, "Windows x64 test process");
   Check(program.GetName().ProcessorArchitecture == ProcessorArchitecture.Amd64, "EXE targets AMD64");
   Check(program.GetName().Version.ToString() == "0.3.4.0", "Version 0.3.4.0");
   Check(program.EntryPoint.IsDefined(typeof(STAThreadAttribute), false), "GUI entry point uses STA");
   Type native = program.GetType("RecoilProbe.Native", true);
   uint rights = (uint)native.GetField("ReadOnlyRights", Static).GetRawConstantValue();
   Check(rights == 0x0410, "Process access is query plus VM_READ");
   Check((rights & (0x0020 | 0x0008)) == 0, "No VM_WRITE or VM_OPERATION rights");
   Type layout = program.GetType("RecoilProbe.Layout", true);
   Check((int)layout.GetField("TargetBuild", Static).GetRawConstantValue() == 14189, "Target build 14189");

   Type readType = program.GetType("RecoilProbe.ReadMemory", true);
   byte[] expected = Encoding.UTF8.GetBytes("Readonly native interop check");
   IntPtr address = Marshal.AllocHGlobal(expected.Length);
   try {
    Marshal.Copy(expected, 0, address, expected.Length);
    int pid; using (Process own = Process.GetCurrentProcess()) { pid = own.Id; }
    using (IDisposable memory = (IDisposable)Activator.CreateInstance(readType, Member, null, new object[] { pid }, null)) {
     byte[] actual = (byte[])readType.GetMethod("Bytes", Member).Invoke(memory,
      new object[] { address.ToInt64(), expected.Length });
     Check(actual.Length == expected.Length && Encoding.UTF8.GetString(actual) == Encoding.UTF8.GetString(expected),
      "ReadProcessMemory roundtrip reads only the test process");
     bool invalid = false;
     try { readType.GetMethod("Bytes", Member).Invoke(memory, new object[] { 0L, 4 }); }
     catch (TargetInvocationException ex) { invalid = ex.InnerException is InvalidOperationException; }
     Check(invalid, "Invalid address rejected before native read");
    }
   } finally { Marshal.FreeHGlobal(address); }

   Environment.SetEnvironmentVariable("CS2_PROBE_TEST_MODE", "1");
   Application.EnableVisualStyles();
   Application.SetCompatibleTextRenderingDefault(false);
   Type mainType = program.GetType("RecoilProbe.MainForm", true);
   using (Form form = (Form)Activator.CreateInstance(mainType, true)) {
    Check(form.ClientSize == new Size(540, 360), "Compact 540 by 360 interface");
    Check(form.BackColor == Color.FromArgb(18, 18, 20), "Dark theme");
    Check(!form.MaximizeBox && form.FormBorderStyle == FormBorderStyle.FixedSingle, "Fixed compact window");
    TextBox sensitivity = (TextBox)mainType.GetField("sensitivity", Member).GetValue(form);
    Check(sensitivity.ReadOnly && sensitivity.Text == "AUTO", "Sensitivity is automatic and cannot be manually annotated");
    TextBox weapon = (TextBox)mainType.GetField("weapon", Member).GetValue(form);
    Check(weapon.ReadOnly, "Weapon field is automatic");
    Button record = (Button)mainType.GetField("record", Member).GetValue(form);
    Check(record.Text.Contains("F8")&&record.Text.Contains("ESTRAI"), "Main F8 action extracts without starting a recorder");
    ComboBox format=(ComboBox)mainType.GetField("format",Member).GetValue(form);
    Check(format.Enabled&&format.Items.Count==2&&format.Items[0].ToString()=="AMC"&&format.Items[1].ToString()=="XML Razer",
     "Main offers an exclusive AMC or XML Razer output choice");
    format.SelectedIndex=1;
    Check(record.Text=="ESTRAI XML · F8","Choosing XML updates the main F8 extraction action");
    format.SelectedIndex=0;
    Check(record.Text=="ESTRAI AMC · F8","Choosing AMC restores the AMC extraction action");
    format.SelectedIndex=1;
    Button report=(Button)mainType.GetField("report",Member).GetValue(form);
    Check(report.Text=="REPORT" && report.Right<=form.ClientSize.Width,"Startup diagnostic button fits the compact window");
    form.Show();
    Application.DoEvents();
    form.Refresh();
    Application.DoEvents();
    using (Bitmap bitmap = new Bitmap(form.Width, form.Height)) {
     form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
     int redPixels = 0;
     for (int y = 0; y < bitmap.Height; y += 2) for (int x = 0; x < bitmap.Width; x += 2) {
      Color pixel = bitmap.GetPixel(x, y);
      if (pixel.R > 130 && pixel.G < 130 && pixel.B < 170) redPixels++;
     }
     Check(redPixels > 60, "Preview contains rendered red controls instead of a blank window");
     bitmap.Save(Path.GetFullPath(args[1]), System.Drawing.Imaging.ImageFormat.Png);
    }
    Check(File.Exists(Path.GetFullPath(args[1])), "UI preview generated");
   }

   Type sampleType = program.GetType("RecoilProbe.Sample", true);
   Type vectorType = program.GetType("RecoilProbe.Vector", true);
   Type recorderType = program.GetType("RecoilProbe.Recorder", true);
   Type listType = typeof(List<>).MakeGenericType(sampleType);
   IList samples = (IList)Activator.CreateInstance(listType);
   double[] times = new double[] { -1, 0, 1, 100, 200, 300 };
   int[] shots = new int[] { 0, 0, 1, 2, 5, 0 };
   for (int i = 0; i < times.Length; i++) {
    object sample = Activator.CreateInstance(sampleType, true);
    Set(sample, "observed_ms", times[i]); Set(sample, "shots_fired", shots[i]);
    Set(sample, "read_duration_ms", i == 4 ? 2.5 : 0.1);
    Set(sample, "left_down", i > 0 && i < 5);
    Set(sample, "weapon_hash", (uint)12345);
    foreach (string key in new string[] { "predictable_angle", "predictable_velocity", "unpredictable_angle", "view_angle", "eye_angle", "input_angle", "camera_view_punch" }) {
     object vector = Activator.CreateInstance(vectorType, true);
     Set(vector, "pitch", -1.25F); Set(vector, "yaw", 2.5F); Set(vector, "roll", 0F);
     Set(sample, key, vector);
    }
    Type identityType = program.GetType("RecoilProbe.GameIdentity", true);
    object identity = Activator.CreateInstance(identityType, true);
    Set(identity,"WeaponName","AK47");Set(identity,"DesignerName","weapon_ak47");
    Set(identity,"ItemDefinitionIndex",7);Set(identity,"WeaponHandle",(uint)32775);
    Set(identity,"Sensitivity",1.250F);Set(identity,"PawnMouseSensitivity",1F);
    Set(identity,"FovSensitivityAdjust",1F);Set(identity,"Ammo",30);
    Set(sample,"identity",identity);
    samples.Add(sample);
   }
   MethodInfo analyze = recorderType.GetMethod("Analyze", Static);
   object result = analyze.Invoke(null, new object[] { samples, "Synthetic export check" });
   Check(Field<int>(result, "Shots") == 5, "Shot reset does not subtract earlier shots");
   Check(Field<int>(result, "MissedShotUpdates") == 2, "Skipped shot updates detected");
   Check(Field<int>(result, "Samples") == 6, "Sample count retained");
   Check(Field<double>(result, "MaxGapMs") == 100 && Field<double>(result, "MedianGapMs") == 99,
    "Measured gaps reflect observed sample times");
   Check(Field<double>(result, "DurationMs") == 300, "Capture duration uses observed timeline");
   Check(Field<double>(result, "MaxReadMs") == 2.5, "Read latency reported");

   string testDirectory = Path.Combine(Path.GetTempPath(), "CS2ProbeSmoke_" + Guid.NewGuid().ToString("N"));
   object recorder = Activator.CreateInstance(recorderType, Member, null,
    new object[] { testDirectory, false, new Action<string>(delegate(string s) { }) }, null);
   Type gameType = program.GetType("RecoilProbe.Game", true);
   object game = FormatterServices.GetUninitializedObject(gameType);
   Set(game, "Build", 14188); Set(game, "ClientVersion", "synthetic"); Set(game, "EngineVersion", "synthetic");
   CultureInfo previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
   try {
    System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
    MethodInfo save = recorderType.GetMethod("Save", Member);
    object saved = save.Invoke(recorder, new object[] { game, samples, "Synthetic export check", false });
    string csv = Field<string>(saved, "CsvPath"), json = Field<string>(saved, "MetadataPath");
    Check(File.Exists(csv) && File.Exists(json), "CSV and JSON export succeeds");
    string[] rows = File.ReadAllLines(csv);
    Check(rows.Length == 7 && rows[0].Split(',').Length == 45, "CSV contains direct internal-state fields and all six samples");
    for (int i = 1; i < rows.Length; i++) Check(rows[i].Split(',').Length == 45, "CSV row " + i + " has all fields");
    Check(rows[1].StartsWith("-1,0.1,", StringComparison.Ordinal), "CSV baseline and decimal format are culture independent");
    Check(rows[2].Contains(",-1.25,2.5,0,"), "Angle signs and decimals are preserved");
    Dictionary<string, object> metadata = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(json));
    Check(Convert.ToInt32(metadata["observed_build"]) == 14188, "JSON records observed build");
    Check((bool)metadata["no_game_memory_writes"] && (bool)metadata["no_mouse_injection"], "JSON records read-only behavior");
    Check((string)metadata["weapon_detected"]=="AK47" && Convert.ToInt32(metadata["weapon_definition_index"])==7,
     "JSON records weapon identity from samples");
    Check(Convert.ToDouble(metadata["sensitivity_detected"])==1.25, "JSON records detected sensitivity");
    Check(!(bool)metadata["amc_generated"] && !(bool)metadata["instantaneous_bullet_recoil_reconstruction_verified"],
     "JSON keeps AMC and recoil reconstruction unverified");
    Check((bool)metadata["deterministic_recoil_state_directly_read"]&&
     !(bool)metadata["ballistic_trajectory_directly_read"]&&!(bool)metadata["server_spread_included"],
     "JSON distinguishes direct deterministic state from unread server trajectory and spread");
    object again = save.Invoke(recorder, new object[] { game, samples, "Second synthetic export", false });
    Check(Field<string>(again, "CsvPath") != csv && File.Exists(csv), "Repeated export creates a new recording");
   } finally { System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture; }

   Console.WriteLine("PASS: " + count + " automatic checks on Windows.");
   Console.WriteLine("CS2 was not run. Game capture and recoil correctness are unverified.");
   return 0;
  } catch (Exception ex) {
   Console.Error.WriteLine(ex); return 1;
  }
 }
}
