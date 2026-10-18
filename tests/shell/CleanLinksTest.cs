using System;
class T {
  static int fails = 0;
  static void Eq(string input, string want) {
    string got = Sidearm.CleanLinks.Clean(input);
    if (got != want) { fails++; Console.WriteLine("FAIL\n  in:   " + input + "\n  got:  " + got + "\n  want: " + want); }
  }
  static void Main() {
    Eq("https://example.com/a?utm_source=x&utm_medium=y&id=5", "https://example.com/a?id=5");
    Eq("https://example.com/a?id=5&fbclid=IwAR123", "https://example.com/a?id=5");
    Eq("https://example.com/a?fbclid=IwAR123#sec", "https://example.com/a#sec");
    Eq("https://www.youtube.com/watch?v=dQw4w9WgXcQ&si=abc123&t=42", "https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42");
    Eq("https://youtu.be/dQw4w9WgXcQ?si=abc", "https://youtu.be/dQw4w9WgXcQ");
    Eq("https://www.google.com/url?sa=t&rct=j&q=&url=https%3A%2F%2Fen.wikipedia.org%2Fwiki%2FKyiv%3Futm_source%3Dg&usg=AOv", "https://en.wikipedia.org/wiki/Kyiv");
    Eq("https://l.facebook.com/l.php?u=https%3A%2F%2Fnews.example.org%2Fstory%3Fid%3D7%26fbclid%3Dzz&h=AT0", "https://news.example.org/story?id=7");
    Eq("https://www.amazon.com/Some-Thing/dp/B0ABCDEF12/ref=sr_1_3?crid=2X&keywords=mouse&qid=1&sr=8-3&th=1", "https://www.amazon.com/Some-Thing/dp/B0ABCDEF12?crid=2X&keywords=mouse&qid=1&sr=8-3&th=1");
    Eq("https://x.com/user/status/123?s=20&t=abcd", "https://x.com/user/status/123");
    Eq("https://open.spotify.com/track/abc?si=xyz", "https://open.spotify.com/track/abc");
    Eq("https://duckduckgo.com/?q=a+b&t=h_", "https://duckduckgo.com/?q=a+b&t=h_");
    Eq("https://en.wikipedia.org/wiki/Main_Page", "https://en.wikipedia.org/wiki/Main_Page");
    Eq("https://www.google.com/search?q=sidearm&srsltid=AfmBOo", "https://www.google.com/search?q=sidearm");
    Eq("https://site.example/p?a=1&a=2&utm_campaign=z&b", "https://site.example/p?a=1&a=2&b");
    Eq("https://site.example/p#utm_source=feed", "https://site.example/p");
    Eq("https://site.example/p#:~:text=hello", "https://site.example/p#:~:text=hello");
    Eq("https://vk.com/away.php?to=https%3A%2F%2Fexample.com%2F", "https://example.com/");
    Eq("not a url", "not a url");
    Console.WriteLine(fails == 0 ? "all clean-link tests passed" : fails + " failed");
  }
}
