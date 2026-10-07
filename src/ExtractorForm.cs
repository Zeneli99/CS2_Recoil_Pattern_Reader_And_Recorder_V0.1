using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace RecoilProbe {
 internal sealed class MainForm : Form {
  private TextBox weapon,sensitivity,folder,log;
  private Label detected;
  private Button record,report,converter,recorder,choose;
  private OutputFormatChoice format;
  private bool busy,detecting;
  private System.Windows.Forms.Timer monitor;
  private const int HotkeyId=8119;
  internal MainForm() {
   Ui.Style(this,"CS2 SENZA SPARARE · V0.3.4 SPERIMENTALE");Ui.Title(this,"CS2 SENZA SPARARE");
   Ui.Label(this,"V0.3.4",470,25,60,24);
   converter=Ui.Button(this,"CONVERTER",20,54,145,29);
   converter.Click+=delegate {using(ConverterForm form=new ConverterForm())form.ShowDialog(this);};
   recorder=Ui.Button(this,"RECORDER / TEST",180,54,165,29);
   recorder.Click+=delegate {
    monitor.Stop();Native.UnregisterHotKey(Handle,HotkeyId);
    try{using(RecorderForm form=new RecorderForm())form.ShowDialog(this);}
    finally{Native.RegisterHotKey(Handle,HotkeyId,0x4000,0x77);monitor.Start();}
   };
   report=Ui.Button(this,"REPORT",365,54,155,29);
   report.Click+=delegate {try{Diagnostics.Open();}catch(Exception ex){log.Text=ex.Message;}};
   Ui.Label(this,"Arma · AUTO",20,96,230,21);Ui.Label(this,"Sensibilita' · AUTO",295,96,220,21);
   weapon=Ui.Text(this,"In attesa di CS2",20,120,250,true);
   sensitivity=Ui.Text(this,"AUTO",295,120,225,true);
   detected=Ui.Label(this,"Mappa offline · -insecure · arma automatica senza zoom.",20,150,500,29);
   folder=Ui.Text(this,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Estrazioni"),20,183,395,false);
   choose=Ui.Button(this,"Cartella",425,181,95,28);
   choose.Click+=delegate {using(FolderBrowserDialog dialog=new FolderBrowserDialog()) {
    dialog.SelectedPath=folder.Text;if(dialog.ShowDialog(this)==DialogResult.OK)folder.Text=dialog.SelectedPath;
   }};
   Ui.Label(this,"Formato:",20,216,85,23);format=new OutputFormatChoice(this,110,212,410);
   record=Ui.Button(this,"ESTRAI + AMC · F8",20,243,245,35);record.Click+=delegate {Extract();};
   Button open=Ui.Button(this,"Apri estrazioni",280,243,240,35);
   open.Click+=delegate {try{Directory.CreateDirectory(folder.Text);Process.Start(folder.Text);}catch(Exception ex){log.Text=ex.Message;}};
   log=Ui.Text(this,"Non sparare. Ricarica, attendi il reset recoil, poi F8.\r\nScegli AMC o XML Razer prima di estrarre.",20,292,500,true);
   log.Multiline=true;log.Size=new Size(500,52);
   format.SelectedIndexChanged+=delegate{UpdateFormat();};UpdateFormat();
   monitor=new System.Windows.Forms.Timer {Interval=1500};monitor.Tick+=delegate {Detect();};
   FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){e.Cancel=true;log.Text="Attendi il salvataggio dei file.";}};
  }
  protected override void OnShown(EventArgs e) {
   base.OnShown(e);if(Environment.GetEnvironmentVariable("CS2_PROBE_TEST_MODE")!="1"){monitor.Start();Detect();}
  }
  private void Detect() {
   if(busy||detecting||IsDisposed)return;detecting=true;
   ThreadPool.QueueUserWorkItem(delegate(object ignored) {
    GameIdentity info=null;string error=null;
    try{using(Game game=new Game(false))info=game.ReadIdentity();}catch(Exception ex){error=ex.Message;}
    Ui.Post(this,delegate {
     detecting=false;if(busy)return;
     weapon.Text=info==null?"In attesa":info.WeaponName;sensitivity.Text=info==null?"AUTO":Ui.Sens(info.Sensitivity);
     detected.Text=info==null?error:info.Scoped?"Togli lo zoom prima dell'estrazione.":
      "Arma e sensibilita' lette · caricatore "+info.Ammo+" · non sparare";
    });
   });
  }
  private void Extract() {
   if(busy||!Enabled)return;
   string outputFolder;try{outputFolder=Path.GetFullPath(folder.Text);}catch(Exception ex){log.Text=ex.Message;return;}
   MacroFormat selected=format.SelectedFormat;
   busy=true;record.Enabled=false;folder.Enabled=false;choose.Enabled=false;converter.Enabled=false;recorder.Enabled=false;format.Enabled=false;
   log.Text="Leggo VData e controllo che l'arma sia ferma...";
   ThreadPool.QueueUserWorkItem(delegate(object ignored) {
    MacroExportResult result=null;WeaponSnapshot input=null;string error=null,snapshotPath=null;
    try {
     using(Game game=new Game(false))input=WeaponDataReader.Extract(game);
     string selection=Path.Combine(outputFolder,NoFireGenerator.SuggestedFileName(input,input.Sensitivity));
     result=ExportSnapshot(input,selection,selected,out snapshotPath);
    } catch(Exception ex){error=ex.Message;Diagnostics.Record(ex);}
    Ui.Post(this,delegate {
     busy=false;record.Enabled=true;folder.Enabled=true;choose.Enabled=true;converter.Enabled=true;recorder.Enabled=true;format.Enabled=true;
     if(input!=null){weapon.Text=input.Weapon;sensitivity.Text=Ui.Sens(input.Sensitivity);}
     log.Text=error!=null?(selected==MacroFormat.XmlRazer?"XML non creato: ":"AMC non creato: ")+error+
      (snapshotPath!=null?"\r\nParametri conservati. Premi REPORT.":"\r\nPremi REPORT per la diagnostica."):
      (selected==MacroFormat.XmlRazer?"XML Razer salvato":"AMC salvato")+" · "+result.Moves+" MoveR\r\n"+result.OutputPath;
    });
   });
  }
  private void UpdateFormat(){record.Text=format.SelectedFormat==MacroFormat.XmlRazer?"ESTRAI XML · F8":"ESTRAI AMC · F8";}
  internal static MacroExportResult ExportSnapshot(WeaponSnapshot input,string selection,MacroFormat selected,out string parameters) {
   parameters=WeaponSnapshotIO.Save(input,MacroExport.OutputPath(selected,selection));
   return MacroExport.Save(selected,selection,delegate(string master){return NoFireGenerator.Convert(input,master,input.Sensitivity);});
  }
  protected override void OnHandleCreated(EventArgs e) {
   base.OnHandleCreated(e);if(!Native.RegisterHotKey(Handle,HotkeyId,0x4000,0x77))log.Text="F8 occupato. Usa il pulsante ESTRAI.";
  }
  protected override void OnHandleDestroyed(EventArgs e){Native.UnregisterHotKey(Handle,HotkeyId);base.OnHandleDestroyed(e);}
  protected override void WndProc(ref Message message) {
   if(message.Msg==0x0312&&message.WParam.ToInt32()==HotkeyId)Extract();base.WndProc(ref message);
  }
  protected override void Dispose(bool disposing) {
   if(disposing&&monitor!=null){monitor.Stop();monitor.Dispose();monitor=null;}base.Dispose(disposing);
  }
 }
}
