using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using RecoilProbe;

internal static class RazerChecks {
 private sealed class Trace {
  internal readonly List<int[]> Buffers=new List<int[]>();
  internal int Duration,ReleaseDelay,TotalX,TotalY;
 }
 private static int Num(XmlNode node,string field){return Int32.Parse(node.SelectSingleNode(field).InnerText,CultureInfo.InvariantCulture);}
 private static Trace ReadXml(string path) {
  XmlDocument doc=new XmlDocument();doc.Load(path);Trace t=new Trace();
  foreach(XmlNode node in doc.SelectNodes("/Macro/MacroEvents/MacroEvent[Type='3']/MouseEvent/Buffer")) {
   int time=Num(node,"time");t.Duration+=time;
   t.Buffers.Add(new int[] {Num(node,"x"),Num(node,"y"),time});
  }
  foreach(XmlNode node in doc.SelectNodes("/Macro/MacroEvents/MacroEvent[Type='0']/Number"))
   t.ReleaseDelay+=(int)(Decimal.Parse(node.InnerText,CultureInfo.InvariantCulture)*1000);
  int[] first=t.Buffers[0],last=t.Buffers[t.Buffers.Count-1];
  t.TotalX=last[0]-first[0];t.TotalY=last[1]-first[1];return t;
 }
 private static bool Equal(Trace a,Trace b) {
  if(a.Buffers.Count!=b.Buffers.Count||a.Duration!=b.Duration||a.ReleaseDelay!=b.ReleaseDelay)return false;
  for(int i=0;i<a.Buffers.Count;i++)for(int j=0;j<3;j++)if(a.Buffers[i][j]!=b.Buffers[i][j])return false;
  return true;
 }
 private static string Hash(string path) {
  using(SHA256 hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
 }
 private static void WriteAmc(string path,List<string> commands,int cost) {
  XmlWriterSettings settings=new XmlWriterSettings {Encoding=Encoding.Unicode,Indent=true};
  using(XmlWriter writer=XmlWriter.Create(path,settings)) {
   writer.WriteStartElement("Root");writer.WriteStartElement("DefaultMacro");
   writer.WriteElementString("Description","M249 · SENS 1.250 · golden-derived TEST fixture");
   writer.WriteElementString("Comment","MoveRCommandCostMs="+cost+"; Synthetic test, not the original user AMC.");
   writer.WriteStartElement("GUIOption");writer.WriteElementString("RepeatType","1");writer.WriteEndElement();
   writer.WriteStartElement("KeyUp");writer.WriteElementString("Syntax","LeftUp 1");writer.WriteEndElement();
   writer.WriteStartElement("KeyDown");writer.WriteElementString("Syntax",String.Join("\n",commands.ToArray()));writer.WriteEndElement();
   writer.WriteElementString("Software","Counter-Strike 2");writer.WriteEndElement();writer.WriteEndElement();
  }
 }
 // Independent command evaluator uses the verified physical clock, not AmcInput anchors.
 private static Trace ExpectedFromAmc(string path) {
  XmlDocument doc=new XmlDocument();doc.Load(path);Trace t=new Trace();
  t.Buffers.Add(new int[] {500,300,0});int x=500,y=300,wait=0;bool release=false;
  foreach(string raw in doc.SelectSingleNode("/Root/DefaultMacro/KeyDown/Syntax").InnerText.Split(new char[] {'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) {
   string[] p=raw.Trim().Split(new char[] {' ','\t'},StringSplitOptions.RemoveEmptyEntries);
   if(p[0]=="Delay"&&!release)wait+=Int32.Parse(p[1],CultureInfo.InvariantCulture);
   if(p[0]=="MoveR") {
    x+=Int32.Parse(p[1],CultureInfo.InvariantCulture);y+=Int32.Parse(p[2],CultureInfo.InvariantCulture);
    t.Buffers.Add(new int[] {x,y,wait+1});t.Duration+=wait+1;wait=0;
   }
   if(p[0]=="LeftUp"){release=true;t.ReleaseDelay=wait;wait=0;}
  }
  t.TotalX=x-500;t.TotalY=y-300;return t;
 }
 private static void Format(string xmlPath,Trace trace,Action<bool,string> check) {
  XmlDocument doc=new XmlDocument();doc.Load(xmlPath);
  check(doc.SelectSingleNode("/Macro/Version").InnerText=="4"&&
   doc.SelectSingleNode("/Macro/MouseMoveType").InnerText=="relative"&&
   doc.SelectSingleNode("/Macro/MacroEvents/MacroEvent[Type='actionBar']/recordProfile/mmtSetting").InnerText=="3",
   "Synapse 4 uses relative Start Point mouse cursor format (mmtSetting 3)");
  XmlNodeList events=doc.SelectNodes("/Macro/MacroEvents/MacroEvent");
  check(events[0].SelectSingleNode("Type").InnerText=="actionBar"&&
   events[1].SelectSingleNode("MouseEvent/State").InnerText=="0"&&
   events[1].SelectSingleNode("MouseEvent/MouseButton").InnerText=="0"&&
   events[2].SelectSingleNode("Type").InnerText=="3"&&
   events[events.Count-1].SelectSingleNode("MouseEvent/State").InnerText=="1"&&
   events[events.Count-1].SelectSingleNode("MouseEvent/MouseButton").InnerText=="0",
   "XML LeftDown precedes all buffers and LeftUp follows the release delay");
  check((int)(Decimal.Parse(events[2].SelectSingleNode("Number").InnerText,CultureInfo.InvariantCulture)*1000)==trace.Duration,
   "XML movement Number equals the sum of buffer time intervals");
  check(trace.Buffers[0][0]==500&&trace.Buffers[0][1]==300&&trace.Buffers[0][2]==0,
   "XML initial buffer matches verified M249 origin and has zero delay");
 }
 internal static void Run(string fixtures,string output,Action<bool,string> check,Action<Action,string> reject) {
  string golden=Path.Combine(fixtures,"M249_Razer_Synapse4_CAL1_MoveCost1ms.xml");
  check(Hash(golden)=="aa462a092e5fbb67b77a489fdcff9b33e9535d6299ba18abc7adad9e6dfba0f5","Attached verified M249 XML is pinned byte-for-byte");
  Trace reference=ReadXml(golden);
  check(reference.Buffers.Count==766&&reference.Duration==7920&&reference.TotalX==25&&reference.TotalY==448&&reference.ReleaseDelay==79,
   "Verified M249 golden: 765 moves, X+25/Y+448, 7920ms movement plus 79ms to release");
  // Original AMC download was unavailable. Invert the supplied golden for a clearly labelled fixture.
  List<string> commands=new List<string>();commands.Add("LeftDown 1");int delaySum=0;
  for(int i=1;i<reference.Buffers.Count;i++) {
   int[] before=reference.Buffers[i-1],at=reference.Buffers[i];int wait=at[2]-1;
   if(wait<0)throw new Exception("Golden buffer cannot represent a physical MoveR");
   delaySum+=wait;if(wait>0)commands.Add("Delay "+wait+" ms");
   commands.Add("MoveR "+(at[0]-before[0])+" "+(at[1]-before[1]));
  }
  commands.Add("Delay 79 ms");commands.Add("LeftUp 1");
  for(int i=0;i<30;i++)commands.Add("Delay 999 ms");commands.Add("Delay 30 ms");
  string master=Path.Combine(output,"M249_GOLDEN_DERIVED_TEST.amc");WriteAmc(master,commands,1);string beforeHash=Hash(master);
  RazerXmlResult result=RazerXmlExporter.Export(master,Path.Combine(output,"M249_GOLDEN_EXPORT.xml"));
  Trace actual=ReadXml(result.XmlPath);
  check(delaySum==7155&&result.DelayBeforeLastMoveMs==7155&&result.MoveCommands==765&&result.MovementDurationMs==7920,
   "M249 reconstructs 7155ms Delay plus 765 individual MoveR costs exactly once");
  check(Equal(actual,reference),"All 765 verified M249 X/Y deltas and buffer intervals match golden XML exactly");
  check(result.ReleaseTimeMs==7999&&result.ReleaseDelayMs==79&&result.IgnoredTailMs==30000&&actual.ReleaseDelay==79,
   "M249 retains 79ms before LeftUp and omits only the 30000ms post-release tail");
  check(beforeHash==Hash(master)&&result.MasterAmcSha256==beforeHash&&!result.CoordinatesScaled&&!result.MovementResampled,
   "XML export does not change AMC bytes, scale X/Y or resample any movement");
  Format(result.XmlPath,actual,check);
  List<string> edge=new List<string> {"LeftDown 1","Delay 9 ms","MoveR 127 -127","MoveR 127 -127",
   "MoveR 46 -46","Delay 3 ms","Delay 7 ms","MoveR -127 127","MoveR 0 0","Delay 17 ms","LeftUp 1","Delay 30000 ms"};
  string edge0=Path.Combine(output,"CONSECUTIVE_LEGACY.amc"),edge1=Path.Combine(output,"CONSECUTIVE_COST1.amc");
  WriteAmc(edge0,edge,0);WriteAmc(edge1,edge,1);
  RazerXmlResult e0=RazerXmlExporter.Export(edge0,Path.Combine(output,"CONSECUTIVE_LEGACY.xml"));
  RazerXmlResult e1=RazerXmlExporter.Export(edge1,Path.Combine(output,"CONSECUTIVE_COST1.xml"));
  Trace t0=ReadXml(e0.XmlPath),t1=ReadXml(e1.XmlPath);
  check(Equal(t0,t1)&&e1.MovementDurationMs==24&&e1.ReleaseTimeMs==41,
   "Existing MoveRCommandCostMs=1 does not add a second timing correction");
  check(t1.Buffers.Count==6&&t1.Buffers[1][2]==10&&t1.Buffers[2][2]==1&&t1.Buffers[3][2]==1&&
   t1.Buffers[4][2]==11&&t1.Buffers[5][2]==1&&t1.TotalX==173&&t1.TotalY== -173,
   "Adjacent chunked MoveR, multiple Delay and zero moves each retain their own 1ms cost");
  string smooth=Path.Combine(output,"AK47_SENS_1.250_SMOOTH_10MS.amc");string smoothHash=Hash(smooth);
  RazerXmlResult ak=RazerXmlExporter.Export(smooth,Path.Combine(output,"AK47_RAZER_FROM_AMC.xml"));
  check(Equal(ExpectedFromAmc(smooth),ReadXml(ak.XmlPath))&&smoothHash==Hash(smooth),
   "Legacy AMC XML preserves every emitted movement and uses its effective calibrated clock");
  string noFire=Path.Combine(output,"AK47_NO_FIRE_SYNTHETIC_TEST.amc");string nfHash=Hash(noFire);
  RazerXmlResult nf=RazerXmlExporter.Export(noFire,Path.Combine(output,"AK47_NO_FIRE_RAZER.xml"));
  AmcInput nfInput=AmcInput.Load(noFire);
  check(Equal(ExpectedFromAmc(noFire),ReadXml(nf.XmlPath))&&nf.ReleaseTimeMs==nfInput.ReleaseTime&&nfHash==Hash(noFire),
   "Existing no-fire compensated AMC exports to XML with exactly the same effective release time");
  string fixture=Path.Combine(fixtures,"AK47_ORIGINAL_50MS_V022.amc");
  AmcResult ui=ConverterForm.ExportMaster(null,AmcInput.Load(fixture),null,
   Path.Combine(output,"CONVERTER_MASTER.amc"),1.25);
  XmlDocument uiDoc=new XmlDocument(),baselineDoc=new XmlDocument();uiDoc.Load(ui.AmcPath);baselineDoc.Load(smooth);
  check(uiDoc.SelectSingleNode("//KeyDown/Syntax").InnerText==baselineDoc.SelectSingleNode("//KeyDown/Syntax").InnerText,
   "Converter integration still emits the unchanged baseline AMC command stream");
  string root=Path.Combine(output,"folders"),name=Path.Combine(root,"macro.amc");
  check(OutputFolders.Amc(name)==Path.Combine(root,"AMC","macro.amc")&&
   OutputFolders.Xml(OutputFolders.Amc(name))==Path.Combine(root,"XML","macro.xml")&&
   OutputFolders.Amc(OutputFolders.Amc(name))==OutputFolders.Amc(name)&&
   OutputFolders.Xml(Path.Combine(root,"XML","macro.xml"))==Path.Combine(root,"XML","macro.xml"),
   "AMC and XML folders are siblings; selecting either does not nest folders again");
  Environment.SetEnvironmentVariable("CS2_PROBE_TEST_MODE","1");
  using(RecorderForm form=new RecorderForm()) {
   System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
   System.Windows.Forms.CheckBox amcBox=(System.Windows.Forms.CheckBox)typeof(RecorderForm).GetField("autoAmc",flags).GetValue(form);
   System.Windows.Forms.CheckBox xmlBox=(System.Windows.Forms.CheckBox)typeof(RecorderForm).GetField("exportXml",flags).GetValue(form);
   check(amcBox.Checked&&xmlBox.Enabled&&!xmlBox.Checked,"Recorder starts with its AMC behavior preserved and XML optional");
   amcBox.Checked=false;check(!xmlBox.Enabled,"Recorder XML cannot be selected without an AMC master");
   amcBox.Checked=true;
   typeof(RecorderForm).GetMethod("SetExecutionTest",flags).Invoke(form,new object[] {AmcInput.Load(smooth)});
   check(!amcBox.Enabled&&!xmlBox.Enabled,"AMC execution diagnostic mode disables both output options");
   typeof(RecorderForm).GetMethod("SetExecutionTest",flags).Invoke(form,new object[] {null});
   check(amcBox.Enabled&&xmlBox.Enabled,"Leaving diagnostic mode restores optional XML export");
   form.Show();System.Windows.Forms.Application.DoEvents();form.Refresh();
   bool bounds=true;
   foreach(System.Windows.Forms.Control child in form.Controls)bounds&=child.Left>=0&&child.Top>=0&&child.Right<=540&&child.Bottom<=360;
   check(bounds,"Recorder XML option fits the existing compact 540x360 window");
   using(System.Drawing.Bitmap bitmap=new System.Drawing.Bitmap(form.Width,form.Height)) {
    form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"UI_RECORDER.png"));
   }
  }
  string zero=Path.Combine(output,"ZERO_RELEASE.amc");WriteAmc(zero,new List<string> {"LeftDown 1","MoveR 1 -1","LeftUp 1"},1);
  RazerXmlResult z=RazerXmlExporter.Export(zero,Path.Combine(output,"ZERO_RELEASE.xml"));
  check(z.MovementDurationMs==1&&z.ReleaseDelayMs==0&&ReadXml(z.XmlPath).ReleaseDelay==0,
   "Zero release delay is preserved without a guessed pause");
  CultureInfo saved=Thread.CurrentThread.CurrentCulture;
  try {
   Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("it-IT");
   RazerXmlResult it=RazerXmlExporter.Export(master,Path.Combine(output,"M249_ITALIAN.xml"));
   check(Equal(reference,ReadXml(it.XmlPath)),"Synapse seconds and integer buffers are culture independent");
  }finally{Thread.CurrentThread.CurrentCulture=saved;}
  string xmlHash=Hash(result.XmlPath),reportHash=Hash(result.ReportPath);
  reject(delegate{RazerXmlExporter.Export(master,result.XmlPath);},"Existing XML and report are never overwritten");
  check(xmlHash==Hash(result.XmlPath)&&reportHash==Hash(result.ReportPath)&&beforeHash==Hash(master),
   "Rejected overwrite preserves the XML, report and master AMC");
  string occupied=Path.Combine(output,"REPORT_OCCUPIED.xml");File.WriteAllText(Path.ChangeExtension(occupied,".report.json"),"keep");
  reject(delegate{RazerXmlExporter.Export(master,occupied);},"Report collision rejects XML before creating output");
  check(!File.Exists(occupied),"Rejected report collision leaves no partial XML");
  string broken=Path.Combine(output,"UNSUPPORTED.amc");WriteAmc(broken,new List<string> {"LeftDown 1","MoveR 1 1","KeyDown A","LeftUp 1"},1);
  reject(delegate{RazerXmlExporter.Export(broken,Path.Combine(output,"UNSUPPORTED.xml"));},"Unsupported AMC command is rejected instead of silently omitted");
  WriteAmc(broken,new List<string> {"LeftDown 1","MoveR 1 1"},1);
  reject(delegate{RazerXmlExporter.Export(broken,Path.Combine(output,"MISSING_RELEASE.xml"));},"Missing LeftUp cannot produce XML");
  WriteAmc(broken,new List<string> {"LeftDown 1","MoveR 1 1","LeftUp 1","MoveR 1 1"},1);
  reject(delegate{RazerXmlExporter.Export(broken,Path.Combine(output,"POST_RELEASE.xml"));},"Movement after release is rejected");
  File.WriteAllText(broken,"<!DOCTYPE Root [<!ENTITY x SYSTEM 'file:///no-such-file'>]><Root>&x;</Root>");
  reject(delegate{RazerXmlExporter.Export(broken,Path.Combine(output,"DTD.xml"));},"DTD input is rejected without external resolution");
 }
}
