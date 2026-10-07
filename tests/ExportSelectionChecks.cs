using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;
using RecoilProbe;

internal static class ExportSelectionChecks {
 private static string Commands(string path) {
  XmlDocument doc=new XmlDocument();doc.Load(path);return doc.SelectSingleNode("//KeyDown/Syntax").InnerText;
 }
 private static bool SameXmlTrajectory(string a,string b) {
  XmlDocument left=new XmlDocument(),right=new XmlDocument();left.Load(a);right.Load(b);
  XmlNodeList la=left.SelectNodes("//Buffer"),lb=right.SelectNodes("//Buffer");
  if(la.Count!=lb.Count)return false;
  for(int i=0;i<la.Count;i++)foreach(string field in new string[] {"x","y","time"})
   if(la[i].SelectSingleNode(field).InnerText!=lb[i].SelectSingleNode(field).InnerText)return false;
  XmlNodeList da=left.SelectNodes("/Macro/MacroEvents/MacroEvent[Type='0']/Number"),db=right.SelectNodes("/Macro/MacroEvents/MacroEvent[Type='0']/Number");
  if(da.Count!=db.Count)return false;
  for(int i=0;i<da.Count;i++)if(da[i].InnerText!=db[i].InnerText)return false;
  return true;
 }
 private static WeaponSnapshot Snapshot(string path) {
  WeaponSnapshot input;
  if(!WeaponSnapshotIO.TryLoad(path,out input))throw new Exception("Snapshot fixture not loaded");return input;
 }
 internal static void Run(string output,RecordingData recording,Action<bool,string> check,Action<Action,string> reject) {
  string root=Path.Combine(output,"SelectionChecks"),source=Path.Combine(output,"AK47_NO_FIRE_SYNTHETIC_TEST.recoil.json"),parameters;
  string xmlRoot=Path.Combine(root,"no_fire_xml");
  MacroExportResult xml=MainForm.ExportSnapshot(Snapshot(source),Path.Combine(xmlRoot,"AK47.amc"),MacroFormat.XmlRazer,out parameters);
  check(xml.Xml!=null&&xml.Amc==null&&File.Exists(Path.Combine(xmlRoot,"XML","AK47.xml"))&&File.Exists(parameters),
   "Actual F8 export path with XML selected creates XML and its extracted parameters");
  check(!Directory.Exists(Path.Combine(xmlRoot,"AMC"))&&Directory.GetFiles(xmlRoot,"*.amc",SearchOption.AllDirectories).Length==0,
   "XML-only extraction does not require or leave a visible AMC output");
  check(SameXmlTrajectory(xml.OutputPath,Path.Combine(output,"AK47_NO_FIRE_RAZER.xml")),
   "F8 XML-only output preserves the existing AMC trajectory and calibrated timing exactly");
  check(!xml.Xml.MasterAmcStored&&xml.Xml.MasterAmcPath==null&&xml.Xml.MasterAmcSha256.Length==64,
   "XML report keeps the master checksum without referencing a deleted temporary path");
  string amcRoot=Path.Combine(root,"no_fire_amc");
  MacroExportResult amc=MainForm.ExportSnapshot(Snapshot(source),Path.Combine(amcRoot,"AK47.xml"),MacroFormat.Amc,out parameters);
  check(amc.Amc!=null&&amc.Xml==null&&File.Exists(Path.Combine(amcRoot,"AMC","AK47.amc"))&&!Directory.Exists(Path.Combine(amcRoot,"XML")),
   "Actual F8 export path with AMC selected creates only the selected macro format");
  check(Commands(amc.OutputPath)==Commands(Path.Combine(output,"AK47_NO_FIRE_SYNTHETIC_TEST.amc")),
   "AMC selection preserves the baseline generator command stream byte-for-byte");
  string jsonRoot=Path.Combine(root,"converter_json");
  MacroExportResult json=ConverterForm.ExportSelected(Snapshot(source),null,null,Path.Combine(jsonRoot,"AK47.xml"),1.25,MacroFormat.XmlRazer);
  check(File.Exists(json.OutputPath)&&!Directory.Exists(Path.Combine(jsonRoot,"AMC"))&&SameXmlTrajectory(json.OutputPath,xml.OutputPath),
   "Converter JSON export produces XML directly with the same calibrated no-fire trajectory");
  string goldMaster=Path.Combine(output,"M249_GOLDEN_DERIVED_TEST.amc"),before=File.ReadAllText(goldMaster);
  string directRoot=Path.Combine(root,"converter_existing_amc");
  MacroExportResult direct=ConverterForm.ExportSelected(null,AmcInput.Load(goldMaster),null,
   Path.Combine(directRoot,"M249.xml"),Double.NaN,MacroFormat.XmlRazer);
  check(SameXmlTrajectory(direct.OutputPath,Path.Combine(output,"M249_GOLDEN_EXPORT.xml"))&&File.ReadAllText(goldMaster)==before,
   "XML selection on an existing AMC bypasses sensitivity and smoothing and retains every golden M249 command");
  check(!Directory.Exists(Path.Combine(directRoot,"AMC"))&&direct.Xml.MasterAmcStored&&direct.Xml.MasterAmcPath==goldMaster,
   "Existing AMC remains the referenced master while only XML is exported");
  string recordingRoot=Path.Combine(root,"recorded_xml");
  MacroExportResult captured=MacroExport.Save(MacroFormat.XmlRazer,Path.Combine(recordingRoot,"AK47.amc"),
   delegate(string master){return AmcConverter.Convert(recording,master,recording.Sensitivity);});
  string expected=Path.Combine(output,"RECORDED_REFERENCE.xml");RazerXmlExporter.Export(Path.Combine(output,"AK47_FIXTURE_TEST.amc"),expected);
  check(SameXmlTrajectory(captured.OutputPath,expected)&&!Directory.Exists(Path.Combine(recordingRoot,"AMC")),
   "Recorder XML output uses the same selected-format pipeline and baseline recorded trajectory");
  string temporary=null;
  MacroExport.Save(MacroFormat.XmlRazer,Path.Combine(root,"temp_cleanup","AK47.xml"),delegate(string master) {
   temporary=Path.GetDirectoryName(master);return NoFireGenerator.Convert(Snapshot(source),master,1.25);
  });
  check(!Directory.Exists(temporary),"Generated temporary AMC and its report are removed after XML export");
  reject(delegate {
   MacroExport.Save(MacroFormat.XmlRazer,xml.OutputPath,delegate(string master) {
    temporary=Path.GetDirectoryName(master);return NoFireGenerator.Convert(Snapshot(source),master,1.25);
   });
  },"Selected-format XML export reports an existing output instead of silently returning an AMC success");
  check(!Directory.Exists(temporary),"Temporary AMC is also removed if XML export fails");
  BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
  Environment.SetEnvironmentVariable("CS2_PROBE_TEST_MODE","1");
  using(MainForm form=new MainForm()) {
   ComboBox choice=(ComboBox)typeof(MainForm).GetField("format",flags).GetValue(form);choice.SelectedIndex=1;
   using(ConverterForm next=new ConverterForm()) {
    check(((ComboBox)typeof(ConverterForm).GetField("format",flags).GetValue(next)).SelectedIndex==1&&
     ((Button)typeof(ConverterForm).GetField("convert",flags).GetValue(next)).Text=="ESPORTA XML RAZER",
     "The XML choice carries into a newly opened converter");
   }
   choice.SelectedIndex=0;
   using(RecorderForm next=new RecorderForm()) {
    check(((ComboBox)typeof(RecorderForm).GetField("format",flags).GetValue(next)).SelectedIndex==0,
     "Switching back to AMC carries into the newly opened recorder");
   }
  }
 }
}
