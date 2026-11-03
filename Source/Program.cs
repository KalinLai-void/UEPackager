using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Win32;

public class Settings
{
    public string Project { get; set; }
    public string EngineRoot { get; set; }
    public string Destination { get; set; }
    public string AndroidFlavor { get; set; }
    public string[] Platforms { get; set; }
    public string[] PlatformOrder { get; set; }
    public Settings() { Project=""; EngineRoot=""; Destination=""; AndroidFlavor="ASTC"; Platforms=new[]{"Win64","Android"}; PlatformOrder=new string[0]; }
}
public class Platform
{
    public string Id, Folder, Label, State="";
    public bool Enabled=true, Checked, Detected;
    public Platform(string id, string label, bool enabled=true) { Id=id; Folder=id=="Win64"?"Windows":id; Label=label; Enabled=enabled; }
    public override string ToString() { return (State.Length>0?"["+State+"] ":"")+Label+(!Enabled?" [unavailable on Windows host]":""); }
}
public static class Catalog
{
    public static List<Platform> Load(string engine)
    {
        var result=new List<Platform>{new Platform("Win64","Windows (Win64)"),new Platform("Android","Android / Quest"),new Platform("Linux","Linux x64 (cross-compile SDK required)"),new Platform("LinuxArm64","Linux ARM64 (engine / SDK required)"),new Platform("Mac","macOS (Mac build host required)",false),new Platform("IOS","iOS / iPadOS (signing / remote Mac)"),new Platform("TVOS","tvOS (signing / remote Mac)"),new Platform("VisionOS","visionOS (Apple SDK / build host required)")};
        foreach(string relative in new[]{"Engine/Config","Engine/Platforms","Engine/Restricted"})
        {
            if(string.IsNullOrWhiteSpace(engine)) continue;
            string root=Path.Combine(engine,relative); if(!Directory.Exists(root)) continue;
            foreach(string file in Directory.GetFiles(root,"DataDrivenPlatformInfo.ini",SearchOption.AllDirectories))
            {
                var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); bool inside=false;
                foreach(string raw in File.ReadLines(file)) { string line=raw.Trim(); if(line.StartsWith("[")){inside=line.Equals("[DataDrivenPlatformInfo]",StringComparison.OrdinalIgnoreCase);continue;} int equal=line.IndexOf('='); if(inside && equal>0 && !line.StartsWith(";") && !line.StartsWith("#")) values[line.Substring(0,equal).Trim()]=line.Substring(equal+1).Trim(); }
                if(!values.ContainsKey("PlatformGroupName")&&!values.ContainsKey("UBTPlatformName"))continue;
                var dir=new DirectoryInfo(Path.GetDirectoryName(file)); string id=values.ContainsKey("UBTPlatformName")?values["UBTPlatformName"]:(dir.Name=="Config"?dir.Parent.Name:dir.Name);
                if(!Regex.IsMatch(id,@"^[A-Za-z][A-Za-z0-9_]*$"))continue;
                var item=result.FirstOrDefault(p=>p.Id.Equals(id,StringComparison.OrdinalIgnoreCase)); if(item==null){item=new Platform(id,id+" (engine extension / SDK required)");result.Add(item);}
                item.Detected=true; if(values.ContainsKey("Windows:bIsEnabled"))item.Enabled=values["Windows:bIsEnabled"].Equals("true",StringComparison.OrdinalIgnoreCase);
            }
        }
        return result;
    }
}
public sealed class ProcessJob : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct Basic { public long PerProcessTime,PerJobTime; public uint Flags; public UIntPtr Minimum,Maximum; public uint Active; public UIntPtr Affinity; public uint Priority,Scheduling; }
    [StructLayout(LayoutKind.Sequential)] struct IO { public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic Basic; public IO IO; public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attributes,string name);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int info,ref Extended value,int length);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateJobObject(IntPtr job,uint exitCode);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    IntPtr handle;
    public ProcessJob(){handle=CreateJobObject(IntPtr.Zero,null);if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();var value=new Extended();value.Basic.Flags=0x2000;if(!SetInformationJobObject(handle,9,ref value,Marshal.SizeOf(value))){int error=Marshal.GetLastWin32Error();Dispose();throw new System.ComponentModel.Win32Exception(error);}}
    public void Attach(Process child){if(!AssignProcessToJobObject(handle,child.Handle)&&!child.HasExited)throw new System.ComponentModel.Win32Exception();}
    public void Stop(){if(handle!=IntPtr.Zero&&!TerminateJobObject(handle,1))throw new System.ComponentModel.Win32Exception();}
    public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
}
public sealed class BuildRunner
{
    private readonly object gate=new object(); private Process process; private ProcessJob job; private bool stopped;
    public bool Stopped { get { lock(gate)return stopped; } }
    public void Stop()
    {
        lock(gate) {
            stopped=true;
            if(process==null || process.HasExited)return;
            if(job!=null)job.Stop();
        }
    }
    private void CheckStop() { if(Stopped)throw new OperationCanceledException(); }
    public static string Quote(string value)
    {
        if(value.IndexOfAny(new[]{'"','\r','\n','%'})>=0)throw new Exception("Paths may not contain quotes, newlines or % when calling Unreal's batch launcher.");
        return "\""+value+"\"";
    }
    public static string Arguments(Settings s, Platform platform)
    {
        return "BuildCookRun "+Quote("-project="+s.Project)+" -noP4 -platform="+platform.Id+" -clientconfig=Development \"-ubtargs=-DisableUnity -NoUBA\" -build -cook -stage -pak -package -archive "+Quote("-archivedirectory="+Path.Combine(s.Destination,platform.Folder))+" -utf8output -unattended"+(platform.Id=="Android"?" -cookflavor="+s.AndroidFlavor:"");
    }
    public static Dictionary<string,string> Toolchain(string root, bool android)
    {
        var env=new Dictionary<string,string>(); string local=Path.Combine(root,"Saved/AndroidToolchain");
        if(!android || !Directory.Exists(local))return env;
        string sdk=Path.Combine(local,"sdk"), ndk=Path.Combine(sdk,"ndk/27.2.12479018"), java=Path.Combine(local,"jdk21/jdk-21.0.12.1+1");
        foreach(string file in new[]{Path.Combine(java,"bin/java.exe"),Path.Combine(sdk,"platform-tools/adb.exe"),Path.Combine(ndk,"source.properties")})if(!File.Exists(file))throw new Exception("Missing project-local Android toolchain: "+file);
        env["ANDROID_HOME"]=sdk;env["NDKROOT"]=ndk;env["JAVA_HOME"]=java;
        string temp=Path.Combine(root,"Saved/JavaTemp");Directory.CreateDirectory(temp);
        string options=Environment.GetEnvironmentVariable("JAVA_TOOL_OPTIONS")??"";
        env["JAVA_TOOL_OPTIONS"]=options.Contains("jdk.net.unixdomain.tmpdir")?options:options+" -Djdk.net.unixdomain.tmpdir=\""+temp+"\"";
        return env;
    }
    public void Run(Settings s,List<Platform> platforms,bool validate,Action<string> log,Action<Platform,string> state)
    {
        if(!File.Exists(s.Project)||!s.Project.EndsWith(".uproject",StringComparison.OrdinalIgnoreCase))throw new Exception("Select a valid .uproject.");
        new JavaScriptSerializer().DeserializeObject(File.ReadAllText(s.Project));
        string uat=Path.Combine(s.EngineRoot,"Engine/Build/BatchFiles/RunUAT.bat");if(!File.Exists(uat))throw new Exception("RunUAT.bat not found in selected engine.");
        if(platforms.Count==0)throw new Exception("Select at least one platform.");
        if(platforms.Any(p=>!p.Enabled))throw new Exception("A selected platform is unavailable on this Windows host.");
        if(!new[]{"ASTC","ETC2","DXT","Multi"}.Contains(s.AndroidFlavor))throw new Exception("Invalid Android texture format.");
        var editors=Process.GetProcessesByName("UnrealEditor"); bool editorOpen=editors.Length>0;foreach(var editor in editors)editor.Dispose();if(editorOpen)throw new Exception("Save and close Unreal Editor before packaging.");
        string root=Path.GetDirectoryName(s.Project), projectBuilds=Path.Combine(root,"Builds");
        Directory.CreateDirectory(projectBuilds);Directory.CreateDirectory(s.Destination);
        using(var projectLock=new FileStream(Path.Combine(projectBuilds,".PackageBoth.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        using(var outputLock=new FileStream(Path.Combine(s.Destination,".PackagingDestination.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        {
            var env=Toolchain(root,platforms.Any(p=>p.Id=="Android"));
            log("Project: "+s.Project);log("Destination: "+s.Destination);log(env.Count>0?"Using project-local Android toolchain.":"Using installed SDK environment.");
            CheckStop();if(validate){foreach(var p in platforms)state(p,"Setup checked");log("Preflight passed. SDK/build compatibility is validated by UAT during packaging.");return;}
            foreach(var platform in platforms)
            {
                CheckStop();state(platform,"Running");log("===== START "+platform.Id+" =====");
                try {
                    var info=new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")??"cmd.exe", "/d /s /v:off /c \""+Quote(uat)+" "+Arguments(s,platform)+"\"") {WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                    foreach(var pair in env)info.EnvironmentVariables[pair.Key]=pair.Value;
                    using(var child=new Process{StartInfo=info})
                    using(var childJob=new ProcessJob())
                    {
                        child.OutputDataReceived+=(o,e)=>{if(e.Data!=null)log(e.Data);};child.ErrorDataReceived+=(o,e)=>{if(e.Data!=null)log(e.Data);};
                        lock(gate){CheckStop();child.Start();try{childJob.Attach(child);}catch{if(!child.HasExited)child.Kill();throw;}process=child;job=childJob;}
                        try {child.BeginOutputReadLine();child.BeginErrorReadLine();child.WaitForExit();CheckStop();if(child.ExitCode!=0)throw new Exception(platform.Id+" failed (exit "+child.ExitCode+").");}
                        finally {lock(gate){process=null;job=null;}}
                    }
                    string archive=Path.Combine(s.Destination,platform.Folder);
                    if(!Directory.Exists(archive)||!Directory.EnumerateFiles(archive,"*",SearchOption.AllDirectories).Any(f=>new FileInfo(f).Length>0))throw new Exception("UAT returned success but no non-empty archive found: "+archive);
                    state(platform,"Completed");log("===== SUCCESS "+platform.Id+" =====");
                } catch {state(platform,Stopped?"Stopped":"Failed");throw;}
            }
        }
    }
}
public class MainForm : Form
{
    readonly TextBox project=new TextBox(),engine=new TextBox(),destination=new TextBox(),logBox=new TextBox();
    readonly PlatformOrderList list=new PlatformOrderList(); readonly ComboBox flavor=new ComboBox();
    readonly Label status=new Label(); readonly Button stop=new Button();
    readonly List<Control> inputs=new List<Control>(); List<Platform> platforms=new List<Platform>();
    readonly string settingsPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Settings.json");
    BuildRunner runner; bool rebuilding, busy, closing; string loadedEngine="";
    public MainForm()
    {
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath); Text="UE Packaging Automation";Size=new Size(1040,870);MinimumSize=new Size(1040,800);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);
        LabelAt("Project (.uproject)",18,18,700);Box(project,18,46,750);ButtonAt("Browse...",780,44,110,()=>{using(var d=new OpenFileDialog{Filter="Unreal projects|*.uproject"})if(d.ShowDialog()==DialogResult.OK){project.Text=d.FileName;DetectEngine();Reload();}});
        LabelAt("Unreal Engine folder (contains Engine)",18,85,700);Box(engine,18,113,750);ButtonAt("Browse...",780,111,110,()=>BrowseFolder(engine));
        LabelAt("Platforms - checked items build from top to bottom (Development)",18,153,900);
        list.SetBounds(18,182,720,180);Controls.Add(list);inputs.Add(list);
        LabelAt("Android texture format",755,185,240);flavor.SetBounds(755,215,220,28);flavor.DropDownStyle=ComboBoxStyle.DropDownList;flavor.Items.AddRange(new object[]{"ASTC","ETC2","DXT","Multi"});flavor.SelectedIndex=0;Controls.Add(flavor);inputs.Add(flavor);
        ButtonAt("Refresh platforms",755,256,220,Reload);ButtonAt("Move up",755,298,105,()=>Move(list.SelectedIndex,list.SelectedIndex-1));ButtonAt("Move down",870,298,105,()=>Move(list.SelectedIndex,list.SelectedIndex+1));LabelAt("Drag a row or use its arrows.",755,337,240);
        LabelAt("Destination folder (default: project / Builds)",18,375,900);Box(destination,18,403,660);ButtonAt("Browse...",690,401,110,()=>BrowseFolder(destination));ButtonAt("Use project Builds",812,401,188,DefaultDestination);
        LabelAt("Outputs are separated by platform. Missing destination folders are created automatically.",18,445,990);LabelAt("Save and close Unreal Editor first. Existing build folders will be updated.",18,473,990);
        ButtonAt("Start packaging",18,510,160,()=>Start(false));ButtonAt("Check setup",190,510,130,()=>Start(true));
        stop.Text="Stop";stop.SetBounds(332,510,100,32);stop.Enabled=false;stop.Click+=async(o,e)=>await StopBuild();Controls.Add(stop);
        var open=ButtonAt("Open destination",444,510,150,()=>{if(Directory.Exists(destination.Text))Process.Start("explorer.exe",BuildRunner.Quote(destination.Text));});inputs.Remove(open);
        status.SetBounds(18,554,982,28);status.Text="Ready";Controls.Add(status);
        logBox.SetBounds(18,588,982,230);logBox.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;logBox.Multiline=true;logBox.ReadOnly=true;logBox.ScrollBars=ScrollBars.Both;logBox.WordWrap=false;logBox.Font=new Font("Consolas",9);Controls.Add(logBox);
        project.TextChanged+=(o,e)=>{DefaultDestination();ClearStates();};engine.Leave+=(o,e)=>{if(engine.Text!=loadedEngine)Safe(Reload);};
        list.MoveRequested+=(o,e)=>Move(e.Source,e.Target);list.ItemCheck+=(o,e)=>{if(rebuilding)return;if(!platforms[e.Index].Enabled)e.NewValue=CheckState.Unchecked;platforms[e.Index].Checked=e.NewValue==CheckState.Checked;};
        FormClosing+=async(o,e)=>{if(!busy)return;e.Cancel=true;if(MessageBox.Show("Stop packaging and close?","Packaging is running",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes){closing=true;await StopBuild();}};
        LoadSettings();
    }
    void Safe(Action action){try{action();}catch(Exception e){MessageBox.Show(this,e.Message,"UE Packaging Automation",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
    void LabelAt(string text,int x,int y,int w){Controls.Add(new Label{Text=text,Bounds=new Rectangle(x,y,w,25)});}
    void Box(TextBox box,int x,int y,int w){box.SetBounds(x,y,w,28);Controls.Add(box);inputs.Add(box);}
    Button ButtonAt(string text,int x,int y,int w,Action action){var b=new Button{Text=text,Bounds=new Rectangle(x,y,w,32)};b.Click+=(o,e)=>Safe(action);Controls.Add(b);inputs.Add(b);return b;}
    void BrowseFolder(TextBox box){using(var d=new FolderBrowserDialog{SelectedPath=box.Text,ShowNewFolderButton=true})if(d.ShowDialog()==DialogResult.OK){box.Text=d.SelectedPath;if(box==engine)Reload();}}
    void DefaultDestination(){try{destination.Text=string.IsNullOrWhiteSpace(project.Text)?"":Path.Combine(Path.GetDirectoryName(Path.GetFullPath(project.Text)),"Builds");}catch{destination.Text="";}}
    void ClearStates(){foreach(var p in platforms)p.State="";PaintList(list.SelectedIndex);}
    void DetectEngine()
    {
        try {var json=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(project.Text));string association=Convert.ToString(json["EngineAssociation"]);var candidates=new List<string>();
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Epic Games\Unreal Engine\Builds"))if(key!=null)candidates.Add(key.GetValue(association) as string);
            using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EpicGames\Unreal Engine\"+association))if(key!=null)candidates.Add(key.GetValue("InstalledDirectory") as string);
            candidates.Add(@"D:\GameEngines\Unreal\UE_"+association);candidates.Add(@"C:\Program Files\Epic Games\UE_"+association);
            foreach(string candidate in candidates)if(candidate!=null&&File.Exists(Path.Combine(candidate,"Engine/Build/BatchFiles/RunUAT.bat"))){engine.Text=candidate;break;}
        }catch{}
    }
    void Reload(){if(busy)return;var previous=platforms;var available=Catalog.Load(engine.Text);platforms=previous.Select(p=>available.FirstOrDefault(a=>a.Id==p.Id)).Where(p=>p!=null).Concat(available.Where(a=>!previous.Any(p=>p.Id==a.Id))).ToList();foreach(var p in platforms){var old=previous.FirstOrDefault(a=>a.Id==p.Id);p.Checked=p.Enabled&&(old!=null?old.Checked:p.Id=="Win64"||p.Id=="Android");}loadedEngine=engine.Text;PaintList(-1);}
    void PaintList(int selection){rebuilding=true;list.BeginUpdate();list.Items.Clear();foreach(var p in platforms)list.Items.Add(p.ToString(),p.Checked);if(selection>=0&&selection<list.Items.Count)list.SelectedIndex=selection;list.EndUpdate();rebuilding=false;}
    public new void Move(int source,int target){if(busy||source<0||target<0||source>=platforms.Count||target>=platforms.Count)return;var p=platforms[source];platforms.RemoveAt(source);platforms.Insert(target,p);PaintList(target);Safe(SaveSettings);}
    Settings Snapshot(){return new Settings{Project=project.Text,EngineRoot=engine.Text,Destination=destination.Text,AndroidFlavor=Convert.ToString(flavor.SelectedItem),PlatformOrder=platforms.Select(p=>p.Id).ToArray(),Platforms=platforms.Where(p=>p.Checked).Select(p=>p.Id).ToArray()};}
    void SaveSettings(){File.WriteAllText(settingsPath,new JavaScriptSerializer().Serialize(Snapshot()),Encoding.UTF8);}
    void LoadSettings(){Safe(()=>{var s=File.Exists(settingsPath)?new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(settingsPath)):new Settings();project.Text=s.Project;engine.Text=s.EngineRoot;if(!string.IsNullOrWhiteSpace(s.Destination))destination.Text=s.Destination;flavor.SelectedItem=new[]{"ASTC","ETC2","DXT","Multi"}.Contains(s.AndroidFlavor)?s.AndroidFlavor:"ASTC";Reload();var order=s.PlatformOrder??new string[0];platforms=platforms.OrderBy(p=>{int i=Array.IndexOf(order,p.Id);return i<0?int.MaxValue:i;}).ToList();foreach(var p in platforms)p.Checked=p.Enabled&&(s.Platforms??new string[0]).Contains(p.Id);PaintList(-1);});}
    void UI(Action action){if(!IsDisposed&&IsHandleCreated)BeginInvoke(action);}
    void SetState(Platform p,string state){p.State=state;int index=platforms.IndexOf(p);if(index>=0){list.Items[index]=p.ToString();list.Invalidate();}}
    async void Start(bool validate)
    {
        if(busy)return;StreamWriter writer=null;object logGate=new object();
        try {
            if(string.IsNullOrWhiteSpace(destination.Text))DefaultDestination();
            if(!Regex.IsMatch(destination.Text,@"^(?:[A-Za-z]:[\\/]|\\\\[^\\]+\\[^\\]+)"))throw new Exception("Select an absolute destination folder.");
            var s=Snapshot();s.Project=Path.GetFullPath(s.Project);s.EngineRoot=Path.GetFullPath(s.EngineRoot);s.Destination=Path.GetFullPath(s.Destination);
            if(!File.Exists(s.Project))throw new Exception("Select a .uproject file.");var selected=platforms.Where(p=>p.Checked).ToList();if(selected.Count==0)throw new Exception("Select at least one platform.");
            SaveSettings();string logs=Path.Combine(s.Destination,"Logs");Directory.CreateDirectory(logs);string prefix=Path.Combine(logs,"Packaging-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
            File.WriteAllText(prefix+".json",new JavaScriptSerializer().Serialize(s),Encoding.UTF8);writer=new StreamWriter(prefix+".log",false,Encoding.UTF8){AutoFlush=true};
            foreach(var p in platforms)p.State=p.Checked?(validate?"Checking":"Waiting"):"";PaintList(-1);logBox.Clear();status.Text="Running - "+prefix+".log";
            busy=true;foreach(var control in inputs)control.Enabled=false;stop.Enabled=true;runner=new BuildRunner();
            Action<string> log=line=>{lock(logGate)writer.WriteLine(line);UI(()=>{if(logBox.TextLength>180000)logBox.Text=logBox.Text.Substring(logBox.TextLength-90000);logBox.AppendText(line+Environment.NewLine);});};
            try {await Task.Run(()=>runner.Run(s,selected,validate,log,(p,state)=>UI(()=>SetState(p,state))));status.Text="Completed successfully.";}
            catch(Exception e){log(e.Message);status.Text=runner.Stopped?"Stopped. Output may be incomplete.":"Failed: "+e.Message;}
            // Queued state callbacks are dispatched before the UI task continuation.
            foreach(var p in selected)if(p.State=="Waiting"||p.State=="Checking"||p.State=="Running")p.State=runner.Stopped?"Stopped":"Not built";
            PaintList(-1);
        }catch(Exception e){MessageBox.Show(this,e.Message,"Cannot start",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        finally {if(writer!=null)writer.Dispose();busy=false;runner=null;foreach(var control in inputs)control.Enabled=true;stop.Enabled=false;if(closing)Close();}
    }
    async Task StopBuild(){var active=runner;if(active==null)return;stop.Enabled=false;try{await Task.Run(()=>active.Stop());}catch(Exception e){closing=false;MessageBox.Show(this,e.Message,"Cannot stop");stop.Enabled=true;}}
}
public static class Program
{
    [STAThread] public static void Main(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm());}
}


