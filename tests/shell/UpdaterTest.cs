using System; using System.Reflection; using System.IO; using System.IO.Compression; using System.Collections.Generic;
using System.Security.Cryptography; using System.Web.Script.Serialization;
// 0.8 updater: version compare, reading GitHub's answer, checking and unpacking the zip, swapping files, rollback, confirm.
class U {
  static int fail = 0;
  static void Ok(bool c, string what) { if (!c) { fail++; Console.WriteLine("FAIL " + what); } }
  static string Sha(string p) { using (var s = SHA256.Create()) using (var f = File.OpenRead(p)) return BitConverter.ToString(s.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
  static void Zip(string path, Dictionary<string, string> files) {
    if (File.Exists(path)) File.Delete(path);
    using (var z = ZipFile.Open(path, ZipArchiveMode.Create)) foreach (var kv in files) { var e = z.CreateEntry(kv.Key); using (var w = new StreamWriter(e.Open())) w.Write(kv.Value); }
  }
  static void Main() {
    var asm = Assembly.LoadFrom("Sidearm.exe"); var BF = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    var prog = asm.GetType("Sidearm.Program"); var up = asm.GetType("Sidearm.Updater");
    string root = Path.GetFullPath("upd"); if (Directory.Exists(root)) Directory.Delete(root, true);
    string app = Path.Combine(root, "Sidearm"), data = Path.Combine(app, "data");
    Directory.CreateDirectory(Path.Combine(app, "ui")); Directory.CreateDirectory(data);
    File.WriteAllText(Path.Combine(app, "Sidearm.exe"), "old exe"); File.WriteAllText(Path.Combine(app, "ui", "chrome.html"), "old ui");
    File.WriteAllText(Path.Combine(data, "settings.json"), "mine");
    prog.GetField("AppDir", BF).SetValue(null, app); prog.GetField("DataDir", BF).SetValue(null, data); prog.GetField("UiDir", BF).SetValue(null, Path.Combine(app, "ui"));
    Func<string, string, int> cmp = (a, b) => (int)up.GetMethod("Compare", BF).Invoke(null, new object[] { a, b });
    Ok(cmp("v0.8.1", "0.8.0") > 0, "0.8.1 > 0.8.0"); Ok(cmp("0.8.0", "0.8.0") == 0, "equal"); Ok(cmp("v0.10.0", "0.9.9") > 0, "0.10 > 0.9.9"); Ok(cmp("0.7.2", "0.8") < 0, "0.7.2 < 0.8");
    var js = new JavaScriptSerializer(); var read = up.GetMethod("Read", BF);
    Func<string, bool> Read = json => (bool)read.Invoke(null, new object[] { js.Deserialize<Dictionary<string, object>>(json) });
    Func<string, object> F = n => up.GetField(n, BF) != null ? up.GetField(n, BF).GetValue(null) : up.GetProperty(n, BF).GetValue(null, null);
    string dl = "https://github.com/HARDv11/Sidearm/releases/download/v99.0.0/";
    Ok(!Read("{\"tag_name\":\"v0.0.1\",\"assets\":[]}"), "older release is not an update");
    Ok(Read("{\"tag_name\":\"v99.0.0\",\"name\":\"99.0.0 · Test\",\"body\":\"notes\",\"assets\":[{\"name\":\"Sidearm-99.0.0-share-uk.zip\",\"browser_download_url\":\"" + dl + "Sidearm-99.0.0-share-uk.zip\",\"digest\":\"sha256:ABC\"}]}"), "newer release");
    Ok((string)F("Latest") == "99.0.0" && (bool)F("CanInstall"), "latest 99.0.0 installable");
    Ok(Read("{\"tag_name\":\"v99.0.0\",\"assets\":[{\"name\":\"Sidearm-99.0.0-share-uk.zip\",\"browser_download_url\":\"https://evil.example/x.zip\"}]}") && !(bool)F("CanInstall"), "foreign download link refused");
    Ok(Read("{\"tag_name\":\"v99.0.0\",\"assets\":[{\"name\":\"notes.txt\",\"browser_download_url\":\"" + dl + "notes.txt\"}]}") && !(bool)F("CanInstall"), "no zip, nothing to install");
    // unpack: right checksum, wrong checksum, a path that escapes
    string z1 = Path.Combine(root, "good.zip"), un = Path.Combine(root, "new");
    var files = new Dictionary<string, string> { { "Sidearm/Sidearm.exe", "new exe" }, { "Sidearm/ui/chrome.html", "new ui" }, { "Sidearm/ui/notes.css", "added" }, { "Sidearm/data/settings.json", "theirs" }, { "Sidearm/defaults.json", "{\"language\":\"uk\"}" } };
    Zip(z1, files); string sha = Sha(z1);
    var unpack = up.GetMethod("Unpack", BF);
    string got = null;
    try { got = (string)unpack.Invoke(null, new object[] { z1, sha, un }); } catch (TargetInvocationException e) { Console.WriteLine(e.InnerException.Message); }
    Ok(got != null && File.ReadAllText(Path.Combine(got, "Sidearm.exe")) == "new exe", "unpacked with the right checksum");
    Zip(z1, files);
    try { unpack.Invoke(null, new object[] { z1, new string('0', 64), un }); Ok(false, "wrong checksum accepted"); } catch (TargetInvocationException e) { Ok(e.InnerException.Message.Contains("checksum"), "wrong checksum refused"); }
    Zip(z1, new Dictionary<string, string> { { "../evil.txt", "x" }, { "Sidearm/Sidearm.exe", "e" } }); sha = Sha(z1);
    try { unpack.Invoke(null, new object[] { z1, sha, un }); Ok(false, "escaping path accepted"); } catch (TargetInvocationException e) { Ok(e.InnerException.Message.Contains("strange path"), "escaping path refused"); }
    Ok(!File.Exists(Path.Combine(root, "evil.txt")), "nothing written outside");
    // apply, two failed starts, then rollback on the third
    Zip(z1, files); got = (string)unpack.Invoke(null, new object[] { z1, Sha(z1), un });
    up.GetMethod("Apply", BF).Invoke(null, new object[] { got });
    Ok(File.ReadAllText(Path.Combine(app, "Sidearm.exe")) == "new exe" && File.ReadAllText(Path.Combine(app, "Sidearm.exe.old")) == "old exe", "exe swapped, old kept");
    Ok(File.ReadAllText(Path.Combine(app, "ui", "notes.css")) == "added", "new file added");
    Ok(File.ReadAllText(Path.Combine(data, "settings.json")) == "mine", "data untouched");
    Ok(!File.Exists(Path.Combine(app, "defaults.json")), "defaults.json skipped");
    var onStart = up.GetMethod("OnStartup", BF);
    Ok(!(bool)onStart.Invoke(null, null) && !(bool)onStart.Invoke(null, null), "first two starts carry on");
    Ok((bool)onStart.Invoke(null, null), "third start rolls back");
    Ok(File.ReadAllText(Path.Combine(app, "Sidearm.exe")) == "old exe" && File.ReadAllText(Path.Combine(app, "ui", "chrome.html")) == "old ui", "old files back");
    Ok(!File.Exists(Path.Combine(data, "update", "pending.json")), "pending cleared after rollback");
    Ok(!(bool)onStart.Invoke(null, null) && !File.Exists(Path.Combine(app, "Sidearm.exe.bad")), "leftovers cleaned on the next start");
    // apply again; this time it works and confirms
    Zip(z1, files); got = (string)unpack.Invoke(null, new object[] { z1, Sha(z1), un });
    up.GetMethod("Apply", BF).Invoke(null, new object[] { got });
    Ok(!(bool)onStart.Invoke(null, null), "start after update");
    up.GetMethod("Confirm", BF).Invoke(null, null);
    Ok(!File.Exists(Path.Combine(app, "Sidearm.exe.old")) && !File.Exists(Path.Combine(data, "update", "pending.json")), "confirmed: .old files and pending gone");
    Ok(File.ReadAllText(Path.Combine(app, "Sidearm.exe")) == "new exe", "new version stays");
    Console.WriteLine(fail == 0 ? "ALL PASSED" : fail + " FAILED");
  }
}
