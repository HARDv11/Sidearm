using System;
class T2 {
  static int fails = 0;
  static void Eq(string got, string want) { if (got != want) { fails++; Console.WriteLine("FAIL got  " + got + "\n     want " + want); } }
  static void Main() {
    var f = System.Reflection.Assembly.LoadFrom("Sidearm.exe").GetType("Sidearm.MainForm");
    var tr = f.GetMethod("TranslateUrl", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    var orig = f.GetMethod("OriginalOf", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Func<string,string> T = u => (string)tr.Invoke(null, new object[] { u, "uk" });
    Func<string,string> O = u => (string)orig.Invoke(null, new object[] { u });
    Eq(T("https://de.wikipedia.org/wiki/Kiew"), "https://de-wikipedia-org.translate.goog/wiki/Kiew?_x_tr_sl=auto&_x_tr_tl=uk&_x_tr_hl=uk&_x_tr_pto=wapp");
    Eq(T("https://www.my-site.co.uk/a/b?x=1#top"), "https://www-my--site-co-uk.translate.goog/a/b?x=1&_x_tr_sl=auto&_x_tr_tl=uk&_x_tr_hl=uk&_x_tr_pto=wapp#top");
    foreach (var u in new[] { "https://de.wikipedia.org/wiki/Kiew", "https://www.my-site.co.uk/a/b?x=1#top", "https://a--b.example.com/" })
      Eq(O(T(u)), u);
    Console.WriteLine(fails == 0 ? "translate url tests passed" : fails + " failed");
  }
}
