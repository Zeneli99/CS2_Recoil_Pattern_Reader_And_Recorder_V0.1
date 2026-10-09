using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using RecoilProbe;

internal static class LayoutChecks {
 internal static void Run(string fixtures, Action<bool,string> check) {
  Dictionary<string,object> manifest = new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(
   File.ReadAllText(Path.Combine(fixtures,"LAYOUT_14190.json")));
  check(Convert.ToInt32(manifest["build_number"],CultureInfo.InvariantCulture)==Layout.TargetBuild,
   "Layout target matches independently pinned dump build 14190");
  check((string)manifest["source_commit"]==Layout.SourceCommit,
   "Layout provenance matches the pinned 14190 dump commit");
  Dictionary<string,object> offsets=(Dictionary<string,object>)manifest["offsets"];
  Dictionary<string,object> candidates=(Dictionary<string,object>)manifest["non_schema_candidates"];
  BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
  foreach(KeyValuePair<string,object> entry in offsets) {
   FieldInfo field=typeof(Layout).GetField(entry.Key,flags);
   check(field!=null&&field.IsLiteral&&Convert.ToInt32(field.GetRawConstantValue(),CultureInfo.InvariantCulture)==
    Convert.ToInt32(entry.Value,CultureInfo.InvariantCulture),"Pinned 14190 schema/global: "+entry.Key);
  }
  foreach(KeyValuePair<string,object> entry in candidates) {
   FieldInfo field=typeof(Layout).GetField(entry.Key,flags);
   check(field!=null&&Convert.ToInt32(field.GetRawConstantValue(),CultureInfo.InvariantCulture)==
    Convert.ToInt32(entry.Value,CultureInfo.InvariantCulture),"Non-schema candidate retained with live validation: "+entry.Key);
  }
  bool covered=true;
  foreach(FieldInfo field in typeof(Layout).GetFields(flags))
   if(field.FieldType==typeof(int)&&field.Name!="TargetBuild")
    covered&=offsets.ContainsKey(field.Name)||candidates.ContainsKey(field.Name);
  check(covered&&offsets.Count==62&&candidates.Count==2,"Every layout offset has explicit provenance or candidate classification");
 }
}
