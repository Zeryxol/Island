using System;
using System.IO;

namespace Island
{
    // plain text log in %APPDATA%\Island\log.txt, kept under ~1 MB
    static class Log
    {
        public static readonly string File = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "log.txt");
        static readonly object Gate = new object();

        public static void Write(string area, string msg)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(File));
                    var fi = new FileInfo(File);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        var old = File + ".old";
                        if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
                        System.IO.File.Move(File, old);
                    }
                    System.IO.File.AppendAllText(File, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  [" + area + "] " + msg + Environment.NewLine);
                }
            }
            catch { }
        }

        public static void Error(string area, Exception e) { Write(area, "ОШИБКА " + e.GetType().Name + ": " + e.Message); }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SHParseDisplayName(string name, IntPtr ctx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr[] items, uint flags);

        // open the folder in Explorer with the file highlighted ("explorer /select" chokes on some paths)
        public static void Reveal(string path)
        {
            IntPtr pidl; uint o;
            if (System.IO.File.Exists(path) && SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out o) == 0)
            {
                int hr = SHOpenFolderAndSelectItems(pidl, 0, null, 0);
                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pidl);
                if (hr == 0) return;
                Write("start", "Проводник не открыл папку (0x" + hr.ToString("X") + "), открываю файл в Блокноте");
                // Explorer refused; at least show the file itself
                System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");
                return;
            }
            var dir = Path.GetDirectoryName(path);
            if (Directory.Exists(dir)) System.Diagnostics.Process.Start(dir);
        }
    }
}
