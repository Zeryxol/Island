using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

// Interactive regression test. Uses only windows created by this test process.
// UI Automation must run off the WPF dispatcher, just as it does in Island.
class SelectionTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    static int exitCode = 1;
    static Assembly assembly;
    static Application app;
    static Type islandType;
    static Window island;
    static TextBox source;
    static Window fixture;
    static bool autoMode;
    static bool both;
    static string firstReport = "";
    static string report;

    static void Finish(string message, bool success)
    {
        System.IO.File.WriteAllText(report, message);
        exitCode = success ? 0 : 1; app.Shutdown();
    }

    [STAThread] static int Main(string[] args)
    {
        report = args[1]; autoMode = args.Length > 2 && args[2] == "auto";
        both = args.Length > 2 && args[2] == "both";
        assembly = Assembly.LoadFrom(args[0]);
        var settings = assembly.GetType("Island.Settings");
        var values = (Dictionary<string,string>)settings.GetField("values", Static).GetValue(null);
        values["updates"] = "0"; values["weather"] = "0"; values["translator"] = "1"; values["translateAuto"] = "1";
        app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (s,e) => { e.Handled = true; Finish(e.Exception.ToString(), false); };
        islandType = assembly.GetType("Island.IslandWindow");
        island = (Window)Activator.CreateInstance(islandType, true);
        island.Show();
        var layout = new StackPanel();
        source = new TextBox { Text = "Hello, world!", FontSize = 22, Margin = new Thickness(12), Height = 48 };
        var focus = new Button { Content = "Non-text focus, like a link inside a browser document", Height = 32 };
        var password = new PasswordBox { Password = "not-for-translation", Height = 28 };
        layout.Children.Add(source); layout.Children.Add(focus); layout.Children.Add(password);
        fixture = new Window { Title = "Island selection regression", Left = 180, Top = 420, Width = 540, Height = 200, Content = layout };
        fixture.Loaded += (s,e) =>
        {
            fixture.Activate(); source.SelectAll(); focus.Focus();
            var h = new WindowInteropHelper(fixture).Handle;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(300); // let the test window publish its automation provider
                string selected = null; Exception error = null;
                try { selected = (string)assembly.GetType("Island.SelectionReader").GetMethod("Read", Static).Invoke(null, new object[] { h, null }); }
                catch (Exception ex) { error = ex; }
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (error != null || selected != "Hello, world!") { Finish("FAIL non-text focus: " + (error == null ? selected : error.ToString()), false); return; }
                    System.IO.File.WriteAllText(report, "PASS selection with non-text focused element\n");
                    islandType.GetField("fullscreen", Instance).SetValue(island, true);
                    var pill = (FrameworkElement)islandType.GetField("pill", Instance).GetValue(island);
                    pill.Opacity = 0; pill.IsHitTestVisible = false;
                    // Exercise the real application's message/auto-selection path.
                    if (autoMode)
                    {
                        Point p = source.PointToScreen(new Point(40, 20));
                        var mouseType = islandType.GetNestedType("MSLL", BindingFlags.NonPublic);
                        var mouse = Activator.CreateInstance(mouseType);
                        mouseType.GetField("X").SetValue(mouse, (int)p.X); mouseType.GetField("Y").SetValue(mouse, (int)p.Y);
                        IntPtr data = Marshal.AllocHGlobal(Marshal.SizeOf(mouse));
                        try
                        {
                            Marshal.StructureToPtr(mouse, data, false);
                            var hook = (Delegate)islandType.GetField("wheelProc", Instance).GetValue(island);
                            hook.DynamicInvoke(0, new IntPtr(0x0202), data);
                        }
                        finally { Marshal.FreeHGlobal(data); }
                    }
                    else PostMessage(new WindowInteropHelper(island).Handle, 0x0312, new IntPtr(0x1A03), IntPtr.Zero);
                    var deadline = DateTime.UtcNow.AddSeconds(25);
                    var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                    check.Tick += (a,b) =>
                    {
                        var host = islandType.GetField("settingsHost", Instance).GetValue(island) as Grid;
                        if (host != null)
                        {
                            string text = AllText(host);
                            if (text.Contains("Google Translate") && text.Contains("мир") && host.IsVisible && pill.Opacity > 0.99 && pill.IsHitTestVisible)
                            {
                                check.Stop();
                                var copyLabel = FindText(host, "Скопировать");
                                var button = copyLabel == null ? null : System.Windows.Media.VisualTreeHelper.GetParent(copyLabel) as Border;
                                if (button == null) { Finish("FAIL copy button missing", false); return; }
                                Point center = copyLabel.TranslatePoint(new Point(copyLabel.ActualWidth / 2, copyLabel.ActualHeight / 2), button);
                                if (Math.Abs(center.X - button.ActualWidth / 2) > 1 || Math.Abs(center.Y - button.ActualHeight / 2) > 1)
                                { Finish("FAIL copy label is not centered", false); return; }
                                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth), (int)Math.Ceiling(island.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                                bitmap.Render(island);
                                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                                using (var stream = System.IO.File.Create(report + ".png")) encoder.Save(stream);
                                string passed = "PASS selection with non-text focused element\nPASS centered copy button\nPASS " + (autoMode ? "mouse selection" : "WM_HOTKEY") + " to visible translated card: " + text;
                                if (both && !autoMode)
                                {
                                    firstReport = passed; autoMode = true;
                                    islandType.GetMethod("HideSettings", Instance).Invoke(island, null);
                                    Point p = source.PointToScreen(new Point(40, 20));
                                    islandType.GetMethod("SelectionMouseUp", Instance).Invoke(island, new object[] { (int)p.X, (int)p.Y });
                                    check.Start(); return;
                                }
                                Finish(firstReport + passed, true);
                                return;
                            }
                        }
                        if (DateTime.UtcNow > deadline) { check.Stop(); Finish("FAIL translated card timed out\nforeground=" + GetForegroundWindow() + " fixture=" + h + "\n" + (host == null ? "no host" : "visible=" + host.IsVisible + "\n" + AllText(host)) + "\nselectionBusy=" + islandType.GetField("selectionBusy", Instance).GetValue(island) + " request=" + islandType.GetField("translationRequest", Instance).GetValue(island), false); }
                    };
                    check.Start();
                }));
            });
        };
        var globalTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(35) };
        globalTimeout.Tick += (s,e) => Finish("FAIL UI Automation timed out", false); globalTimeout.Start();
        fixture.Show(); app.Run(); return exitCode;
    }

    static string AllText(DependencyObject root)
    {
        var text = root as TextBlock; string result = text == null ? "" : text.Text + "\n";
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            result += AllText(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
        return result;
    }

    static TextBlock FindText(DependencyObject root, string value)
    {
        var text = root as TextBlock;
        if (text != null && text.Text == value) return text;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindText(System.Windows.Media.VisualTreeHelper.GetChild(root, i), value);
            if (found != null) return found;
        }
        return null;
    }
}
