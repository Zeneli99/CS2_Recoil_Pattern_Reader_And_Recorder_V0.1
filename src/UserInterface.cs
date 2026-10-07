using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace RecoilProbe {
 internal static class Ui {
  internal static void Style(Form form,string title) {
   form.Text=title;form.ClientSize=new Size(540,360);form.FormBorderStyle=FormBorderStyle.FixedSingle;
   form.MaximizeBox=false;form.StartPosition=FormStartPosition.CenterScreen;
   form.BackColor=Color.FromArgb(18,18,20);form.ForeColor=Color.Gainsboro;form.Font=new Font("Segoe UI",9F);
   form.AutoScaleMode=AutoScaleMode.None;
  }
  internal static Label Label(Form form,string text,int x,int y,int w,int h) {
   Label label=new Label {Text=text,Location=new Point(x,y),Size=new Size(w,h)};
   form.Controls.Add(label);return label;
  }
  internal static TextBox Text(Form form,string text,int x,int y,int w,bool readOnly) {
   TextBox box=new TextBox {Text=text,Location=new Point(x,y),Size=new Size(w,25),ReadOnly=readOnly,
    BackColor=Color.FromArgb(28,28,31),ForeColor=Color.Gainsboro,BorderStyle=BorderStyle.FixedSingle};
   form.Controls.Add(box);return box;
  }
  internal static Button Button(Form form,string text,int x,int y,int w,int h) {
   Button button=new Button {Text=text,Location=new Point(x,y),Size=new Size(w,h),
    FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(58,24,28),ForeColor=Color.Gainsboro};
   button.FlatAppearance.BorderColor=Color.FromArgb(157,45,55);form.Controls.Add(button);return button;
  }
  internal static void Title(Form form,string text) {
   Label title=Label(form,text,20,16,460,32);title.ForeColor=Color.FromArgb(235,63,75);
   title.Font=new Font("Segoe UI",15F,FontStyle.Bold);
  }
  internal static void Post(Control control,Action action) {
   try {if(!control.IsDisposed && control.IsHandleCreated)control.BeginInvoke(action);}
   catch(InvalidOperationException) { }
  }
  internal static string Sens(float value){return value.ToString("0.000###",CultureInfo.InvariantCulture);}
 }
 internal sealed class RecorderForm : Form {
  private TextBox weapon,sensitivity,folder,log;
  private Label detected;
  private Button record,open,convert,choose,report,check;
  private AmcInput executionAmc;
  private CheckBox autoAmc;
  private Recorder recorder;
  private Thread worker;
  private bool running,closing,detecting;
  private System.Windows.Forms.Timer monitor;
  private const int HotkeyId=8118;
  internal RecorderForm() {
   Ui.Style(this,"CS2 RECORDER DIAGNOSTICO · V0.3.2");Ui.Title(this,"RECORDER DIAGNOSTICO");
   Ui.Label(this,"V0.3.2",470,25,60,24);
   Button page=Ui.Button(this,"RECOIL INTERNO",20,54,145,29);page.Enabled=false;
   convert=Ui.Button(this,"CONVERTER",180,54,145,29);
   convert.Click+=delegate{using(ConverterForm form=new ConverterForm())form.ShowDialog(this);};
   report=Ui.Button(this,"REPORT",365,54,155,29);
   report.Click+=delegate{
    try{Diagnostics.Open();}catch(Exception ex){Status(ex.Message);}
   };
   Ui.Label(this,"Arma · AUTO",20,96,230,21);Ui.Label(this,"Sensibilita' · AUTO",295,96,220,21);
   weapon=Ui.Text(this,"In attesa di CS2",20,120,250,true);
   sensitivity=Ui.Text(this,"AUTO",295,120,225,true);
   detected=Ui.Label(this,"Apri una mappa di pratica locale con -insecure.",20,150,500,29);
   folder=Ui.Text(this,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Registrazioni"),20,183,395,false);
   choose=Ui.Button(this,"Cartella",425,181,95,28);
   choose.Click+=delegate{
    using(FolderBrowserDialog dialog=new FolderBrowserDialog()){
     dialog.SelectedPath=folder.Text;
     if(dialog.ShowDialog(this)==DialogResult.OK)folder.Text=dialog.SelectedPath;
    }
   };
   autoAmc=new CheckBox {Text="Genera AMC dopo lo spray",Checked=true,
    Location=new Point(20,214),Size=new Size(250,23),ForeColor=Color.Gainsboro};
   Controls.Add(autoAmc);
   check=Ui.Button(this,"TEST AMC...",280,213,240,25);
   check.Click+=delegate {
    if(executionAmc!=null) {SetExecutionTest(null);return;}
    using(OpenFileDialog dialog=new OpenFileDialog()) {
     dialog.Filter="Macro da verificare (*.amc)|*.amc";
     if(dialog.ShowDialog(this)==DialogResult.OK) {
      try{SetExecutionTest(AmcInput.Load(dialog.FileName));}
      catch(Exception ex){Status(ex.Message);}
     }
    }
   };
   record=Ui.Button(this,"ARMA / FERMA · F8",20,243,245,35);record.Click+=delegate{Toggle();};
   open=Ui.Button(this,"Apri registrazioni",280,243,240,35);
   open.Click+=delegate{
    try{Directory.CreateDirectory(folder.Text);Process.Start(folder.Text);}
    catch(Exception ex){Status(ex.Message);}
   };
   log=Ui.Text(this,"F8, torna al gioco, attendi un secondo e spara senza muovere il mouse.",20,292,500,true);
   log.Multiline=true;log.Size=new Size(500,52);
   monitor=new System.Windows.Forms.Timer {Interval=1200};
   monitor.Tick+=delegate{Detect();};
   FormClosing+=delegate(object sender,FormClosingEventArgs args){
    if(running){closing=true;recorder.StopRequested=true;args.Cancel=true;Status("Termino e salvo i dati...");}
   };
  }
  protected override void OnShown(EventArgs e) {
   base.OnShown(e);
   if(Environment.GetEnvironmentVariable("CS2_PROBE_TEST_MODE")!="1"){monitor.Start();Detect();}
  }
  private void Detect() {
   if(running||detecting||closing||IsDisposed)return;
   detecting=true;
   ThreadPool.QueueUserWorkItem(delegate(object ignored){
    GameIdentity info=null;string error=null;
    try{using(Game game=new Game(false)){game.VerifySession();info=game.ReadIdentity();}}
    catch(Exception ex){error=ex.Message;Diagnostics.Record(ex);}
    Ui.Post(this,delegate{
     detecting=false;if(running||closing)return;
     if(info!=null) {
      SetIdentity(info);detected.Text="Arma e sensibilita' lette dal gioco · build "+RecoilProbe.Layout.TargetBuild+
       (info.Scoped?" · zoom attivo":"");
      detected.ForeColor=info.Scoped?Color.FromArgb(235,135,75):Color.FromArgb(170,205,180);
     } else {
      weapon.Text="In attesa";sensitivity.Text="AUTO";detected.Text=error;
      detected.ForeColor=Color.Gainsboro;
     }
    });
   });
  }
  private void SetIdentity(GameIdentity info) {weapon.Text=info.WeaponName;sensitivity.Text=Ui.Sens(info.Sensitivity);}
  protected override void OnHandleCreated(EventArgs e) {
   base.OnHandleCreated(e);
   if(!Native.RegisterHotKey(Handle,HotkeyId,0x4000,0x77))Status("F8 occupato. Usa il pulsante ARMA.");
  }
  protected override void OnHandleDestroyed(EventArgs e) {
   Native.UnregisterHotKey(Handle,HotkeyId);base.OnHandleDestroyed(e);
  }
  protected override void WndProc(ref Message message) {
   if(message.Msg==0x0312 && message.WParam.ToInt32()==HotkeyId && Enabled)Toggle();
   base.WndProc(ref message);
  }
  private void Toggle() {
   if(running){recorder.StopRequested=true;record.Enabled=false;return;}
   string path;try{path=Path.GetFullPath(folder.Text);}catch(Exception ex){Status(ex.Message);return;}
   recorder=new Recorder(path,autoAmc.Checked,Status,delegate(GameIdentity info){Ui.Post(this,delegate{SetIdentity(info);});},executionAmc);
   running=true;folder.Enabled=false;autoAmc.Enabled=false;convert.Enabled=false;choose.Enabled=false;check.Enabled=false;
   record.Text="FERMA · F8";
   worker=new Thread(delegate(){
    CaptureResult result=null;string error=null;
    try{result=recorder.Run();}catch(Exception ex){error=ex.Message;Diagnostics.Record(ex);}
    Ui.Post(this,delegate{
     running=false;record.Enabled=true;record.Text=executionAmc==null?"ARMA / FERMA · F8":"PROVA AMC · F8";folder.Enabled=true;
     autoAmc.Enabled=executionAmc==null;convert.Enabled=true;choose.Enabled=true;check.Enabled=true;
     if(error!=null)log.Text=error+"\r\nPremi REPORT per aprire Diagnostica_CS2.txt.";
     else if(result==null)log.Text="Registrazione annullata prima del primo colpo.";
     else {
      log.Text=result.Shots+" colpi · "+result.Samples+" campioni · gap max "+
       result.MaxGapMs.ToString("F2",CultureInfo.CurrentCulture)+" ms\r\n"+
       (result.ExecutionReportPath!=null?"Test salvato: "+Path.GetFileName(result.ExecutionReportPath):
        result.ExecutionError??(result.AmcPath!=null?"AMC salvato: "+Path.GetFileName(result.AmcPath):
        result.AmcError??"CSV + JSON salvati."));
     }
     if(closing)Close();
    });
   });
   worker.IsBackground=true;worker.Name="CS2 read-only recorder";worker.Start();
  }
  private void SetExecutionTest(AmcInput selected) {
   executionAmc=selected;autoAmc.Enabled=selected==null;
   check.Text=selected==null?"TEST AMC...":"ESCI DAL TEST";
   record.Text=selected==null?"ARMA / FERMA · F8":"PROVA AMC · F8";
   log.Text=selected==null?"F8, torna al gioco, attendi un secondo e spara senza muovere il mouse.":
    selected.WeaponName+" · sens "+selected.Sensitivity.ToString("0.000###",CultureInfo.InvariantCulture)+
    " · "+Path.GetFileName(selected.SourcePath)+"\r\nF8 nel gioco, poi esegui questa macro in Bloody con il mouse fermo.";
  }
  private void Status(string message){Ui.Post(this,delegate{log.Text=message;});}
  protected override void Dispose(bool disposing) {
   if(disposing && monitor!=null){monitor.Stop();monitor.Dispose();monitor=null;}
   base.Dispose(disposing);
  }
 }
 internal sealed class ConverterForm : Form {
  private TextBox source,target,summary,log;
  private Button load,convert;
  private CheckBox original;
  private RecordingData data;
  private WeaponSnapshot snapshot;
  private AmcInput amc;
  private bool busy;
  internal ConverterForm() {
   Ui.Style(this,"CS2 AMC CONVERTER · V0.3.2");Ui.Title(this,"AMC CONVERTER");
   Ui.Label(this,"Apri AMC / ZIP / CSV / JSON · oppure trascina un file.",20,55,500,24);
   source=Ui.Text(this,"Nessuna registrazione caricata",20,87,375,true);
   load=Ui.Button(this,"APRI FILE",410,85,110,29);load.Click+=delegate{Choose();};
   summary=Ui.Text(this,"Arma e sensibilita' verranno lette dai dati del file.",20,126,500,true);
   summary.Multiline=true;summary.Size=new Size(500,50);
   original=new CheckBox {Text="Usa la sensibilita' della registrazione",Checked=true,
    Location=new Point(20,187),Size=new Size(315,23),ForeColor=Color.Gainsboro};
   Controls.Add(original);target=Ui.Text(this,"1.250",365,187,155,false);target.Enabled=false;
   original.CheckedChanged+=delegate{target.Enabled=!original.Checked && !busy;};
   Ui.Label(this,"Smooth moderato · circa 10 ms · click hold · pausa 30 s",20,219,500,23);
   convert=Ui.Button(this,"CONVERTI IN AMC",20,249,500,37);convert.Enabled=false;
   convert.Click+=delegate{Export();};
   log=Ui.Text(this,"Conversione di prova: pitch/yaw 0.022 e recoil scale 2.0 assunti.",20,302,500,true);
   log.Multiline=true;log.Size=new Size(500,42);
   AllowDrop=true;
   DragEnter+=delegate(object sender,DragEventArgs e){
    if(!busy && e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;
   };
   DragDrop+=delegate(object sender,DragEventArgs e){
    string[] paths=e.Data.GetData(DataFormats.FileDrop) as string[];
    if(paths==null||paths.Length!=1){log.Text="Trascina una registrazione alla volta.";return;}
    LoadRecording(paths[0]);
   };
   FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){e.Cancel=true;log.Text="Attendi la fine dell'operazione.";}} ;
  }
  private void Choose() {
   using(OpenFileDialog dialog=new OpenFileDialog()){
    dialog.Filter="AMC e registrazioni CS2 (*.amc;*.zip;*.csv;*.json)|*.amc;*.zip;*.csv;*.json";
    if(dialog.ShowDialog(this)==DialogResult.OK)LoadRecording(dialog.FileName);
   }
  }
  private void Busy(bool value) {
   busy=value;load.Enabled=!value;convert.Enabled=!value && (data!=null||amc!=null||snapshot!=null);
   original.Enabled=!value;target.Enabled=!value && !original.Checked;
  }
  private void LoadRecording(string path) {
   if(busy)return;Busy(true);data=null;amc=null;snapshot=null;source.Text=Path.GetFileName(path);log.Text="Controllo i dati...";
   ThreadPool.QueueUserWorkItem(delegate(object ignored){
    RecordingData loaded=null;AmcInput loadedAmc=null;WeaponSnapshot loadedSnapshot=null;int shots=0;string error=null;
    try {
     if(String.Equals(Path.GetExtension(path),".amc",StringComparison.OrdinalIgnoreCase))
      loadedAmc=AmcInput.Load(path);
     else if(WeaponSnapshotIO.TryLoad(path,out loadedSnapshot))shots=loadedSnapshot.Native.MaxClip;
     else {loaded=RecordingIO.Load(path);shots=AmcConverter.Points(loaded).Count;}
    } catch(Exception ex){error=ex.Message;}
    Ui.Post(this,delegate{
     if(error!=null){summary.Text="File non convertibile";log.Text=error;data=null;amc=null;snapshot=null;}
     else if(loadedSnapshot!=null) {
      snapshot=loadedSnapshot;target.Text=snapshot.Sensitivity.ToString("0.000",CultureInfo.InvariantCulture);
      summary.Text=snapshot.Weapon+" · sens "+Ui.Sens(snapshot.Sensitivity)+" · "+shots+" colpi\r\nVData senza sparare · MODELLO SPERIMENTALE";
      log.Text="Parametri estratti + modello non verificato. Non e' una traiettoria letta direttamente.";
     }
     else if(loadedAmc!=null) {
      amc=loadedAmc;target.Text=amc.Sensitivity.ToString("0.000",CultureInfo.InvariantCulture);
      summary.Text=amc.WeaponName+" · sens "+amc.Sensitivity.ToString("0.000###",CultureInfo.InvariantCulture)+
       " · "+amc.MoveCommands+" MoveR · AMC\r\nSensibilita' letta dall'intestazione del file.";
      log.Text="Pronto. Mantengo i punti originali e suddivido i salti lunghi.";
     } else {
      data=loaded;target.Text=data.Sensitivity.ToString("0.000",CultureInfo.InvariantCulture);
      summary.Text=data.WeaponName+" · sens "+data.Sensitivity.ToString("0.000###",CultureInfo.InvariantCulture)+
       " · "+shots+" colpi\r\n"+(data.AutomaticIdentity?"Arma e sensibilita' registrate automaticamente.":
        "File V0.1: nome/sensibilita' inseriti manualmente all'origine.");
      log.Text="Pronto. Smooth moderato; verifica l'AMC nel gioco.";
     }
     Busy(false);
    });
   });
  }
  internal static bool TryTarget(string text,out double value) {
   value=0;
   return System.Text.RegularExpressions.Regex.IsMatch(text,@"^\d+\.\d{3}$") &&
    Double.TryParse(text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value) &&
    IdentityReader.ValidSensitivity(value);
  }
  private void Export() {
   if((data==null&&amc==null&&snapshot==null)||busy)return;
   double sens=snapshot!=null?snapshot.Sensitivity:amc!=null?amc.Sensitivity:data.Sensitivity;
   if(!original.Checked && !TryTarget(target.Text,out sens)){log.Text="Scrivi la sensibilita' come 1.250 (punto e tre decimali).";return;}
   string output;
   using(SaveFileDialog dialog=new SaveFileDialog()){
    dialog.Filter="Macro Bloody (*.amc)|*.amc";dialog.FileName=snapshot!=null?NoFireGenerator.SuggestedFileName(snapshot,sens):amc!=null?AmcConverter.SuggestedFileName(amc,sens):AmcConverter.SuggestedFileName(data,sens);
    dialog.OverwritePrompt=true;
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;output=dialog.FileName;
   }
   Busy(true);log.Text="Creo AMC e report...";
   ThreadPool.QueueUserWorkItem(delegate(object ignored){
    AmcResult result=null;string error=null;
    try{result=snapshot!=null?NoFireGenerator.Convert(snapshot,output,sens):amc!=null?AmcConverter.Smooth(amc,output,sens):AmcConverter.Convert(data,output,sens);}catch(Exception ex){error=ex.Message;}
    Ui.Post(this,delegate{
     log.Text=error??((result.SourceKind=="AMC"?"AMC":result.Shots+" colpi")+" · "+result.MoveCommands+" MoveR · "+
      result.ActiveDurationMs.ToString("F0",CultureInfo.InvariantCulture)+" ms\r\nAMC e report salvati.");
     Busy(false);
    });
   });
  }
 }
 internal static class Program {
  [STAThread] private static void Main() {
   if(IntPtr.Size!=8){MessageBox.Show("Serve la versione x64.");return;}
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   Application.Run(new MainForm());
  }
 }
}
