using System;
using System.IO;
using System.Media;

namespace Sidearm
{
    /// <summary>A short two-note chime for finished timers, made in memory (no sound files to ship).</summary>
    internal static class Chime
    {
        static byte[] wav;
        static SoundPlayer player;

        public static void Play()
        {
            try
            {
                if (wav == null) wav = Make();
                if (player != null) player.Dispose();
                player = new SoundPlayer(new MemoryStream(wav));
                player.Play(); // async: returns at once
            }
            catch (Exception ex) { Log.Error("Chime", ex); try { SystemSounds.Exclamation.Play(); } catch (Exception) { } }
        }

        /// <summary>Two rounds of E6 then A6 with a soft bell decay, 16-bit mono, 22 kHz.</summary>
        static byte[] Make()
        {
            const int rate = 22050;
            double[] notes = { 1318.5, 1760.0, 0, 1318.5, 1760.0 };
            double[] lengths = { 0.16, 0.34, 0.12, 0.16, 0.42 };
            int total = 0;
            foreach (var l in lengths) total += (int)(l * rate);
            var samples = new short[total];
            int pos = 0;
            for (int n = 0; n < notes.Length; n++)
            {
                int len = (int)(lengths[n] * rate);
                for (int i = 0; i < len; i++, pos++)
                {
                    if (notes[n] == 0) continue;
                    double t = (double)i / rate;
                    double attack = Math.Min(1.0, i / (0.006 * rate));
                    double env = attack * Math.Exp(-t * 7.5);
                    double v = Math.Sin(2 * Math.PI * notes[n] * t) * 0.7 + Math.Sin(2 * Math.PI * notes[n] * 2 * t) * 0.18 + Math.Sin(2 * Math.PI * notes[n] * 3 * t) * 0.06;
                    samples[pos] = (short)(v * env * 0.32 * short.MaxValue);
                }
            }
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int data = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + data);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
                w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(data);
                foreach (var s in samples) w.Write(s);
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
