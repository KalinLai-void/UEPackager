using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PackagingAutomation;

public sealed class Settings
{
    public string Project { get; set; } = "";
    public string EngineRoot { get; set; } = "";
    public string Destination { get; set; } = "";
    public string AndroidFlavor { get; set; } = "ASTC";
    public string[] Platforms { get; set; } = [Host.Name == "Windows" ? "Win64" : Host.Name == "Mac" ? "Mac" : "Linux", "Android"];
    public string[] PlatformOrder { get; set; } = [];
}
public static class Host
{
    public static string Name => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "Mac" : "Linux";
    public static string Uat(string engine, string? host = null) => Path.Combine(engine, "Engine", "Build", "BatchFiles", (host ?? Name) == "Windows" ? "RunUAT.bat" : "RunUAT.sh");
    public static void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) throw new IOException("Destination folder does not exist yet.");
        var info = new ProcessStartInfo(Name == "Windows" ? "explorer.exe" : Name == "Mac" ? "/usr/bin/open" : "xdg-open") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(path); Process.Start(info)?.Dispose();
    }
    public static string? DetectEngine(string project)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(project));
        if (!json.RootElement.TryGetProperty("EngineAssociation", out var element)) return null;
        var version = element.GetString() ?? "";
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            using var custom = Registry.CurrentUser.OpenSubKey(@"Software\Epic Games\Unreal Engine\Builds");
            using var installed = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EpicGames\Unreal Engine\" + version);
            candidates.Add(custom?.GetValue(version) as string ?? ""); candidates.Add(installed?.GetValue("InstalledDirectory") as string ?? "");
            candidates.Add(@"D:\GameEngines\Unreal\UE_" + version); candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games", "UE_" + version));
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add("/Users/Shared/Epic Games/UE_" + version); candidates.Add("/Users/Shared/UnrealEngine/" + version);
            candidates.Add(Path.Combine(home, "UnrealEngine")); candidates.Add(Path.Combine(home, "Epic", "UE_" + version)); candidates.Add("/opt/unreal-engine");
            var registration = Name == "Mac" ? Path.Combine(home, "Library/Application Support/Epic/UnrealEngine/Install.ini") : Path.Combine(home, ".config/Epic/UnrealEngine/Install.ini");
            if (File.Exists(registration)) foreach (var line in File.ReadLines(registration)) { var pair = line.Split('=', 2); if (pair.Length == 2 && pair[0].Trim() == version) candidates.Insert(0, pair[1].Trim()); }
        }
        return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(Uat(c)));
    }
}
public sealed class SettingsStore
{
    public string FilePath { get; }
    public SettingsStore(string? folder = null) => FilePath = Path.Combine(folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UE-Packaging-Automation"), "Settings.json");
    public Settings Load()
    {
        var path = File.Exists(FilePath) ? FilePath : Path.Combine(AppContext.BaseDirectory, "Settings.json");
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions) ?? new();
        if (path != FilePath) Save(settings);
        return settings;
    }
    public void Save(Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions)); File.Move(temporary, FilePath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
}
public sealed class Platform(string id, string label, bool enabled)
{
    public string Id { get; } = id;
    public string Folder => Id == "Win64" ? "Windows" : Id;
    public string Label { get; } = label;
    public bool Enabled { get; set; } = enabled;
    public bool Checked { get; set; }
    public bool Detected { get; set; }
    public string State { get; set; } = "";
}
public static class Catalog
{
    public static List<Platform> Load(string engine, string? host = null)
    {
        host ??= Host.Name;
        var list = new List<Platform> {
            new("Win64", "Windows (Win64)", host == "Windows"), new("Android", "Android / Quest", true),
            new("Linux", "Linux x64 (SDK required)", host != "Mac"), new("LinuxArm64", "Linux ARM64 (SDK required)", host != "Mac"),
            new("Mac", "macOS", host == "Mac"), new("IOS", "iOS / iPadOS (signing / Mac required)", host != "Linux"),
            new("TVOS", "tvOS (signing / Mac required)", host != "Linux"), new("VisionOS", "visionOS (Apple SDK required)", host == "Mac") };
        if (string.IsNullOrWhiteSpace(engine)) return list;
        foreach (var relative in new[] { "Engine/Config", "Engine/Platforms", "Engine/Restricted" })
        {
            var root = Path.Combine(engine, relative); if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "DataDrivenPlatformInfo.ini", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); bool inside = false;
                foreach (var raw in File.ReadLines(file)) { var line = raw.Trim(); if (line.StartsWith('[')) { inside = line.Equals("[DataDrivenPlatformInfo]", StringComparison.OrdinalIgnoreCase); continue; } var parts = line.Split('=', 2); if (inside && parts.Length == 2 && !line.StartsWith(';') && !line.StartsWith('#')) values[parts[0].Trim()] = parts[1].Trim(); }
                if (!values.ContainsKey("PlatformGroupName") && !values.ContainsKey("UBTPlatformName")) continue;
                var dir = new DirectoryInfo(Path.GetDirectoryName(file)!); var id = values.GetValueOrDefault("UBTPlatformName", dir.Name == "Config" ? dir.Parent!.Name : dir.Name);
                if (!Regex.IsMatch(id, "^[A-Za-z][A-Za-z0-9_]*$")) continue;
                var item = list.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (item == null) { item = new(id, id + " (engine extension / SDK required)", true); list.Add(item); }
                item.Detected = true;
                if (values.TryGetValue(host + ":bIsEnabled", out var allowed) || values.TryGetValue("bIsEnabled", out allowed)) item.Enabled = allowed.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }
        return list;
    }
    public static void Move(List<Platform> items, int source, int target)
    { if (source < 0 || target < 0 || source >= items.Count || target >= items.Count || source == target) return; var item = items[source]; items.RemoveAt(source); items.Insert(target, item); }
}
public static class Toolchain
{
    public static Dictionary<string, string> EnvironmentFor(string projectRoot, bool android)
    {
        var env = new Dictionary<string, string>(); var root = Path.Combine(projectRoot, "Saved/AndroidToolchain");
        if (!android || !Directory.Exists(root)) return env;
        string sdk = Path.Combine(root, "sdk"), ndk = Path.Combine(sdk, "ndk/27.2.12479018");
        string java = Path.Combine(root, "jdk21/jdk-21.0.12.1+1"), suffix = Host.Name == "Windows" ? ".exe" : "";
        if (!File.Exists(Path.Combine(java, "bin/java" + suffix)))
        {
            java = new[] { Path.Combine(java, "Contents/Home"), Path.Combine(root, "jdk"), Path.Combine(root, "jdk/Contents/Home") }.FirstOrDefault(p => File.Exists(Path.Combine(p, "bin/java" + suffix))) ?? java;
        }
        // Only select a local toolchain when it contains binaries for this host.
        if (!File.Exists(Path.Combine(java, "bin/java" + suffix)) || !File.Exists(Path.Combine(sdk, "platform-tools/adb" + suffix)))
        {
            if (Host.Name != "Windows") return env;
            throw new IOException("Project-local Android toolchain is incomplete. Check Saved/AndroidToolchain.");
        }
        if (!File.Exists(Path.Combine(ndk, "source.properties"))) throw new IOException("Missing local NDK: " + ndk);
        env["ANDROID_HOME"] = sdk; env["NDKROOT"] = ndk; env["JAVA_HOME"] = java;
        var temp = Path.Combine(projectRoot, "Saved/JavaTemp"); Directory.CreateDirectory(temp);
        var options = Environment.GetEnvironmentVariable("JAVA_TOOL_OPTIONS") ?? "";
        env["JAVA_TOOL_OPTIONS"] = options.Contains("jdk.net.unixdomain.tmpdir") ? options : options + " -Djdk.net.unixdomain.tmpdir=\"" + temp + "\"";
        return env;
    }
}
public sealed class BuildRunner
{
    readonly object gate = new(); Process? process; ProcessJob? job; bool stopped;
    public bool Stopped { get { lock (gate) return stopped; } }
    public void Stop()
    {
        lock (gate) { stopped = true; if (process is null || process.HasExited) return; if (job != null) job.Stop(); else process.Kill(entireProcessTree: true); }
    }
    void CheckStop() { if (Stopped) throw new OperationCanceledException(); }
    public static string[] Arguments(Settings s, Platform p) => new[] { "BuildCookRun", "-project=" + s.Project, "-noP4", "-platform=" + p.Id, "-clientconfig=Development", "-ubtargs=-DisableUnity -NoUBA", "-build", "-cook", "-stage", "-pak", "-package", "-archive", "-archivedirectory=" + Path.Combine(s.Destination, p.Folder), "-utf8output", "-unattended" }.Concat(p.Id == "Android" ? ["-cookflavor=" + s.AndroidFlavor] : Array.Empty<string>()).ToArray();
    public static ProcessStartInfo StartInfo(Settings s, Platform p, string? host = null)
    {
        host ??= Host.Name;
        var info = new ProcessStartInfo { WorkingDirectory = Path.GetDirectoryName(s.Project), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        if (host == "Windows")
        {
            static string Quote(string value) { if (value.IndexOfAny(['"', '\r', '\n', '%']) >= 0) throw new ArgumentException("Windows batch paths cannot contain quotes, newlines or %."); return "\"" + value + "\""; }
            info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            info.Arguments = "/d /s /v:off /c \"" + Quote(Host.Uat(s.EngineRoot, host)) + " " + string.Join(" ", Arguments(s, p).Select(Quote)) + "\"";
        }
        else
        {
            info.FileName = "/bin/bash"; info.ArgumentList.Add(Host.Uat(s.EngineRoot, host));
            foreach (var arg in Arguments(s, p)) info.ArgumentList.Add(arg);
        }
        return info;
    }
    public void Run(Settings s, IReadOnlyList<Platform> selected, bool validate, Action<string> log, Action<Platform, string> state)
    {
        if (!File.Exists(s.Project) || !s.Project.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase)) throw new IOException("Select a valid .uproject file.");
        using var projectJson = JsonDocument.Parse(File.ReadAllText(s.Project));
        if (!File.Exists(Host.Uat(s.EngineRoot))) throw new IOException("Unreal AutomationTool launcher not found: " + Host.Uat(s.EngineRoot));
        if (!Path.IsPathFullyQualified(s.Destination)) throw new IOException("Choose an absolute destination folder.");
        if (selected.Count == 0 || selected.Any(p => !p.Enabled)) throw new InvalidOperationException("Select at least one platform supported on this host.");
        if (!new[] { "ASTC", "ETC2", "DXT", "Multi" }.Contains(s.AndroidFlavor)) throw new ArgumentException("Invalid Android format.");
        var editors = Process.GetProcessesByName("UnrealEditor"); bool open = editors.Length > 0; foreach (var editor in editors) editor.Dispose();
        if (open) throw new InvalidOperationException("Save and close Unreal Editor before packaging.");
        var root = Path.GetDirectoryName(s.Project)!; var builds = Path.Combine(root, "Builds"); Directory.CreateDirectory(builds); Directory.CreateDirectory(s.Destination);
        using var projectLock = new FileStream(Path.Combine(builds, ".PackageBoth.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var destinationLock = new FileStream(Path.Combine(s.Destination, ".PackagingDestination.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var env = Toolchain.EnvironmentFor(root, selected.Any(p => p.Id == "Android"));
        log("Host: " + Host.Name); log("Project: " + s.Project); log("Destination: " + s.Destination); log(env.Count == 0 ? "Using installed SDK environment." : "Using project-local Android toolchain.");
        CheckStop();
        if (validate) { foreach (var p in selected) state(p, "Setup checked"); log("Preflight passed. SDK, signing and build compatibility are validated by UAT during packaging."); return; }
        foreach (var p in selected)
        {
            CheckStop(); state(p, "Running"); log("===== START " + p.Id + " =====");
            try
            {
                var info = StartInfo(s, p); foreach (var pair in env) info.Environment[pair.Key] = pair.Value;
                using var child = new Process { StartInfo = info }; using var group = OperatingSystem.IsWindows() ? new ProcessJob() : null;
                child.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); }; child.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
                lock (gate) { CheckStop(); child.Start(); try { group?.Attach(child); } catch { if (!child.HasExited) child.Kill(true); throw; } process = child; job = group; }
                try { child.BeginOutputReadLine(); child.BeginErrorReadLine(); child.WaitForExit(); CheckStop(); if (child.ExitCode != 0) throw new IOException(p.Id + " failed (exit " + child.ExitCode + ")."); }
                finally { lock (gate) { process = null; job = null; } }
                var archive = Path.Combine(s.Destination, p.Folder);
                if (!Directory.Exists(archive) || !Directory.EnumerateFiles(archive, "*", SearchOption.AllDirectories).Any(f => new FileInfo(f).Length > 0)) throw new IOException("UAT succeeded but no non-empty archived output found: " + archive);
                state(p, "Completed"); log("===== SUCCESS " + p.Id + " =====");
            }
            catch { state(p, Stopped ? "Stopped" : "Failed"); throw; }
        }
    }
}
