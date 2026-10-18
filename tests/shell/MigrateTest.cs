using System; using System.Reflection; using System.IO;
class M { static void Main() {
  var asm = Assembly.LoadFrom("Sidearm.exe"); var BF = BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
  var prog = asm.GetType("Sidearm.Program"); string root = Path.GetFullPath("mig/Downloads");
  prog.GetField("AppDir", BF).SetValue(null, root + "/Sidearm-0.7.0-share-uk/Sidearm");
  prog.GetField("DataDir", BF).SetValue(null, root + "/Sidearm-0.7.0-share-uk/Sidearm/data");
  var mg = asm.GetType("Sidearm.Migrate");
  var found = (string)mg.GetMethod("FindEarlier", BF).Invoke(null, null);
  Console.WriteLine("found: " + found);
  var copy = mg.GetMethod("Copy", BF); var args = new object[]{ found, root + "/Sidearm-0.7.0-share-uk/Sidearm/data", 0, 0L };
  copy.Invoke(null, args); Console.WriteLine("files " + args[2]);
  foreach (var f in Directory.GetFiles(root + "/Sidearm-0.7.0-share-uk/Sidearm/data", "*", SearchOption.AllDirectories)) Console.WriteLine("  " + f.Substring(root.Length));
}}
