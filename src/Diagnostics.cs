using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace RecoilProbe {
 internal sealed class MemoryFieldException : InvalidOperationException {
  internal readonly string Field;
  internal readonly long Address;
  internal readonly long? Value;
  internal MemoryFieldException(string field,long address,long? value,Exception inner)
   :base(field+": "+(value.HasValue?(value.Value==0?"puntatore non disponibile.":"puntatore non valido."):
    "lettura non riuscita."),inner) {
   Field=field;Address=address;Value=value;
  }
 }
 internal static class Diagnostics {
  private static readonly object gate=new object();
  private static Exception previous;
  private static string previousKey;
  private static DateTime previousTime;
  internal static string ReportDirectory=AppDomain.CurrentDomain.BaseDirectory;
  internal static string LastPath;
  internal static void Record(Exception error,string context,string pointerTrace) {
   if(error==null)return;
   lock(gate) {
    if(Object.ReferenceEquals(previous,error))return;
    string key=error.GetType().FullName+"|"+error.Message+"|"+context;
    if(key==previousKey && (DateTime.UtcNow-previousTime).TotalSeconds<10)return;
    previous=error;previousKey=key;previousTime=DateTime.UtcNow;
    StringBuilder report=new StringBuilder();
    report.AppendLine("CS2 RECOIL INTERNO V0.2.5 - DIAGNOSTICA AVVIO");
    report.AppendLine("UTC: "+DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture));
    report.AppendLine("Il report viene salvato sul PC; non viene inviato automaticamente.");
    report.AppendLine("Layout: "+Layout.TargetBuild+"; fonte "+Layout.SourceCommit);
    if(!String.IsNullOrEmpty(context))report.AppendLine(context);
    report.AppendLine();report.AppendLine("ERRORE: "+error.Message);
    MemoryFieldException memory=error as MemoryFieldException;
    if(memory!=null) {
     report.AppendLine("Campo: "+memory.Field);
     report.AppendLine("Indirizzo letto: 0x"+memory.Address.ToString("X",CultureInfo.InvariantCulture));
     report.AppendLine("Valore: "+(memory.Value.HasValue?
      "0x"+memory.Value.Value.ToString("X",CultureInfo.InvariantCulture):"lettura incompleta"));
    }
    if(!String.IsNullOrEmpty(pointerTrace)){report.AppendLine();report.AppendLine("PUNTATORI LETTI");report.AppendLine(pointerTrace);}
    report.AppendLine();report.AppendLine("DETTAGLIO");report.AppendLine(error.ToString());
    if(!Save(ReportDirectory,report.ToString())) {
     Save(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CS2RecoilProbe"),
      report.ToString());
    }
   }
  }
  internal static void Record(Exception error) {Record(error,null,null);}
  private static bool Save(string folder,string text) {
   try {
    Directory.CreateDirectory(folder);
    string path=Path.Combine(folder,"Diagnostica_CS2.txt");
    File.WriteAllText(path,text,new UTF8Encoding(true));LastPath=path;return true;
   }catch(Exception){return false;}
  }
  internal static void Open() {
   string path;lock(gate){path=LastPath;}
   if(path==null||!File.Exists(path))
    throw new InvalidOperationException("Nessun report ancora disponibile. Premi F8 per riprodurre il problema.");
   Process.Start(path);
  }
 }
}
