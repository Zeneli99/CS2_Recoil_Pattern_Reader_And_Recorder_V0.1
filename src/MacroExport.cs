using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RecoilProbe {
 internal enum MacroFormat { Amc, XmlRazer }
 internal sealed class OutputFormatChoice : ComboBox {
  private static MacroFormat last=LoadChoice();
  private static string ChoicePath {
   get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RecoilLabs","ExportFormat.txt");}
  }
  private static MacroFormat LoadChoice() {
   if(Environment.GetEnvironmentVariable("CS2_PROBE_TEST_MODE")=="1")return MacroFormat.Amc;
   try{return File.ReadAllText(ChoicePath).Trim()=="XML"?MacroFormat.XmlRazer:MacroFormat.Amc;}
   catch(IOException){return MacroFormat.Amc;}catch(UnauthorizedAccessException){return MacroFormat.Amc;}
  }
  internal MacroFormat SelectedFormat {get{return SelectedIndex==1?MacroFormat.XmlRazer:MacroFormat.Amc;}}
  internal OutputFormatChoice(Form form,int x,int y,int width) {
   Name="OutputFormat";DropDownStyle=ComboBoxStyle.DropDownList;FlatStyle=FlatStyle.Flat;
   BackColor=Color.FromArgb(28,28,31);ForeColor=Color.Gainsboro;
   Location=new Point(x,y);Size=new Size(width,25);Items.AddRange(new object[] {"AMC","XML Razer"});
   SelectedIndex=(int)last;
   SelectedIndexChanged+=delegate {
    last=SelectedFormat;
    if(Environment.GetEnvironmentVariable("CS2_PROBE_TEST_MODE")=="1")return;
    try{Directory.CreateDirectory(Path.GetDirectoryName(ChoicePath));File.WriteAllText(ChoicePath,SelectedFormat==MacroFormat.XmlRazer?"XML":"AMC");}
    catch(IOException){}catch(UnauthorizedAccessException){}
   };
   form.Controls.Add(this);
  }
 }
 internal sealed class MacroExportResult {
  internal AmcResult Amc;
  internal RazerXmlResult Xml;
  internal string OutputPath {get{return Xml!=null?Xml.XmlPath:Amc.AmcPath;}}
  internal int Moves {get{return Xml!=null?Xml.MoveCommands:Amc.MoveCommands;}}
 }
 internal static class MacroExport {
  internal static string OutputPath(MacroFormat format,string selection) {
   return format==MacroFormat.XmlRazer?OutputFolders.Xml(selection):OutputFolders.Amc(selection);
  }
  internal static MacroExportResult Save(MacroFormat format,string selection,Func<string,AmcResult> generate,string existingMaster=null) {
   string output=OutputPath(format,selection);
   if(format==MacroFormat.Amc)return new MacroExportResult {Amc=generate(output)};
   if(existingMaster!=null)return new MacroExportResult {Xml=RazerXmlExporter.Export(existingMaster,output)};
   // XML-only output still derives from the exact existing AMC generator, without a second reconstruction.
   string temp=Path.Combine(Path.GetTempPath(),"RecoilLabs_XML_"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(temp);
   try {
    AmcResult master=generate(Path.Combine(temp,Path.GetFileNameWithoutExtension(output)+".amc"));
    return new MacroExportResult {Xml=RazerXmlExporter.Export(master.AmcPath,output,true)};
   } finally {
    try{Directory.Delete(temp,true);}catch(IOException){}catch(UnauthorizedAccessException){}
   }
  }
 }
}
