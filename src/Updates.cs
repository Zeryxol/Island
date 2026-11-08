using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;

namespace Island
{
    static class Updates
    {
        public static string Current { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); } }
        public static string Status = L.T("Обновления ещё не проверялись");
        public static Action Ready;
        static int busy;
        static string staged, digest;
        static Version pendingVersion;

        static void OnUI(Action action)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
                Application.Current.Dispatcher.BeginInvoke(action);
        }

        public static void Check(bool manual, Action<string> changed)
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) { if (changed != null) changed(L.T("Проверка уже идёт…")); return; }
            Status = L.T("Проверяем обновления…"); if (changed != null) changed(Status);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ready = false;
                try
                {
                    var json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Online.Get("https://api.github.com/repos/Zeryxol/Island/releases/latest"));
                    Version version;
                    if (!Version.TryParse(Convert.ToString(json["tag_name"]).TrimStart('v'), out version)) throw new InvalidDataException("Invalid release version");
                    version = new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
                    if (version <= Assembly.GetExecutingAssembly().GetName().Version)
                    { Status = L.T("Установлена последняя версия"); }
                    else if (pendingVersion != null && pendingVersion == version && File.Exists(staged))
                    { Status = L.T("Обновление готово") + " · " + version; ready = manual; }
                    else
                    {
                        Dictionary<string, object> asset = null;
                        foreach (var raw in (IEnumerable)json["assets"])
                        {
                            var item = raw as Dictionary<string, object>;
                            if (item != null && Convert.ToString(item["name"]) == "Island.exe") { asset = item; break; }
                        }
                        if (asset == null) throw new InvalidDataException("Release has no Island.exe");
                        string url = Convert.ToString(asset["browser_download_url"]);
                        var uri = new Uri(url);
                        if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/Zeryxol/Island/releases/download/", StringComparison.Ordinal))
                            throw new InvalidDataException("Unexpected download origin");
                        object rawDigest;
                        if (!asset.TryGetValue("digest", out rawDigest) || !System.Text.RegularExpressions.Regex.IsMatch(Convert.ToString(rawDigest), "^sha256:[0-9a-fA-F]{64}$"))
                            throw new InvalidDataException("Release has no SHA-256 digest");
                        long size = Convert.ToInt64(asset["size"]);
                        if (size < 1024 || size > 64 * 1024 * 1024) throw new InvalidDataException("Invalid executable size");
                        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Island", "updates", Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);
                        string file = Path.Combine(dir, "Island.exe");
                        try
                        {
                            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                            request.UserAgent = "Island/" + Current; request.Timeout = 15000; request.ReadWriteTimeout = 15000;
                            using (var response = request.GetResponse())
                            using (var input = response.GetResponseStream())
                            using (var output = File.Create(file))
                            {
                                var buffer = new byte[65536]; int read; long written = 0;
                                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    written += read; if (written > size) throw new InvalidDataException("Download exceeds release size");
                                    output.Write(buffer, 0, read);
                                }
                                if (written != size) throw new InvalidDataException("Incomplete download");
                            }
                            string expected = Convert.ToString(rawDigest).Substring(7);
                            if (!Hash(file).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 mismatch");
                            if (AssemblyName.GetAssemblyName(file).Version != version) throw new InvalidDataException("Executable version differs from release tag");
                            if (staged != null) try { File.Delete(staged); } catch { }
                            staged = file; digest = expected; pendingVersion = version;
                            Status = L.T("Обновление готово") + " · " + version; ready = true;
                        }
                        catch { try { File.Delete(file); Directory.Delete(dir); } catch { } throw; }
                    }
                }
                catch (Exception e)
                {
                    Log.Write("update", e.GetType().Name + ": " + e.Message);
                    Status = L.T("Не удалось проверить обновления. Попробуйте позже.");
                }
                finally { Interlocked.Exchange(ref busy, 0); }
                OnUI(() => { if (changed != null) changed(Status); if (ready && Ready != null) Ready(); });
            });
        }

        internal static string Hash(string file)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static string Literal(string s) { return "'" + s.Replace("'", "''") + "'"; }

        public static void Install()
        {
            if (staged == null || !File.Exists(staged)) { Status = L.T("Сначала проверьте обновления"); return; }
            try
            {
                if (!Hash(staged).Equals(digest, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Staged executable changed");
                string target = Process.GetCurrentProcess().MainModule.FileName;
                string script = Path.Combine(Path.GetDirectoryName(staged), "install.ps1");
                // The helper waits until WPF releases the executable. A backup is restored on failure.
                string body = @"param()
$ErrorActionPreference = 'Stop'
$source = SOURCE
$target = TARGET
$backup = $target + '.previous'
$expected = DIGEST
try {
    Wait-Process -Id PROCESSID -Timeout 30 -ErrorAction SilentlyContinue
    if (Get-Process -Id PROCESSID -ErrorAction SilentlyContinue) { throw 'Island is still running' }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($source)
    try { $actual = [System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
    if ($actual -ne $expected) { throw 'SHA-256 mismatch' }
    $moved = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try { Move-Item -LiteralPath $target -Destination $backup -Force; $moved = $true; break }
        catch { Start-Sleep -Milliseconds 300 }
    }
    if (-not $moved) { throw 'Cannot replace Island.exe' }
    try {
        Move-Item -LiteralPath $source -Destination $target
        Start-Process -FilePath $target -ArgumentList '--restart' -WorkingDirectory ([System.IO.Path]::GetDirectoryName($target)) -WindowStyle Hidden
    } catch {
        Move-Item -LiteralPath $backup -Destination $target -Force
        Start-Process -FilePath $target -ArgumentList '--restart' -WindowStyle Hidden
        throw
    }
    Remove-Item -LiteralPath $PSCommandPath -Force
} catch {
    $_ | Out-String | Set-Content -LiteralPath ($source + '.error.txt')
}
";
                body = body.Replace("SOURCE", Literal(staged)).Replace("TARGET", Literal(target)).Replace("DIGEST", Literal(digest)).Replace("PROCESSID", Process.GetCurrentProcess().Id.ToString());
                File.WriteAllText(script, body, new UTF8Encoding(true));
                string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                var info = new ProcessStartInfo(shell, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                Process.Start(info); Application.Current.Shutdown();
            }
            catch (Exception e) { Log.Write("update-install", e.Message); Status = L.T("Не удалось установить обновление. Проверьте доступ к папке приложения."); }
        }
    }
}
