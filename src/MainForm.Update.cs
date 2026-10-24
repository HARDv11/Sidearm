using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sidearm
{
    /// <summary>0.8: the update check and the one-click update (see <see cref="Updater"/>).</summary>
    internal sealed partial class MainForm
    {
        Task updateCheck;
        string updateToasted;

        /// <summary>After boot: confirm a fresh update once it has run a while, and check GitHub a minute in.</summary>
        void InitUpdates()
        {
            ReportFailedUpdate();
            var confirm = new Timer { Interval = 30000 };
            confirm.Tick += (s, e) => { confirm.Stop(); confirm.Dispose(); Updater.Confirm(); };
            confirm.Start();
            var first = new Timer { Interval = 60000 };
            first.Tick += (s, e) => { first.Stop(); first.Dispose(); CheckUpdatesIfDue(); };
            first.Start();
        }

        /// <summary>Once a day at most, in the background. Called at boot and on every draw.</summary>
        void CheckUpdatesIfDue()
        {
            if (!S.updateCheck || updateCheck != null) return;
            DateTime last;
            if (DateTime.TryParse(S.lastUpdateCheck, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out last)
                && (DateTime.UtcNow - last).TotalHours < 24)
            {
                OfferUpdate(); // a check from earlier today may already have found one
                return;
            }
            CheckUpdatesNow(null);
        }

        void CheckUpdatesNow(Tab settingsTab)
        {
            if (updateCheck != null) return;
            updateCheck = Task.Run(() => Updater.Check());
            updateCheck.ContinueWith(t =>
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        updateCheck = null;
                        S.lastUpdateCheck = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                        SaveSettingsSoon();
                        OfferUpdate();
                        if (settingsTab != null) PushSettingsSnapshot(settingsTab);
                    }));
                }
                catch (Exception) { updateCheck = null; }
            });
        }

        /// <summary>A newer version: say so once per version, the next time Sidearm is on screen.</summary>
        void OfferUpdate()
        {
            if (Updater.Latest == null || updateToasted == Updater.Latest || !Visible) return;
            updateToasted = Updater.Latest;
            if (Updater.CanInstall) Toast("Sidearm " + Updater.Latest + " is out", "Update", "update");
            else Toast("Sidearm " + Updater.Latest + " is out. Get it from the Releases page on GitHub", null, null);
        }

        async void UpdateNow()
        {
            if (!Updater.CanInstall) { Toast("No update to install right now", null, null); return; }
            Updater.Busy = true;
            string version = Updater.Latest;
            Updater.Status = "Downloading Sidearm " + version + "…";
            Toast(Updater.Status, null, null);
            string app;
            try { app = await Task.Run(() => Updater.Prepare()); }
            catch (Exception ex)
            {
                Updater.Busy = false;
                Updater.Status = "Update failed: " + ex.Message;
                Log.Error("Update to " + version, ex);
                Toast("Sidearm couldn't update: " + ex.Message, null, null);
                foreach (var t in tabs) if (IsTrustedPage(t.Url)) PushSettingsSnapshot(t);
                return;
            }
            // Checked and unpacked. From here Sidearm restarts either way: into the new version, or (if a file
            // couldn't be swapped, and everything was put back) into this one, which then says what went wrong.
            Toast("Restarting into Sidearm " + version + "…", null, null);
            bool tray = !Visible;
            await Task.Delay(500);
            ReleaseEngineForUpdate(); // so no page engine holds a file in ui\ open while it's swapped
            await Task.Delay(1500);
            try { await Task.Run(() => Updater.Apply(app)); }
            catch (Exception ex)
            {
                Log.Error("Installing " + version, ex);
                Updater.RememberFailure(ex.Message);
            }
            try { Updater.Restart(tray); } catch (Exception ex) { Log.Error("Restart after update", ex); }
            Quit();
        }

        /// <summary>Saves everything and closes the page engine (the restart that follows brings it back).</summary>
        void ReleaseEngineForUpdate()
        {
            try
            {
                S.Save();
                SaveSession();
                CloseGlance();
                foreach (var t in tabs) if (t.View != null) DisposeView(t);
                DropQuick();
                DropSpare();
                if (video != null && !video.IsDisposed) video.Close();
                ClosePops();
                FlushRaven();
                if (ravenWin != null) { ravenWin.Dispose(); ravenWin = null; }
                if (ravenTabs != null) { ravenTabs.Dispose(); ravenTabs = null; }
                if (tabsView != null) { tabsView.Dispose(); tabsView = null; }
                if (ui != null) { ui.Dispose(); ui = null; }
            }
            catch (Exception ex) { Log.Error("Closing the engine for an update", ex); }
        }

        /// <summary>At boot: an update that failed while installing says so (the old version is back).</summary>
        void ReportFailedUpdate()
        {
            string msg = Updater.TakeFailure();
            if (msg == null) return;
            Updater.Status = "Update failed: " + msg;
            ShowBalloon("Sidearm couldn't update", Lang.T(msg) + " " + Lang.T("This is still the earlier version."));
        }

        object UpdateInfo()
        {
            return new
            {
                current = Updater.Current,
                latest = Updater.Latest ?? "",
                title = Updater.LatestTitle,
                notes = Updater.Latest != null ? Updater.Notes : "",
                status = updateCheck != null ? "Checking GitHub…" : Updater.Status,
                can = Updater.CanInstall,
                busy = Updater.Busy,
                page = Updater.PageUrl
            };
        }
    }
}
