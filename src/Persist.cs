using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island
{
    // clipboard history that survives restarts, plus whatever Windows already remembers (Win+V)
    partial class IslandWindow
    {
        static readonly string ClipsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "clips");
        static readonly string ClipsIndex = Path.Combine(ClipsDir, "index.txt");

        static ImageSource Thumb(string file)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 160; bi.UriSource = new Uri(file); bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        static BitmapSource LoadFull(string file)
        {
            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(file); bi.EndInit();
            bi.Freeze();
            return bi;
        }

        ClipItem SaveClipImage(BitmapSource img)
        {
            try
            {
                Directory.CreateDirectory(ClipsDir);
                var file = Path.Combine(ClipsDir, DateTime.Now.Ticks + ".png");
                using (var fs = File.Create(file))
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(img));
                    enc.Save(fs);
                }
                return new ClipItem { ImagePath = file, Image = Thumb(file) };
            }
            catch { return null; }
        }

        static void DeleteClipFile(ClipItem c)
        {
            try { if (c.ImagePath != null && File.Exists(c.ImagePath)) File.Delete(c.ImagePath); } catch { }
        }

        void SaveClips()
        {
            try
            {
                Directory.CreateDirectory(ClipsDir);
                var lines = clips.Select(c => c.ImagePath != null
                    ? "I\t" + Path.GetFileName(c.ImagePath)
                    : "T\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(c.Text)));
                File.WriteAllLines(ClipsIndex, lines);
            }
            catch { }
        }

        void LoadClips()
        {
            try
            {
                if (File.Exists(ClipsIndex))
                    foreach (var line in File.ReadAllLines(ClipsIndex))
                    {
                        var p = line.Split('\t');
                        if (p.Length != 2) continue;
                        if (p[0] == "T") clips.Add(new ClipItem { Text = Encoding.UTF8.GetString(Convert.FromBase64String(p[1])) });
                        else if (p[0] == "I")
                        {
                            var f = Path.Combine(ClipsDir, p[1]);
                            if (File.Exists(f)) clips.Add(new ClipItem { ImagePath = f, Image = Thumb(f) });
                        }
                    }
            }
            catch { }
            RenderClips();
            System.Threading.ThreadPool.QueueUserWorkItem(_ => ImportWindowsHistory());
        }

        // texts from the Windows clipboard history (Win+V) that we haven't seen yet
        void ImportWindowsHistory()
        {
            var texts = new System.Collections.Generic.List<string>();
            try
            {
                var res = Await(Windows.ApplicationModel.DataTransfer.Clipboard.GetHistoryItemsAsync());
                if (res.Status == Windows.ApplicationModel.DataTransfer.ClipboardHistoryItemsResultStatus.Success)
                    foreach (var item in res.Items.Take(25))
                        if (item.Content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                        {
                            var t = Await(item.Content.GetTextAsync());
                            if (!string.IsNullOrWhiteSpace(t)) texts.Add(t);
                        }
            }
            catch { }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // at least what is on the clipboard right now
                if (texts.Count == 0) try { if (System.Windows.Clipboard.ContainsText()) texts.Add(System.Windows.Clipboard.GetText()); } catch { }
                bool changed = false;
                // history comes newest first; add oldest first so the order stays right
                foreach (var t in Enumerable.Reverse(texts))
                    if (!clips.Any(c => c.Text == t)) { clips.Insert(0, new ClipItem { Text = t }); changed = true; }
                if (texts.Count > 0)
                {
                    // the newest Windows entry should be first even if we already knew it
                    var top = clips.FirstOrDefault(c => c.Text == texts[0]);
                    if (top != null && clips.IndexOf(top) != 0) { clips.Remove(top); clips.Insert(0, top); changed = true; }
                }
                while (clips.Count > 30) { DeleteClipFile(clips[clips.Count - 1]); clips.RemoveAt(clips.Count - 1); }
                if (changed) { RenderClips(); SaveClips(); }
            }));
        }
    }
}
