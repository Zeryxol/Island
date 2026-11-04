using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WF = System.Windows.Forms;

namespace Island
{
    // ---------- Core Audio (master volume) ----------

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr p);
        void UnregisterControlChangeNotify(IntPtr p);
        void GetChannelCount(out uint c);
        void SetMasterVolumeLevel(float l, ref Guid g);
        void SetMasterVolumeLevelScalar(float l, ref Guid g);
        void GetMasterVolumeLevel(out float l);
        void GetMasterVolumeLevelScalar(out float l);
        void SetChannelVolumeLevel(uint c, float l, ref Guid g);
        void SetChannelVolumeLevelScalar(uint c, float l, ref Guid g);
        void GetChannelVolumeLevel(uint c, out float l);
        void GetChannelVolumeLevelScalar(uint c, out float l);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool m, ref Guid g);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool m);
    }

    // ---------- per-app audio sessions (who is actually making sound) ----------

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        void GetAudioSessionControl(IntPtr guid, uint flags, out IntPtr ctl);
        void GetSimpleAudioVolume(IntPtr guid, uint flags, out IntPtr vol);
        void GetSessionEnumerator(out IAudioSessionEnumerator e);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        void GetCount(out int n);
        void GetSession(int i, out IAudioSessionControl2 s);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        void GetState(out int state);
        void GetDisplayName(IntPtr name);
        void SetDisplayName(IntPtr a, IntPtr b);
        void GetIconPath(IntPtr p);
        void SetIconPath(IntPtr a, IntPtr b);
        void GetGroupingParam(IntPtr g);
        void SetGroupingParam(IntPtr a, IntPtr b);
        void RegisterAudioSessionNotification(IntPtr n);
        void UnregisterAudioSessionNotification(IntPtr n);
        void GetSessionIdentifier(IntPtr s);
        void GetSessionInstanceIdentifier(IntPtr s);
        void GetProcessId(out uint pid);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        void GetPeakValue(out float peak);
    }

    static class AudioSessions
    {
        // every other allowed process producing sound right now: pid -> peak level
        public static Dictionary<uint, float> Active(Func<string, bool> allowed)
        {
            var res = new Dictionary<uint, float>();
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                IMMDevice dev; en.GetDefaultAudioEndpoint(0, 1, out dev);
                var iid = typeof(IAudioSessionManager2).GUID; object o;
                dev.Activate(ref iid, 23, IntPtr.Zero, out o);
                IAudioSessionEnumerator list; ((IAudioSessionManager2)o).GetSessionEnumerator(out list);
                int n; list.GetCount(out n);
                uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                for (int i = 0; i < n; i++)
                {
                    IAudioSessionControl2 s; list.GetSession(i, out s);
                    uint pid; s.GetProcessId(out pid);
                    if (pid == 0 || pid == self) continue;
                    string name; try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { continue; }
                    if (!allowed(name)) continue;
                    float peak; ((IAudioMeterInformation)s).GetPeakValue(out peak);
                    if (peak > 0.002f) { float prev; res.TryGetValue(pid, out prev); res[pid] = Math.Max(prev, peak); }
                }
            }
            catch { }
            return res;
        }
    }

    static class Volume
    {
        static IAudioEndpointVolume ep;
        static Guid ctx = Guid.Empty;

        public static void Refresh()
        {
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                IMMDevice dev; en.GetDefaultAudioEndpoint(0, 1, out dev);
                var iid = typeof(IAudioEndpointVolume).GUID; object o;
                dev.Activate(ref iid, 1, IntPtr.Zero, out o);
                ep = (IAudioEndpointVolume)o;
            }
            catch { ep = null; }
        }

        public static bool Get(out float level, out bool muted)
        {
            level = 0; muted = false;
            if (ep == null) Refresh();
            if (ep == null) return false;
            try { ep.GetMasterVolumeLevelScalar(out level); ep.GetMute(out muted); return true; }
            catch { ep = null; return false; }
        }

        public static void Set(float level)
        {
            if (ep == null) return;
            try { ep.SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(1, level)), ref ctx); if (level > 0) ep.SetMute(false, ref ctx); } catch { }
        }
    }

    // Windows "Do not disturb" (Focus Assist) through the shell's quiet hours service
    [ComImport, Guid("6bff4732-81ec-4ffb-ae67-b6c1bc29631f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IQuietHoursSettings
    {
        [return: MarshalAs(UnmanagedType.LPWStr)] string get_UserSelectedProfile();
        void put_UserSelectedProfile([MarshalAs(UnmanagedType.LPWStr)] string id);
    }

    static class QuietHours
    {
        const string Off = "Microsoft.QuietHoursProfile.Unrestricted", On = "Microsoft.QuietHoursProfile.AlarmsOnly";

        static IQuietHoursSettings Svc()
        {
            return (IQuietHoursSettings)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("f53321fa-34f8-4b7f-b9a3-361877cb94cf")));
        }

        public static bool? IsOn()
        {
            try { return Svc().get_UserSelectedProfile() != Off; } catch { return null; }
        }

        public static void Set(bool on)
        {
            try { Svc().put_UserSelectedProfile(on ? On : Off); } catch { }
        }
    }

    partial class IslandWindow
    {
        const double LW = 290, LH = 40;

        Grid layerLevel; Border levelFill; TextBlock levelGlyph, levelPct;
        float lastVol = -1; bool lastMute;

        bool dnd, fullscreen;
        TextBlock moon, dndGlyph;
        StackPanel eq;

        // media
        string mTitle, mArtist; ImageSource mArt; bool mPlaying, mHas;
        Border mediaArt; TextBlock mediaTitle, mediaArtist, playGlyph;
        int lowBatteryWarned = 101;

        void InitFeatures(Grid root)
        {
            root.Children.Add(layerLevel = BuildLevel());
            layerLevel.Opacity = 0; layerLevel.IsHitTestVisible = false;

            moon = UI.Glyph("", 11, new SolidColorBrush(UI.Purple));
            moon.HorizontalAlignment = HorizontalAlignment.Left; moon.Visibility = Visibility.Collapsed;
            layerCollapsed.Children.Add(moon);
            eq = BuildEqualizer();
            layerCollapsed.Children.Add(eq);

            // scroll over the closed island = system volume
            pill.MouseWheel += (s, e) =>
            {
                // anywhere on the island that doesn't scroll by itself (cards, timer) = volume
                if (mode != Mode.Collapsed && mode != Mode.Level && mode != Mode.Expanded) return;
                float v; bool m;
                if (!Volume.Get(out v, out m)) return;
                var nv = Math.Max(0, Math.Min(1, v + (e.Delta > 0 ? 0.02f : -0.02f)));
                Volume.Set(nv);
                if (mode == Mode.Expanded) Flash("Громкость " + Math.Round(nv * 100) + "%");
                e.Handled = true;
            };
            // right click anywhere on the island (except cards) = do not disturb
            pill.MouseRightButtonUp += (s, e) => { SetDnd(!dnd); e.Handled = true; };

            // and a visible toggle next to the gauges
            dndGlyph = UI.Glyph("", 15, UI.Gray);
            var dndBtn = UI.Pressable(dndGlyph, 38, 38, 19, () => SetDnd(!dnd));
            dndBtn.ToolTip = L.T("Не беспокоить (или ПКМ по островку)");
            dndBtn.VerticalAlignment = VerticalAlignment.Top;
            dndBtn.Margin = new Thickness(0, 6, 18, 0);
            gaugePanel.Children.Insert(0, dndBtn);

            Volume.Get(out lastVol, out lastMute);
            System.Threading.ThreadPool.QueueUserWorkItem(_ => WatchMedia());
        }

        readonly System.Collections.Generic.List<string[]> missed = new System.Collections.Generic.List<string[]>();

        public void SetDnd(bool on)
        {
            dnd = on;
            QuietHours.Set(on);
            if (!on && missed.Count > 0)
            {
                var apps = missed.GroupBy(x => x[0]).Select(g => g.Count() > 1 ? g.Key + " ×" + g.Count() : g.Key);
                var lastMsg = missed[missed.Count - 1];
                string title = "Пропущено: " + missed.Count;
                string sub = string.Join(", ", apps) + "  ·  " + lastMsg[1] + ": " + lastMsg[2];
                missed.Clear();
                dndGlyph.Foreground = UI.Gray; moon.Visibility = Visibility.Collapsed; miniTime.Margin = new Thickness(0);
                if (mode == Mode.Expanded) SetMode(Mode.Collapsed);
                Notify("", UI.Purple, title, sub, null, 5000);
                return;
            }
            dndGlyph.Foreground = on ? new SolidColorBrush(UI.Purple) : UI.Gray;
            moon.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            miniTime.Margin = new Thickness(on ? 16 : 0, 0, 0, 0);
            if (mode == Mode.Expanded) { Flash(on ? "Не беспокоить" : "Уведомления включены"); return; }
            Notify("", UI.Purple, on ? "Не беспокоить" : "Уведомления включены",
                on ? "Сообщения не будут всплывать" : null, null, 1600);
        }

        Grid BuildLevel()
        {
            var g = new Grid { Width = LW, Height = LH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            var row = new DockPanel { Margin = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            levelGlyph = UI.Glyph("", 15, UI.Fg); levelGlyph.Width = 22;
            levelPct = UI.Text("", 12, UI.Fg, FontWeights.SemiBold); levelPct.Width = 34; levelPct.TextAlignment = TextAlignment.Right;
            DockPanel.SetDock(levelGlyph, Dock.Left); DockPanel.SetDock(levelPct, Dock.Right);
            var track = new Grid { Height = 5, Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(UI.Line) });
            levelFill = new Border { CornerRadius = new CornerRadius(2.5), Background = UI.Fg, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            track.Children.Add(levelFill);
            row.Children.Add(levelGlyph); row.Children.Add(levelPct); row.Children.Add(track);
            g.Children.Add(row);
            return g;
        }

        void ShowLevel(float v, bool muted)
        {
            if (fullscreen || mode == Mode.Expanded || mode == Mode.Drop) return;
            const double trackW = LW - 36 - 22 - 34 - 16;
            UI.Ease(levelFill, WidthProperty, muted ? 0 : trackW * v, 160);
            levelPct.Text = muted ? "-" : Math.Round(v * 100) + "";
            levelGlyph.Text = muted || v == 0 ? "" : v < 0.34 ? "" : v < 0.67 ? "" : "";
            if (mode != Mode.Level) SetMode(Mode.Level);
            noticeTimer.Stop(); noticeTimer.Interval = TimeSpan.FromMilliseconds(1400); noticeTimer.Start();
        }

        StackPanel BuildEqualizer()
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Height = 14, Visibility = Visibility.Collapsed };
            int[] ms = { 420, 300, 520, 360 };
            for (int i = 0; i < 4; i++)
            {
                var sc = new ScaleTransform(1, 0.4);
                var bar = new Border { Width = 3, Height = 14, Margin = new Thickness(1.5, 0, 0, 0), CornerRadius = new CornerRadius(1.5), Background = UI.Fg, RenderTransform = sc, RenderTransformOrigin = new Point(0.5, 1), VerticalAlignment = VerticalAlignment.Bottom };
                int len = ms[i], delay = i * 90;
                p.IsVisibleChanged += (s, e) => sc.BeginAnimation(ScaleTransform.ScaleYProperty, !p.IsVisible ? null : new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(len)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }, BeginTime = TimeSpan.FromMilliseconds(delay) });
                p.Children.Add(bar);
            }
            return p;
        }

        StackPanel mediaDots;

        FrameworkElement BuildMediaCard()
        {
            var card = new Border { Width = 262, Height = 64, CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(UI.Card), Margin = new Thickness(0, 0, 14, 0) };
            var g = new DockPanel { Margin = new Thickness(8) };
            mediaArt = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(11), ClipToBounds = true, Background = new SolidColorBrush(UI.Track), Child = UI.Glyph("", 18, UI.Gray), Cursor = Cursors.Hand };
            mediaArt.MouseLeftButtonUp += (s, e) => NextSource(1);
            DockPanel.SetDock(mediaArt, Dock.Left);
            var ctrls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(ctrls, Dock.Right);
            Func<string, byte, TextBlock> btn = (glyph, vk) =>
            {
                var t = UI.Glyph(glyph, 13, UI.Fg);
                var b = UI.Pressable(t, 30, 30, 15, () => MediaCommand(vk));
                ctrls.Children.Add(b);
                return t;
            };
            btn("", 0xB1); playGlyph = btn("", 0xB3); btn("", 0xB0);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) };
            mediaTitle = UI.Text("Ничего не играет", 13, UI.Fg, FontWeights.SemiBold);
            mediaArtist = UI.Text("", 11.5, UI.Gray);
            // one dot per sound source; click a dot (or the cover, or scroll) to switch
            mediaDots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
            texts.Children.Add(mediaTitle); texts.Children.Add(mediaArtist); texts.Children.Add(mediaDots);
            g.Children.Add(mediaArt); g.Children.Add(ctrls); g.Children.Add(texts);
            card.Child = g;
            card.MouseWheel += (s, e) => { if (sources.Count > 1) { NextSource(e.Delta < 0 ? 1 : -1); e.Handled = true; } };
            return card;
        }

        // ---------- now playing: every sound source, not just one ----------

        class MediaSource
        {
            public string Key, Title, Artist, App, Exe;
            public bool Playing;
            public ImageSource Art;
            public Windows.Media.Control.GlobalSystemMediaTransportControlsSession Session;
        }

        List<MediaSource> sources = new List<MediaSource>();
        string curKey, userKey, shownTitleKey;
        DateTime holdUntil = DateTime.MinValue;
        HashSet<string> wasPlaying = new HashSet<string>();
        string lastMediaSnap, lastMediaErr;

        static readonly string[] MediaApps = { "browser", "yandex", "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "arc",
            "vlc", "mpc-hc", "mpc-be", "potplayer", "foobar", "aimp", "winamp", "spotify", "music", "musicbee", "itunes", "wmplayer", "kmplayer" };
        static readonly string[] TitleTails = { " — Яндекс Браузер", " - Яндекс Браузер", " – Яндекс Браузер", " - Google Chrome", " — Google Chrome", " - Microsoft Edge", " — Mozilla Firefox", " - Mozilla Firefox", " - Opera", " - YouTube", " — YouTube" };

        void WatchMedia()
        {
            Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager mgr;
            try { mgr = Await(Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync()); Log.Write("media", "менеджер медиа получен"); }
            catch (Exception ex) { Log.Error("media", ex); return; }
            var artCache = new Dictionary<string, KeyValuePair<string, ImageSource>>();
            var heard = new Dictionary<string, MediaSource>();   // sound-only sources, kept a while after they go quiet
            var heardAt = new Dictionary<string, DateTime>();
            var infoAt = new Dictionary<string, DateTime>();     // window title and exe lookups are slow, redo them every few seconds
            bool first = true;
            while (true)
            {
                var list = new List<MediaSource>();
                var polledAt = DateTime.Now;
                try
                {
                    var snap = new System.Text.StringBuilder();
                    var seenApps = new Dictionary<string, int>();
                    foreach (var s in mgr.GetSessions())
                    {
                        // several tabs of one browser are separate sessions with the same app id: number them
                        int dup; seenApps.TryGetValue(s.SourceAppUserModelId, out dup); seenApps[s.SourceAppUserModelId] = dup + 1;
                        var src = new MediaSource { Session = s, Key = "w:" + s.SourceAppUserModelId + (dup > 0 ? "#" + dup : ""), App = s.SourceAppUserModelId };
                        try
                        {
                            src.Playing = s.GetPlaybackInfo().PlaybackStatus == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                            var props = Await(s.TryGetMediaPropertiesAsync());
                            src.Title = props.Title; src.Artist = props.Artist;
                            var tk = props.Title + "\u0001" + props.Artist;
                            KeyValuePair<string, ImageSource> cached;
                            if (artCache.TryGetValue(src.Key, out cached) && cached.Key == tk) src.Art = cached.Value;
                            else { src.Art = LoadThumb(props.Thumbnail); artCache[src.Key] = new KeyValuePair<string, ImageSource>(tk, src.Art); }
                        }
                        catch (Exception ex) { snap.Append("\n    ").Append(src.App).Append(" ошибка ").Append(ex.GetType().Name); continue; }
                        snap.Append("\n    ").Append(src.App).Append(src.Playing ? " играет" : " пауза").Append(" · ").Append(src.Title);
                        list.Add(src);
                    }

                    // apps that never tell Windows what they play (Yandex Browser, players): use who makes sound
                    var now = DateTime.Now;
                    foreach (var kv in AudioSessions.Active(n => MediaApps.Any(m => n.ToLowerInvariant().Contains(m))))
                    {
                        System.Diagnostics.Process p;
                        try { p = System.Diagnostics.Process.GetProcessById((int)kv.Key); } catch { continue; }
                        var name = p.ProcessName.ToLowerInvariant();
                        // already covered by a real session (Edge, Chrome, Spotify...)
                        if (list.Any(x => x.App.ToLowerInvariant().Contains(name) || name.Contains(x.App.ToLowerInvariant().Split('.')[0]))) continue;
                        DateTime seen;
                        if (heard.ContainsKey("s:" + name) && infoAt.TryGetValue("s:" + name, out seen) && (now - seen).TotalSeconds < 5) { heardAt["s:" + name] = now; continue; }
                        infoAt["s:" + name] = now;
                        var win = p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0 ? p
                            : System.Diagnostics.Process.GetProcessesByName(p.ProcessName).FirstOrDefault(x => x.MainWindowHandle != IntPtr.Zero && x.MainWindowTitle.Length > 0);
                        var title = win != null ? win.MainWindowTitle : p.ProcessName;
                        foreach (var tail in TitleTails) { int i = title.IndexOf(tail, StringComparison.OrdinalIgnoreCase); if (i > 0) title = title.Substring(0, i); }
                        string exe = null; try { exe = (win ?? p).MainModule.FileName; } catch { }
                        string app = p.ProcessName;
                        try { var d = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription; if (!string.IsNullOrEmpty(d)) app = d; } catch { }
                        heard["s:" + name] = new MediaSource { Key = "s:" + name, Title = title, Artist = app, App = app, Exe = exe };
                        heardAt["s:" + name] = now;
                    }
                    foreach (var k in heard.Keys.ToList())
                    {
                        var age = (now - heardAt[k]).TotalSeconds;
                        if (age > 600 || list.Any(x => x.App.ToLowerInvariant().Contains(k.Substring(2)))) { heard.Remove(k); heardAt.Remove(k); continue; }
                        heard[k].Playing = age < 3; // sound pauses between words
                        list.Add(heard[k]);
                        snap.Append("\n    звук: ").Append(heard[k].App).Append(age < 3 ? " играет" : " тихо").Append(" · ").Append(heard[k].Title);
                    }

                    var line = list.Count + " источник(ов)" + snap;
                    if (line != lastMediaSnap) { Log.Write("media", line); lastMediaSnap = line; }
                }
                catch (Exception ex)
                {
                    var msg = ex.GetType().Name + ": " + ex.Message;
                    if (msg != lastMediaErr) { Log.Write("media", "ОШИБКА " + msg); lastMediaErr = msg; }
                }
                var copy = list; bool f = first; var at = polledAt;
                Dispatcher.BeginInvoke(new Action(() => ShowSources(copy, f, at)));
                first = false;
                System.Threading.Thread.Sleep(700);
            }
        }

        static ImageSource LoadThumb(Windows.Storage.Streams.IRandomAccessStreamReference thumb)
        {
            if (thumb == null) return null;
            try
            {
                var stream = Await(thumb.OpenReadAsync());
                var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
                uint size = (uint)stream.Size;
                Await(reader.LoadAsync(size));
                var bytes = new byte[size]; reader.ReadBytes(bytes);
                var art = new BitmapImage();
                art.BeginInit(); art.CacheOption = BitmapCacheOption.OnLoad; art.DecodePixelWidth = 96; art.StreamSource = new MemoryStream(bytes); art.EndInit();
                art.Freeze();
                return art;
            }
            catch { return null; }
        }

        MediaSource Current { get { return sources.FirstOrDefault(x => x.Key == curKey); } }

        // A click asks a source to play or pause, but the source reports the new state with a delay,
        // and a poll that started before the click still carries the old one. So each source remembers
        // what it was told until a poll taken *after* the click confirms it (or 6 s pass).
        class Pend { public bool Want; public DateTime At; }
        readonly Dictionary<string, Pend> pends = new Dictionary<string, Pend>();

        void ShowSources(List<MediaSource> list, bool first, DateTime polledAt)
        {
            foreach (var src in list)
            {
                Pend p;
                if (!pends.TryGetValue(src.Key, out p)) continue;
                if ((polledAt > p.At.AddMilliseconds(200) && src.Playing == p.Want) || (DateTime.Now - p.At).TotalSeconds > 6) pends.Remove(src.Key);
                else src.Playing = p.Want;
            }
            sources = list;
            // only a source that has *just started* playing may take the card over;
            // pausing the shown one never makes the card jump to something that was already playing
            var started = list.FirstOrDefault(x => x.Playing && !wasPlaying.Contains(x.Key) && x.Key != curKey && !pends.ContainsKey(x.Key));
            wasPlaying = new HashSet<string>(list.Where(x => x.Playing).Select(x => x.Key));
            if (userKey != null && !list.Any(x => x.Key == userKey)) userKey = null;
            MediaSource cur;
            if (started != null && !first && DateTime.Now > holdUntil) { cur = started; userKey = null; }
            else if (userKey != null) cur = list.First(x => x.Key == userKey);
            else cur = list.FirstOrDefault(x => x.Key == curKey) ?? list.FirstOrDefault(x => x.Playing) ?? list.FirstOrDefault();
            curKey = cur == null ? null : cur.Key;
            RenderMedia(cur, !first);
        }

        void NextSource(int dir)
        {
            if (sources.Count < 2) return;
            int i = sources.FindIndex(x => x.Key == curKey);
            var next = sources[((i + dir) % sources.Count + sources.Count) % sources.Count];
            userKey = curKey = next.Key;
            holdUntil = DateTime.Now.AddSeconds(3); // the user is looking at this one
            RenderMedia(next, false);
        }

        void RenderMedia(MediaSource cur, bool announce)
        {
            mHas = cur != null;
            var titleKey = cur == null ? null : cur.Key + "\u0001" + cur.Title;
            bool changed = titleKey != shownTitleKey;
            mPlaying = cur != null && cur.Playing;
            playGlyph.Text = mPlaying ? "" : "";

            mediaDots.Children.Clear();
            mediaDots.Visibility = sources.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var s in sources)
            {
                var src = s;
                var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand,
                    Fill = new SolidColorBrush(src.Key == curKey ? UI.FgColor : UI.Dim), ToolTip = src.App + (src.Playing ? " · играет" : "") };
                dot.MouseLeftButtonUp += (a, e) => { userKey = curKey = src.Key; holdUntil = DateTime.Now.AddSeconds(3); RenderMedia(src, false); e.Handled = true; };
                mediaDots.Children.Add(dot);
            }

            if (!changed) return;
            shownTitleKey = titleKey;
            mTitle = cur == null ? null : cur.Title; mArtist = cur == null ? null : cur.Artist;
            mArt = cur == null ? null : cur.Art ?? (cur.Exe != null ? UI.FileIcon(cur.Exe) : null);
            mediaTitle.Text = !string.IsNullOrEmpty(mTitle) ? mTitle : L.T("Ничего не играет");
            mediaArtist.Text = mArtist ?? "";
            mediaArt.Child = mArt != null ? (UIElement)new Image { Source = mArt, Stretch = cur.Art != null ? Stretch.UniformToFill : Stretch.Uniform, Margin = new Thickness(cur.Art != null ? 0 : 8) } : UI.Glyph("", 18, UI.Gray);
            if (announce && cur != null && cur.Playing && !dnd && !string.IsNullOrEmpty(mTitle))
                Notify("", UI.Red, mTitle, string.IsNullOrEmpty(mArtist) ? "Сейчас играет" : mArtist, mArt, 3000);
        }

        // act on the source shown in the card: an explicit play or pause, never a blind toggle
        void MediaCommand(byte vk)
        {
            var cur = Current;
            var s = cur == null ? null : cur.Session;
            if (vk != 0xB3)
            {
                try { if (s == null) Native.Key(vk); else if (vk == 0xB0) s.TrySkipNextAsync(); else s.TrySkipPreviousAsync(); }
                catch { Native.Key(vk); }
                return;
            }
            if (cur == null) { Native.Key(vk); return; }
            bool want = !cur.Playing; // cur.Playing already includes any unconfirmed earlier click
            try
            {
                if (s == null) Native.Key(vk); // sound-only source: the browser/player listens to media keys
                else if (want) s.TryPlayAsync(); else s.TryPauseAsync();
            }
            catch { Native.Key(vk); }
            pends[cur.Key] = new Pend { Want = want, At = DateTime.Now };
            cur.Playing = want;
            userKey = cur.Key; // keep controlling the same source
            holdUntil = DateTime.Now.AddSeconds(3);
            RenderMedia(cur, false);
        }

        // ---------- ticks ----------

        void FastFeatures()
        {
            float v; bool m;
            bool ok = Volume.Get(out v, out m);
            if (ok)
            {
                if (Math.Abs(v - lastVol) > 0.004 || m != lastMute) ShowLevel(v, m);
                lastVol = v; lastMute = m;
            }

            // timer > music > battery on the right side of the closed island
            bool showEq = mPlaying && !timerOn;
            eq.Visibility = showEq ? Visibility.Visible : Visibility.Collapsed;
            ((FrameworkElement)miniRight.Parent).Visibility = showEq ? Visibility.Collapsed : Visibility.Visible;

            bool fs = IsFullscreen();
            if (fs != fullscreen)
            {
                fullscreen = fs;
                UI.Ease(pill, OpacityProperty, fs ? 0 : 1, 250);
                pill.IsHitTestVisible = !fs;
                if (fs && mode != Mode.Collapsed) SetMode(Mode.Collapsed);
            }
        }

        void SlowFeatures()
        {
            // follow the switch in Windows itself (Win+N, quick settings)
            var sys = QuietHours.IsOn();
            if (sys.HasValue && sys.Value != dnd)
            {
                dnd = sys.Value;
                dndGlyph.Foreground = dnd ? new SolidColorBrush(UI.Purple) : UI.Gray;
                moon.Visibility = dnd ? Visibility.Visible : Visibility.Collapsed;
                miniTime.Margin = new Thickness(dnd ? 16 : 0, 0, 0, 0);
            }

            var ps = WF.SystemInformation.PowerStatus;
            if ((ps.BatteryChargeStatus & WF.BatteryChargeStatus.NoSystemBattery) != 0 || ps.BatteryLifePercent > 1) return;
            int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
            bool ac = ps.PowerLineStatus == WF.PowerLineStatus.Online;
            if (ac) { lowBatteryWarned = 101; return; }
            foreach (var lvl in new[] { 20, 10, 5 })
                if (pct <= lvl && lowBatteryWarned > lvl)
                {
                    lowBatteryWarned = lvl;
                    Notify("", UI.Red, "Низкий заряд", "Осталось " + pct + "% - подключите зарядку", null, 4000);
                    Shake();
                    break;
                }
        }

        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
        struct RECT { public int L, T, R, B; }

        bool IsFullscreen()
        {
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == new WindowInteropHelper(this).Handle) return false;
            var sb = new System.Text.StringBuilder(64); GetClassName(fg, sb, 64);
            var cls = sb.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
            RECT r; if (!GetWindowRect(fg, out r)) return false;
            var scr = WF.Screen.PrimaryScreen.Bounds;
            bool covers = r.L <= scr.Left && r.T <= scr.Top && r.R >= scr.Right && r.B >= scr.Bottom;
            // Fullscreen (F11, video, games) sits exactly on the screen edges. A maximized window that
            // merely covers the screen hangs its invisible borders past them (Yandex Browser does that),
            // so only an exact fit counts. Chrome keeps the "maximized" flag in fullscreen, so that flag can't decide.
            bool exact = Math.Abs(r.L - scr.Left) <= 1 && Math.Abs(r.T - scr.Top) <= 1 && Math.Abs(r.R - scr.Right) <= 1 && Math.Abs(r.B - scr.Bottom) <= 1;
            bool fs = covers && (exact || !IsZoomed(fg));
            var key = cls + " " + r.L + "," + r.T + "," + r.R + "," + r.B + " max=" + IsZoomed(fg) + " → " + (fs ? "полный экран" : "обычное");
            if (covers && key != lastFsKey) { Log.Write("screen", cls + " [" + r.L + "," + r.T + " – " + r.R + "," + r.B + "], экран " + scr.Width + "x" + scr.Height + ", развёрнуто=" + IsZoomed(fg) + " → " + (fs ? "прячусь" : "остаюсь")); lastFsKey = key; }
            return fs;
        }

        string lastFsKey;

        // ---------- smart copy ----------

        static readonly Regex UrlRx = new Regex(@"^https?://\S+$", RegexOptions.IgnoreCase);
        static readonly Regex HexRx = new Regex(@"^#?([0-9a-f]{6})$", RegexOptions.IgnoreCase);
        static readonly Regex MailRx = new Regex(@"^[^@\s]+@[^@\s]+\.[a-z]{2,}$", RegexOptions.IgnoreCase);

        bool SmartCopy(string text)
        {
            var t = text.Trim();
            if (UrlRx.IsMatch(t))
            {
                string host = t; try { host = new Uri(t).Host; } catch { }
                Notify("", UI.Blue, "Ссылка скопирована", host);
                return true;
            }
            var hex = HexRx.Match(t);
            if (hex.Success)
            {
                int c = Convert.ToInt32(hex.Groups[1].Value, 16);
                var col = Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c);
                Notify("", col, "Цвет скопирован", "#" + hex.Groups[1].Value.ToUpper() + "   rgb(" + col.R + ", " + col.G + ", " + col.B + ")");
                noticeIcon.Background = new SolidColorBrush(col);
                return true;
            }
            if (MailRx.IsMatch(t))
            {
                Notify("", UI.Blue, "Почта скопирована", t);
                return true;
            }
            return false;
        }
    }
}
