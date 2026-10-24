using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Sidearm
{
    /// <summary>0.9: clipboard history in both bars (`cb`, `буфер`, Alt + V). See ClipHistory: memory only.</summary>
    internal sealed partial class MainForm
    {
        readonly ClipHistory clips = new ClipHistory();
        readonly Timer clipTimer = new Timer { Interval = 700 };
        bool clipsLogged;

        void InitClips()
        {
            clips.Changed += PushClips;
            clipTimer.Tick += (s, e) =>
            {
                bool shown = Visible || (quick != null && quick.Visible);
                if (!shown) { clipTimer.Stop(); return; }
                clips.Check(() => !ghost && IsForegroundOurs());
            };
        }

        /// <summary>Sidearm (or the quick bar) came up: what's on the clipboard now was copied elsewhere.</summary>
        void StartClipWatch()
        {
            if (clipTimer.Enabled) return;
            clips.Baseline();
            clipTimer.Start();
        }

        void PushClips()
        {
            if (!clipsLogged && clips.Items.Count > 0) { clipsLogged = true; Log.Info("Clipboard history: first copy kept this session (memory only)"); }
            var list = clips.Snapshot();
            PostToUi(new { type = "clips", list = list });
            PostToQuick(new { type = "clips", list = list });
        }

        /// <summary>`clip` messages from either bar: restore an item, or clear the list.</summary>
        void OnClipMessage(Dictionary<string, object> m)
        {
            string op = Json.Str(m, "op");
            if (op == "clear") { clips.Clear(); Toast("Clipboard history cleared", null, null); return; }
            if (op == "get") { PushClips(); return; }
            if (op == "restore")
            {
                if (clips.Restore((int)Json.Num(m, "id", 0))) Toast("Copied again", null, null);
            }
        }

        /// <summary>Alt + V: the bar opens on the clipboard history.</summary>
        void ShowClips()
        {
            if (!Visible) Draw();
            FocusBar(false);
            PostToUi(new { type = "prefill", text = "cb " });
        }
    }
}
