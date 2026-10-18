using System; using System.Reflection;
class T3 { static void Main() {
  var asm = Assembly.LoadFrom("Sidearm.exe");
  asm.GetType("Sidearm.Program").GetField("UiDir", BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).SetValue(null, Environment.GetEnvironmentVariable("SIDEARM_UI"));
  var lang = asm.GetType("Sidearm.Lang");
  lang.GetMethod("Load").Invoke(null, new object[]{"uk"});
  var T = lang.GetMethod("T");
  foreach (var s in new[]{"Copied link","Recycled “Some page” (5-tab cap)","Open link in Chrome (private)","Ask Claude about “word”","Ghost mode  (Alt+G)","This page is in German","Not a known string","Saved to Downloads: cat.png (kept as WEBP)"})
    Console.WriteLine(s + "  =>  " + T.Invoke(null, new object[]{s}));
}}
