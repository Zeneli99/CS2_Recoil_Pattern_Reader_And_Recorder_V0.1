using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace RecoilProbe {
 internal static class OutputFolders {
  private static string InFolder(string selection,string folder,string extension) {
   string full=Path.GetFullPath(selection),parent=Path.GetDirectoryName(full);
   string leaf=new DirectoryInfo(parent).Name;
   if(String.Equals(leaf,"AMC",StringComparison.OrdinalIgnoreCase)||
    String.Equals(leaf,"XML",StringComparison.OrdinalIgnoreCase))parent=Path.GetDirectoryName(parent);
   return Path.Combine(parent,folder,Path.GetFileNameWithoutExtension(full)+extension);
  }
  internal static string Amc(string selection){return InFolder(selection,"AMC",".amc");}
  internal static string Xml(string selection){return InFolder(selection,"XML",".xml");}
 }
 internal sealed class RazerXmlResult {
  public string XmlPath,ReportPath,MasterAmcPath,MasterAmcSha256;
  public string Format="Razer Synapse 4",MouseMoveType="relative";
  public int MmtSetting=3,MoveRCommandCostMs=1,MoveCommands,TotalX,TotalY;
  public int DelayBeforeLastMoveMs,MovementDurationMs,ReleaseDelayMs,ReleaseTimeMs,IgnoredTailMs;
  public bool CoordinatesScaled=false,MovementResampled=false,MasterAmcModified=false;
  public bool MasterAmcStored=true;
  public string Timing="Raw AMC delays plus 1 ms for EACH MoveR. The AMC timing tag is not applied again.";
  public string Calibration="User-verified M249 Synapse 4 reference: 765 MoveR, +25/+448, 7155+765=7920 ms movement, then 79 ms release delay.";
 }
 internal static class RazerXmlExporter {
  private sealed class Buffer {internal int X,Y,Time;}
  private const int MaxBytes=2*1024*1024;
  private static string One(XmlNode node,string path) {
   XmlNodeList found=node.SelectNodes(path);
   if(found.Count!=1)throw new InvalidOperationException("Campo AMC assente o duplicato: "+path);
   return found[0].InnerText.Trim();
  }
  private static int Number(string value) {
   int number;
   if(!Int32.TryParse(value,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out number))
    throw new InvalidOperationException("Numero AMC non valido.");
   return number;
  }
  private static List<Buffer> Read(byte[] bytes,RazerXmlResult result) {
   XmlDocument xml=new XmlDocument();xml.XmlResolver=null;
   XmlReaderSettings settings=new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,
    XmlResolver=null,MaxCharactersInDocument=MaxBytes};
   try {
    using(MemoryStream stream=new MemoryStream(bytes))
    using(XmlReader reader=XmlReader.Create(stream,settings))xml.Load(reader);
   } catch(XmlException ex){throw new InvalidOperationException("XML AMC non valido.",ex);}
   XmlNodeList macros=xml.SelectNodes("/Root/DefaultMacro");
   if(macros.Count!=1)throw new InvalidOperationException("Serve un solo DefaultMacro AMC.");
   XmlNode macro=macros[0];
   if(One(macro,"GUIOption/RepeatType")!="1"||One(macro,"KeyUp/Syntax")!="LeftUp 1")
    throw new InvalidOperationException("L'AMC deve usare click hold e rilascio LeftUp.");
   List<Buffer> buffers=new List<Buffer>();buffers.Add(new Buffer {X=500,Y=300,Time=0});
   int pending=0,x=500,y=300,lines=0;bool down=false,released=false;
   foreach(string raw in One(macro,"KeyDown/Syntax").Split(new char[] {'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) {
    string[] p=raw.Split(new char[] {' ','\t'},StringSplitOptions.RemoveEmptyEntries);
    if(p.Length==0)continue;
    if(++lines>30000)throw new InvalidOperationException("Troppi comandi nell'AMC.");
    if(p[0]=="LeftDown"&&p.Length==2&&p[1]=="1") {
     if(down||released||lines!=1)throw new InvalidOperationException("LeftDown AMC fuori posto.");
     down=true;
    } else if(p[0]=="Delay"&&p.Length==3&&p[2]=="ms") {
     int delay=Number(p[1]);
     if(delay<0||delay>30000||(!down&&!released))throw new InvalidOperationException("Delay AMC non valido.");
     if(released) {
      result.IgnoredTailMs=checked(result.IgnoredTailMs+delay);
      if(result.IgnoredTailMs>30000)throw new InvalidOperationException("Pausa AMC oltre 30000 ms.");
     } else {
      pending=checked(pending+delay);
      if(result.MovementDurationMs+pending>20000)throw new InvalidOperationException("Durata AMC oltre 20 secondi.");
     }
    } else if(p[0]=="MoveR"&&p.Length==3) {
     if(!down||released)throw new InvalidOperationException("MoveR deve essere dentro il click hold.");
     int dx=Number(p[1]),dy=Number(p[2]);
     if(dx< -127||dx>127||dy< -127||dy>127)throw new InvalidOperationException("MoveR AMC fuori scala.");
     // Use raw Delay commands, not AmcInput anchors: they can merge adjacent moves.
     // Even a compensated AMC already budgets 1ms per MoveR; rebuild that clock ONCE.
     int time=pending+1;x+=dx;y+=dy;result.MoveCommands++;
     result.DelayBeforeLastMoveMs+=pending;result.MovementDurationMs+=time;pending=0;
     if(result.MoveCommands>10000||result.MovementDurationMs>20000)
      throw new InvalidOperationException("Traiettoria AMC oltre i limiti.");
     buffers.Add(new Buffer {X=x,Y=y,Time=time});
    } else if(p[0]=="LeftUp"&&p.Length==2&&p[1]=="1") {
     if(!down||released||result.MoveCommands==0)throw new InvalidOperationException("LeftUp AMC fuori posto.");
     down=false;released=true;result.ReleaseDelayMs=pending;pending=0;
    } else throw new InvalidOperationException("Comando AMC non supportato: "+p[0]);
   }
   if(!released||down)throw new InvalidOperationException("AMC incompleto: manca LeftUp.");
   result.TotalX=x-500;result.TotalY=y-300;
   result.ReleaseTimeMs=result.MovementDurationMs+result.ReleaseDelayMs;
   return buffers;
  }
  private static string Int(int number){return number.ToString(CultureInfo.InvariantCulture);}
  private static string Seconds(int ms){return (ms/1000.0).ToString("0.000",CultureInfo.InvariantCulture);}
  private static void Selected(XmlWriter writer){writer.WriteElementString("selected","false");}
  private static void Click(XmlWriter writer,string id,string state) {
   writer.WriteStartElement("MacroEvent");writer.WriteElementString("Type","2");writer.WriteElementString("Id",id);
   writer.WriteStartElement("MouseEvent");writer.WriteElementString("MouseButton","0");
   writer.WriteElementString("State",state);writer.WriteEndElement();writer.WriteElementString("flag","");
   Selected(writer);writer.WriteElementString("isPairing","false");writer.WriteEndElement();
  }
  internal static RazerXmlResult Export(string masterAmc,string output) {
   return Export(masterAmc,output,false);
  }
  internal static RazerXmlResult Export(string masterAmc,string output,bool temporaryMaster) {
   string master=Path.GetFullPath(masterAmc),path=Path.GetFullPath(output);
   if(!String.Equals(Path.GetExtension(master),".amc",StringComparison.OrdinalIgnoreCase)||
    !String.Equals(Path.GetExtension(path),".xml",StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Servono un master .amc e un output .xml.");
   byte[] input;
   using(FileStream stream=new FileStream(master,FileMode.Open,FileAccess.Read,FileShare.Read)) {
    if(stream.Length>MaxBytes)throw new InvalidOperationException("AMC oltre il limite di 2 MB.");
    using(MemoryStream copy=new MemoryStream()){stream.CopyTo(copy);input=copy.ToArray();}
   }
   RazerXmlResult result=new RazerXmlResult {MasterAmcPath=master,XmlPath=path,
    ReportPath=Path.ChangeExtension(path,".report.json"),MasterAmcStored=!temporaryMaster};
   if(temporaryMaster)result.MasterAmcPath=null;
   using(SHA256 hash=SHA256.Create())result.MasterAmcSha256=BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();
   List<Buffer> buffers=Read(input,result);
   if(File.Exists(path)||File.Exists(result.ReportPath))throw new InvalidOperationException("Output XML gia' esistente. Scegli un nome nuovo.");
   byte[] content;
   using(MemoryStream bytes=new MemoryStream()) {
    XmlWriterSettings settings=new XmlWriterSettings {Encoding=new UTF8Encoding(false),Indent=true,
     IndentChars="   ",OmitXmlDeclaration=true,NewLineChars="\r\n"};
    using(XmlWriter writer=XmlWriter.Create(bytes,settings)) {
     writer.WriteStartElement("Macro");writer.WriteElementString("Name",Path.GetFileNameWithoutExtension(path));
     writer.WriteStartElement("MacroEvents");writer.WriteStartElement("MacroEvent");
     writer.WriteElementString("Type","actionBar");writer.WriteStartElement("recordProfile");
     writer.WriteElementString("mmtSetting","3");writer.WriteEndElement();Selected(writer);writer.WriteEndElement();
     string id=((DateTime.UtcNow.Ticks-new DateTime(1970,1,1).Ticks)/TimeSpan.TicksPerMillisecond).ToString(CultureInfo.InvariantCulture);
     Click(writer,id,"0");
     writer.WriteStartElement("MacroEvent");writer.WriteElementString("Type","3");writer.WriteElementString("Id","");
     writer.WriteElementString("Number",Seconds(result.MovementDurationMs));writer.WriteStartElement("MouseEvent");
     foreach(Buffer buffer in buffers) {
      writer.WriteStartElement("Buffer");writer.WriteElementString("x",Int(buffer.X));writer.WriteElementString("y",Int(buffer.Y));
      writer.WriteElementString("time",Int(buffer.Time));writer.WriteEndElement();
     }
     writer.WriteEndElement();Selected(writer);writer.WriteEndElement();
     if(result.ReleaseDelayMs>0) {
      writer.WriteStartElement("MacroEvent");writer.WriteElementString("Type","0");
      writer.WriteElementString("Number",Seconds(result.ReleaseDelayMs));Selected(writer);writer.WriteEndElement();
     }
     Click(writer,id,"1");writer.WriteEndElement();writer.WriteElementString("DelaySetting","0");
     writer.WriteElementString("Guid",Guid.NewGuid().ToString());writer.WriteElementString("Version","4");
     writer.WriteElementString("MouseMoveType","relative");writer.WriteEndElement();
    }
    content=bytes.ToArray();
   }
   string report=RecordingIO.Serializer().Serialize(result);
   Directory.CreateDirectory(Path.GetDirectoryName(path));
   using(FileStream file=new FileStream(path,FileMode.CreateNew,FileAccess.Write))file.Write(content,0,content.Length);
   try {
    using(FileStream file=new FileStream(result.ReportPath,FileMode.CreateNew,FileAccess.Write))
    using(StreamWriter writer=new StreamWriter(file,new UTF8Encoding(false)))writer.Write(report);
   } catch {File.Delete(path);throw;}
   return result;
  }
 }
}
