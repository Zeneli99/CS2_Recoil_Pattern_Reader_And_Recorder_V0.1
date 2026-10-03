using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace RecoilProbe {
 internal sealed class MouseAnchor {
  internal int Time,X,Y;
 }
 internal sealed class AmcInput {
  internal string SourcePath,WeaponName;
  internal double Sensitivity;
  internal int ReleaseTime,TailMs,MoveCommands,MoveRCommandCostMs;
  internal string CommandTimingConvention = "Delay-only legacy timeline; command runtime is not measured.";
  internal readonly List<MouseAnchor> Anchors=new List<MouseAnchor>();
  private static string One(XmlNode node,string path) {
   XmlNodeList found=node.SelectNodes(path);
   if(found.Count!=1)throw new InvalidOperationException("Campo AMC assente o duplicato: "+path);
   return found[0].InnerText;
  }
  internal static AmcInput Load(string path) {
   string full=Path.GetFullPath(path);
   if(!String.Equals(Path.GetExtension(full),".amc",StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Seleziona un file .amc.");
   using(FileStream stream=new FileStream(full,FileMode.Open,FileAccess.Read)) {
    if(stream.Length>2*1024*1024)throw new InvalidOperationException("AMC oltre il limite di 2 MB.");
    XmlReaderSettings settings=new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,
     XmlResolver=null,MaxCharactersInDocument=2*1024*1024};
    try {
     using(XmlReader reader=XmlReader.Create(stream,settings)) {
      XmlDocument xml=new XmlDocument();xml.XmlResolver=null;xml.Load(reader);
      return Parse(xml,full);
     }
    } catch(XmlException ex) {throw new InvalidOperationException("XML AMC non valido.",ex);}
   }
  }
  private static int Number(string value) {
   int number;
   if(!Int32.TryParse(value,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out number))
    throw new InvalidOperationException("Numero AMC non valido.");
   return number;
  }
  private static AmcInput Parse(XmlDocument xml,string path) {
   XmlNodeList macros=xml.SelectNodes("/Root/DefaultMacro");
   if(macros.Count!=1)throw new InvalidOperationException("Serve un solo DefaultMacro AMC.");
   XmlNode macro=macros[0];
   if(One(macro,"Software").Trim()!="Counter-Strike 2")
    throw new InvalidOperationException("Apri un AMC CS2 generato da questo recorder.");
   if(One(macro,"GUIOption/RepeatType").Trim()!="1"||
    One(macro,"KeyUp/Syntax").Trim()!="LeftUp 1")
    throw new InvalidOperationException("L'AMC deve usare click hold e rilascio LeftUp.");
   Match header=Regex.Match(One(macro,"Description"),
    @"^\s*([A-Za-z0-9_-]{1,40})\s*·\s*SENS\s+(\d+\.\d{3,6})(?:\s*·|\s*$)");
   double sensitivity;
   if(!header.Success||!Double.TryParse(header.Groups[2].Value,NumberStyles.AllowDecimalPoint,
    CultureInfo.InvariantCulture,out sensitivity)||!IdentityReader.ValidSensitivity(sensitivity))
    throw new InvalidOperationException("Sensibilita' AMC assente: serve l'intestazione ARMA · SENS 1.250.");
   AmcInput data=new AmcInput {SourcePath=path,WeaponName=header.Groups[1].Value,Sensitivity=sensitivity};
   XmlNodeList comments=macro.SelectNodes("Comment");
   if(comments.Count>1)throw new InvalidOperationException("Comment AMC duplicato.");
   MatchCollection timing=Regex.Matches(comments.Count==0?"":comments[0].InnerText,
    @"\bMoveRCommandCostMs\s*=\s*([^;\s]+)");
   if(timing.Count>1)throw new InvalidOperationException("Convenzione tempi AMC duplicata.");
   if(timing.Count==1) {
    data.MoveRCommandCostMs=Number(timing[0].Groups[1].Value);
    if(data.MoveRCommandCostMs<0||data.MoveRCommandCostMs>1)
     throw new InvalidOperationException("Costo MoveR AMC non supportato: usa 0 o 1 ms.");
    if(data.MoveRCommandCostMs==1)data.CommandTimingConvention=AmcConverter.CompensatedTimingConvention;
   }
   int time=0,x=0,y=0,lines=0;bool down=false,released=false;
   foreach(string raw in One(macro,"KeyDown/Syntax").Split(new string[] {"\r\n","\n"},StringSplitOptions.RemoveEmptyEntries)) {
    string line=raw.Trim();if(line.Length==0)continue;
    if(++lines>10000)throw new InvalidOperationException("Troppi comandi nell'AMC.");
    string[] p=line.Split(new char[] {' ','\t'},StringSplitOptions.RemoveEmptyEntries);
    if(p[0]=="LeftDown"&&p.Length==2&&p[1]=="1") {
     if(down||released||lines!=1)throw new InvalidOperationException("LeftDown AMC fuori posto.");
     down=true;
    } else if(p[0]=="LeftUp"&&p.Length==2&&p[1]=="1") {
     if(!down||released)throw new InvalidOperationException("LeftUp AMC fuori posto.");
     down=false;released=true;data.ReleaseTime=time;
    } else if(p[0]=="Delay"&&p.Length==3&&p[2]=="ms") {
     int delay=Number(p[1]);
     if(delay<1||delay>999||(!down&&!released))throw new InvalidOperationException("Delay AMC non valido.");
     time+=delay;if(released)data.TailMs+=delay;
     if((!released&&time>20000)||(released&&data.TailMs>30000))
      throw new InvalidOperationException("Durata AMC oltre i limiti.");
    } else if(p[0]=="MoveR"&&p.Length==3) {
     if(!down||released)throw new InvalidOperationException("MoveR deve essere dentro il click hold.");
     int dx=Number(p[1]),dy=Number(p[2]);
     if(dx< -127||dx>127||dy< -127||dy>127||(dx==0&&dy==0))
      throw new InvalidOperationException("MoveR AMC non valido.");
     time+=data.MoveRCommandCostMs;
     if(time>20000)throw new InvalidOperationException("Durata AMC oltre i limiti.");
     x+=dx;y+=dy;data.MoveCommands++;
     if(Math.Abs(x)>10000000||Math.Abs(y)>10000000)
      throw new InvalidOperationException("Movimento AMC fuori scala.");
     if(data.Anchors.Count>0&&data.Anchors[data.Anchors.Count-1].Time==time) {
      MouseAnchor last=data.Anchors[data.Anchors.Count-1];last.X=x;last.Y=y;
     } else data.Anchors.Add(new MouseAnchor {Time=time,X=x,Y=y});
    } else throw new InvalidOperationException("Comando AMC non supportato: "+p[0]);
   }
   if(!released||down||data.Anchors.Count==0||data.TailMs!=30000)
    throw new InvalidOperationException("AMC incompleto: servono movimento, LeftUp e pausa finale 30000 ms.");
   return data;
  }
 }
}
