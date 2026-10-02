using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Web.Script.Serialization;

namespace RecoilProbe {
 internal sealed class RecordingData {
  internal List<Sample> Samples = new List<Sample>();
  internal Dictionary<string,object> Metadata = new Dictionary<string,object>();
  internal string WeaponName, SourcePath;
  internal double Sensitivity;
  internal int WeaponDefinitionIndex;
  internal bool AutomaticIdentity;
  internal static RecordingData FromSamples(List<Sample> samples, Dictionary<string,object> metadata) {
   RecordingData data = new RecordingData { Samples = samples, Metadata = metadata };
   data.ResolveMetadata(); return data;
  }
  internal void ResolveMetadata() {
   if (Samples.Count == 0) throw new InvalidOperationException("Registrazione vuota.");
   GameIdentity first = null;
   foreach (Sample s in Samples) if (s.identity != null) {
    if (first == null) first = s.identity;
    else if (s.identity.WeaponHandle != first.WeaponHandle ||
     s.identity.ItemDefinitionIndex != first.ItemDefinitionIndex)
     throw new InvalidOperationException("Il file contiene un cambio d'arma.");
    else if (Math.Abs(s.identity.Sensitivity - first.Sensitivity) > 0.000001)
     throw new InvalidOperationException("La sensibilita' e' cambiata durante la registrazione.");
    if (s.identity.Scoped) throw new InvalidOperationException("Registrazione con zoom/ADS: ripeti senza mirino.");
   }
   object value;
   if (first != null) {
    WeaponDefinitionIndex = first.ItemDefinitionIndex;
    WeaponName = WeaponCatalog.Name(WeaponDefinitionIndex); Sensitivity = first.Sensitivity;
    AutomaticIdentity = Metadata.TryGetValue("weapon_identity_automatically_verified", out value) &&
     Convert.ToBoolean(value, CultureInfo.InvariantCulture);
   } else {
    if (Metadata.TryGetValue("sensitivity_detected", out value))
     Sensitivity = Convert.ToDouble(value, CultureInfo.InvariantCulture);
    else if (Metadata.TryGetValue("sensitivity_user_supplied", out value))
     Sensitivity = Convert.ToDouble(value, CultureInfo.InvariantCulture);
    else throw new InvalidOperationException("Sensibilita' assente: conserva il JSON insieme al CSV.");
    if (Metadata.TryGetValue("weapon_definition_index", out value)) {
     WeaponDefinitionIndex = Convert.ToInt32(value, CultureInfo.InvariantCulture);
     WeaponName = WeaponCatalog.Name(WeaponDefinitionIndex);
    } else if (Metadata.TryGetValue("weapon_label_user_supplied", out value))
     WeaponName = Convert.ToString(value, CultureInfo.InvariantCulture);
    else if (Metadata.TryGetValue("weapon_detected", out value))
     WeaponName = Convert.ToString(value, CultureInfo.InvariantCulture);
    else throw new InvalidOperationException("Nome dell'arma assente nel JSON.");
   }
   if (first != null) {
    if (Metadata.TryGetValue("sensitivity_detected", out value) &&
     Math.Abs(Convert.ToDouble(value, CultureInfo.InvariantCulture)-Sensitivity)>0.000001)
     throw new InvalidOperationException("Sensibilita' discordante fra CSV e JSON.");
    if (Metadata.TryGetValue("weapon_definition_index", out value) &&
     Convert.ToInt32(value, CultureInfo.InvariantCulture)!=WeaponDefinitionIndex)
     throw new InvalidOperationException("Arma discordante fra CSV e JSON.");
   }
   if (!IdentityReader.ValidSensitivity(Sensitivity))
    throw new InvalidOperationException("Sensibilita' del file non valida.");
   if (String.IsNullOrWhiteSpace(WeaponName)) throw new InvalidOperationException("Nome dell'arma assente.");
   if (Metadata.TryGetValue("capture_settings_changed", out value) && Convert.ToBoolean(value))
    throw new InvalidOperationException("Registrazione interrotta per cambio arma/sensibilita': ripeti lo spray.");
  }
 }
 internal static class RecordingIO {
  internal const int MaxBytes = 20 * 1024 * 1024;
  internal static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
  internal static JavaScriptSerializer Serializer() {
   return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 30 };
  }
  internal static RecordingData Load(string input) {
   string path = Path.GetFullPath(input), csv, json;
   string extension = Path.GetExtension(path).ToLowerInvariant();
   if (extension == ".zip") {
    using (FileStream file = File.OpenRead(path))
    using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Read)) {
     ZipArchiveEntry selected = null;
     foreach (ZipArchiveEntry entry in zip.Entries)
      if (entry.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) {
       if (selected != null)
        throw new InvalidOperationException("Lo ZIP contiene piu' registrazioni. Scegli un solo CSV con il suo JSON.");
       selected = entry;
      }
     if (selected == null) throw new InvalidOperationException("Nessun CSV di registrazione nello ZIP.");
     string jsonName = selected.FullName.Substring(0, selected.FullName.Length - 4) + ".json";
     ZipArchiveEntry companion = null;
     foreach (ZipArchiveEntry entry in zip.Entries)
      if (String.Equals(entry.FullName, jsonName, StringComparison.OrdinalIgnoreCase)) {
       if (companion != null) throw new InvalidOperationException("JSON duplicato nello ZIP.");
       companion = entry;
      }
     if (companion == null) throw new InvalidOperationException("Il JSON associato al CSV manca nello ZIP.");
     csv = ReadEntry(selected, MaxBytes); json = ReadEntry(companion, 1024 * 1024);
    }
   } else if (extension == ".csv" || extension == ".json") {
    string csvPath = Path.ChangeExtension(path, ".csv"), jsonPath = Path.ChangeExtension(path, ".json");
    if (!File.Exists(csvPath) || !File.Exists(jsonPath))
     throw new InvalidOperationException("Servono il CSV e il JSON con lo stesso nome nella stessa cartella.");
    if (new FileInfo(csvPath).Length > MaxBytes || new FileInfo(jsonPath).Length > 1024 * 1024)
     throw new InvalidOperationException("Registrazione troppo grande.");
    csv = File.ReadAllText(csvPath); json = File.ReadAllText(jsonPath);
   } else throw new InvalidOperationException("Scegli una registrazione ZIP, CSV oppure JSON.");
   RecordingData result = Parse(csv, json); result.SourcePath = path; return result;
  }
  private static string ReadEntry(ZipArchiveEntry entry, int limit) {
   if (entry.Length > limit) throw new InvalidOperationException("File nello ZIP troppo grande.");
   using (Stream stream = entry.Open())
   using (MemoryStream copy = new MemoryStream()) {
    byte[] buffer = new byte[8192]; int count;
    while ((count = stream.Read(buffer, 0, buffer.Length)) > 0) {
     if (copy.Length + count > limit) throw new InvalidOperationException("Limite ZIP superato.");
     copy.Write(buffer, 0, count);
    }
    copy.Position = 0;
    using (StreamReader reader = new StreamReader(copy, Encoding.UTF8, true))
     return reader.ReadToEnd();
   }
  }
  internal static RecordingData Parse(string csv, string json) {
   RecordingData result = new RecordingData();
   result.Metadata = Serializer().Deserialize<Dictionary<string,object>>(json);
   if (result.Metadata == null) throw new InvalidOperationException("JSON non valido.");
   using (StringReader reader = new StringReader(csv)) {
    string header = reader.ReadLine();
    if (header == null) throw new InvalidOperationException("CSV vuoto.");
    string[] names = header.TrimStart('\uFEFF').Split(',');
    Dictionary<string,int> columns = new Dictionary<string,int>(StringComparer.Ordinal);
    for (int i=0;i<names.Length;i++) {
     if (columns.ContainsKey(names[i])) throw new InvalidOperationException("Colonna CSV duplicata.");
     columns.Add(names[i], i);
    }
    foreach (string key in new string[] {"observed_ms","shots_fired","predictable_tick",
     "predictable_tick_fraction","predictable_pitch","predictable_yaw","left_down"})
     if (!columns.ContainsKey(key)) throw new InvalidOperationException("Colonna CSV assente: " + key);
    bool identityColumns = columns.ContainsKey("weapon_definition_index") &&
     columns.ContainsKey("weapon_handle") && columns.ContainsKey("game_sensitivity") &&
     columns.ContainsKey("is_scoped");
    string line; double previousTime = Double.NegativeInfinity;
    while ((line = reader.ReadLine()) != null) {
     if (String.IsNullOrWhiteSpace(line)) continue;
     if (result.Samples.Count >= 30000) throw new InvalidOperationException("Troppi campioni nel CSV.");
     string[] row = line.Split(',');
     if (row.Length != names.Length) throw new InvalidOperationException("Numero campi CSV non valido.");
     Sample s = new Sample();
     s.observed_ms = Number(row, columns, "observed_ms", 0);
     if (s.observed_ms < previousTime) throw new InvalidOperationException("Tempi CSV non ordinati.");
     previousTime = s.observed_ms;
     s.read_duration_ms = Number(row, columns, "read_duration_ms", 0);
     s.shots_fired = Integer(row, columns, "shots_fired", 0);
     s.predictable_tick = Integer(row, columns, "predictable_tick", 0);
     s.predictable_tick_fraction = (float)Number(row, columns, "predictable_tick_fraction", 0);
     s.predictable_angle = ReadVector(row, columns, "predictable", "");
     s.predictable_velocity = ReadVector(row, columns, "predictable_velocity", "");
     s.unpredictable_tick = Integer(row, columns, "unpredictable_tick", 0);
     s.unpredictable_angle = ReadVector(row, columns, "unpredictable", "");
     s.view_angle = ReadVector(row, columns, "view", "");
     s.eye_angle = ReadVector(row, columns, "eye", "");
     s.game_last_fired_time = (float)Number(row, columns, "game_last_fired_time", 0);
     s.client_tick = Integer(row, columns, "client_tick", 0);
     s.controller_tick = Integer(row, columns, "controller_tick", 0);
     s.weapon_hash = Unsigned(row, columns, "weapon_hash", 0);
     s.left_down = Flag(row, columns, "left_down", false);
     if (identityColumns) {
      s.identity = new GameIdentity {
       ItemDefinitionIndex = Integer(row,columns,"weapon_definition_index",0),
       WeaponHandle = Unsigned(row,columns,"weapon_handle",0),
       Sensitivity = (float)Number(row,columns,"game_sensitivity",0),
       PawnMouseSensitivity = (float)Number(row,columns,"pawn_mouse_sensitivity",1),
       FovSensitivityAdjust = (float)Number(row,columns,"fov_sensitivity_adjust",1),
       Scoped = Flag(row,columns,"is_scoped",false),
       Ammo = Integer(row,columns,"ammo",0)
      };
     }
     if (s.shots_fired < 0 || s.shots_fired > 1000 ||
      s.predictable_tick < -1 || s.predictable_tick_fraction < 0 || s.predictable_tick_fraction >= 1)
      throw new InvalidOperationException("Valori del colpo/tick non plausibili nel CSV.");
     s.predictable_angle.Validate(180); s.predictable_velocity.Validate(2000);
     s.unpredictable_angle.Validate(180);s.eye_angle.Validate(1000);s.view_angle.Validate(1000);
     result.Samples.Add(s);
    }
   }
   result.ResolveMetadata(); return result;
  }
  private static Vector ReadVector(string[] row, Dictionary<string,int> columns, string prefix, string suffix) {
   return new Vector { pitch=(float)Number(row,columns,prefix+"_pitch",0),
    yaw=(float)Number(row,columns,prefix+"_yaw",0),roll=(float)Number(row,columns,prefix+"_roll",0) };
  }
  private static double Number(string[] row, Dictionary<string,int> columns, string key, double fallback) {
   int index; if (!columns.TryGetValue(key,out index)) return fallback;
   double value;
   if (!Double.TryParse(row[index],NumberStyles.Float,Invariant,out value) ||
    Double.IsNaN(value) || Double.IsInfinity(value))
    throw new InvalidOperationException("Numero CSV non valido: " + key);
   return value;
  }
  private static int Integer(string[] row, Dictionary<string,int> columns, string key, int fallback) {
   int index,value; if (!columns.TryGetValue(key,out index)) return fallback;
   if (!Int32.TryParse(row[index],NumberStyles.Integer,Invariant,out value))
    throw new InvalidOperationException("Intero CSV non valido: " + key);
   return value;
  }
  private static uint Unsigned(string[] row, Dictionary<string,int> columns, string key, uint fallback) {
   int index; uint value; if (!columns.TryGetValue(key,out index)) return fallback;
   if (!UInt32.TryParse(row[index],NumberStyles.Integer,Invariant,out value))
    throw new InvalidOperationException("Handle/hash CSV non valido: " + key);
   return value;
  }
  private static bool Flag(string[] row, Dictionary<string,int> columns, string key, bool fallback) {
   int index; if (!columns.TryGetValue(key,out index)) return fallback;
   if (row[index]!="0" && row[index]!="1") throw new InvalidOperationException("Flag CSV non valido: "+key);
   return row[index]=="1";
  }
  internal static void WriteCsv(string path, List<Sample> samples) {
   using(FileStream stream = new FileStream(path,FileMode.CreateNew,FileAccess.Write))
   using(StreamWriter writer = new StreamWriter(stream,new UTF8Encoding(false))) {
    writer.WriteLine("observed_ms,read_duration_ms,shots_fired,client_tick,controller_tick," +
     "weapon_hash,game_last_fired_time,predictable_tick,predictable_tick_fraction," +
     "predictable_pitch,predictable_yaw,predictable_roll," +
     "predictable_velocity_pitch,predictable_velocity_yaw,predictable_velocity_roll," +
     "unpredictable_tick,unpredictable_pitch,unpredictable_yaw,unpredictable_roll," +
     "view_pitch,view_yaw,view_roll,eye_pitch,eye_yaw,eye_roll,left_down,shot_update," +
     "weapon_definition_index,weapon_handle,game_sensitivity,pawn_mouse_sensitivity," +
     "fov_sensitivity_adjust,is_scoped,ammo");
    int highWater = samples[0].shots_fired;
    foreach(Sample s in samples) {
     bool update = s.shots_fired > highWater; highWater=Math.Max(highWater,s.shots_fired);
     List<string> values = new List<string>();
     Add(values,s.observed_ms); Add(values,s.read_duration_ms); Add(values,s.shots_fired);
     Add(values,s.client_tick);Add(values,s.controller_tick);Add(values,s.weapon_hash);
     Add(values,s.game_last_fired_time);Add(values,s.predictable_tick);Add(values,s.predictable_tick_fraction);
     AddVector(values,s.predictable_angle);AddVector(values,s.predictable_velocity);
     Add(values,s.unpredictable_tick);AddVector(values,s.unpredictable_angle);
     AddVector(values,s.view_angle);AddVector(values,s.eye_angle);
     values.Add(s.left_down?"1":"0");values.Add(update?"1":"0");
     if(s.identity==null)throw new InvalidOperationException("Identita' automatica mancante nel campione.");
     Add(values,s.identity.ItemDefinitionIndex);Add(values,s.identity.WeaponHandle);
     Add(values,s.identity.Sensitivity);Add(values,s.identity.PawnMouseSensitivity);
     Add(values,s.identity.FovSensitivityAdjust);values.Add(s.identity.Scoped?"1":"0");Add(values,s.identity.Ammo);
     writer.WriteLine(String.Join(",",values.ToArray()));
    }
   }
  }
  private static void Add(List<string> list,object value) {
   if(value is float)list.Add(((float)value).ToString("R",Invariant));
   else if(value is double)list.Add(((double)value).ToString("R",Invariant));
   else list.Add(Convert.ToString(value,Invariant));
  }
  private static void AddVector(List<string> list,Vector v) { Add(list,v.pitch);Add(list,v.yaw);Add(list,v.roll); }
  internal static void WriteJson(string path,object data) {
   using(FileStream stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write))
   using(StreamWriter writer=new StreamWriter(stream,new UTF8Encoding(false)))
    writer.Write(Serializer().Serialize(data));
  }
 }
}
