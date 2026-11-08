using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Island
{
    // One background sampler. No WMI or performance counter work on the UI thread.
    sealed class SystemMonitor : IDisposable
    {
        public sealed class Sample
        {
            public double? Gpu, Temperature;
            public string TemperatureSource = "", Adapter = "";
            public double Download, Upload;
        }
        public volatile Sample Latest = new Sample();
        readonly Dictionary<string, long[]> previous = new Dictionary<string, long[]>();
        DateTime lastNetwork = DateTime.UtcNow, lastSensors = DateTime.MinValue;
        IntPtr query, gpuCounter;
        bool triedGpu;
        double? temperature;
        string temperatureSource = "";

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQuery(string source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")] static extern uint AddCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr query);
        [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")] static extern uint CounterArray(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr buffer);
        [StructLayout(LayoutKind.Explicit, Size = 16)] struct CounterValue
        {
            [FieldOffset(0)] public uint Status;
            [FieldOffset(8)] public double Value;
        }
        [StructLayout(LayoutKind.Sequential)] struct CounterItem { public IntPtr Name; public CounterValue Value; }

        double? ReadGpu()
        {
            if (!triedGpu)
            {
                triedGpu = true;
                if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) return null;
                if (AddCounter(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out gpuCounter) != 0) return null;
                PdhCollectQueryData(query); return null; // rate counters need two samples
            }
            if (gpuCounter == IntPtr.Zero || PdhCollectQueryData(query) != 0) return null;
            uint bytes = 0, count;
            CounterArray(gpuCounter, 0x8200, ref bytes, out count, IntPtr.Zero);
            if (bytes == 0 || bytes > 32 * 1024 * 1024) return null;
            var buffer = Marshal.AllocHGlobal((int)bytes);
            try
            {
                if (CounterArray(gpuCounter, 0x8200, ref bytes, out count, buffer) != 0) return null;
                var engines = new Dictionary<string, double>();
                int size = Marshal.SizeOf(typeof(CounterItem));
                for (int i = 0; i < count; i++)
                {
                    var item = (CounterItem)Marshal.PtrToStructure(IntPtr.Add(buffer, i * size), typeof(CounterItem));
                    if (item.Value.Status > 1 || double.IsNaN(item.Value.Value) || double.IsInfinity(item.Value.Value)) continue;
                    var name = Marshal.PtrToStringUni(item.Name) ?? "";
                    int start = name.IndexOf("luid_", StringComparison.Ordinal);
                    if (start < 0) continue;
                    // Sum processes on each physical engine, then show the busiest engine.
                    string engine = name.Substring(start); double current;
                    engines.TryGetValue(engine, out current); engines[engine] = current + Math.Max(0, item.Value.Value);
                }
                return engines.Count == 0 ? (double?)null : Math.Min(100, engines.Values.Max());
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        void ReadTemperature()
        {
            temperature = null; temperatureSource = "";
            // Use actual CPU/GPU sensor providers, never an ACPI zone mislabeled as CPU.
            foreach (string ns in new[] { @"root\LibreHardwareMonitor", @"root\OpenHardwareMonitor" })
            {
                try
                {
                    var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = false };
                    using (var search = new ManagementObjectSearcher(new ManagementScope(ns), new ObjectQuery("SELECT Name, Value, Parent FROM Sensor WHERE SensorType='Temperature'"), options))
                    using (var results = search.Get())
                    {
                        double best = -1; string label = "";
                        foreach (ManagementObject sensor in results)
                        using (sensor)
                        {
                            var name = Convert.ToString(sensor["Name"]);
                            var parent = Convert.ToString(sensor["Parent"]);
                            if (parent.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) < 0 && parent.IndexOf("gpu", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            double value = Convert.ToDouble(sensor["Value"], CultureInfo.InvariantCulture);
                            if (value > 0 && value < 150 && value > best) { best = value; label = name; }
                        }
                        if (best > 0) { temperature = best; temperatureSource = label + " · " + ns.Substring(5); return; }
                    }
                }
                catch (ManagementException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.Runtime.InteropServices.COMException) { }
            }
            try
            {
                // NVIDIA exposes its sensor without installing a monitoring driver.
                if (Nvml.nvmlInit_v2() == 0)
                {
                    try
                    {
                        uint count;
                        if (Nvml.nvmlDeviceGetCount_v2(out count) == 0)
                            for (uint i = 0; i < count; i++)
                            {
                                IntPtr device; uint value;
                                if (Nvml.nvmlDeviceGetHandleByIndex_v2(i, out device) == 0 && Nvml.nvmlDeviceGetTemperature(device, 0, out value) == 0 && value > 0 && value < 150)
                                    if (!temperature.HasValue || value > temperature.Value) { temperature = value; temperatureSource = "NVIDIA GPU"; }
                            }
                    }
                    finally { Nvml.nvmlShutdown(); }
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (BadImageFormatException) { }
        }

        static class Nvml
        {
            [DllImport("nvml.dll")] internal static extern int nvmlInit_v2();
            [DllImport("nvml.dll")] internal static extern int nvmlShutdown();
            [DllImport("nvml.dll")] internal static extern int nvmlDeviceGetCount_v2(out uint count);
            [DllImport("nvml.dll")] internal static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
            [DllImport("nvml.dll")] internal static extern int nvmlDeviceGetTemperature(IntPtr device, uint sensor, out uint value);
        }

        public void Poll(bool sensors)
        {
            var sample = new Sample();
            try { sample.Gpu = sensors ? ReadGpu() : null; } catch (Exception e) { Log.Write("gpu", e.Message); }
            if (sensors && (DateTime.UtcNow - lastSensors).TotalSeconds >= 15)
            {
                lastSensors = DateTime.UtcNow;
                try { ReadTemperature(); } catch (Exception e) { Log.Write("temperature", e.Message); }
            }
            sample.Temperature = temperature; sample.TemperatureSource = temperatureSource;
            var now = DateTime.UtcNow;
            double elapsed = (now - lastNetwork).TotalSeconds;
            var alive = new HashSet<string>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                try
                {
                    var stats = adapter.GetIPStatistics(); long[] old;
                    alive.Add(adapter.Id);
                    if (elapsed > 0 && previous.TryGetValue(adapter.Id, out old))
                    {
                        double down = Math.Max(0, stats.BytesReceived - old[0]) / elapsed;
                        double up = Math.Max(0, stats.BytesSent - old[1]) / elapsed;
                        // The busiest adapter avoids counting a VPN packet twice.
                        if (down + up >= sample.Download + sample.Upload)
                        { sample.Download = down; sample.Upload = up; sample.Adapter = adapter.Name; }
                    }
                    previous[adapter.Id] = new[] { stats.BytesReceived, stats.BytesSent };
                }
                catch (NetworkInformationException) { }
            }
            foreach (var id in previous.Keys.Where(k => !alive.Contains(k)).ToArray()) previous.Remove(id);
            lastNetwork = now; Latest = sample;
        }
        public void Dispose() { if (query != IntPtr.Zero) PdhCloseQuery(query); query = IntPtr.Zero; gpuCounter = IntPtr.Zero; }
    }

    partial class IslandWindow
    {
        double tabWidth;
        Gauge gGpu, gTemp, gNet;
        readonly Dictionary<string, TextBlock> monitorValues = new Dictionary<string, TextBlock>();
        readonly SystemMonitor monitor = new SystemMonitor();
        bool monitorBusy;
        FrameworkElement monitorPage;
        DateTime lastMonitorPoll = DateTime.MinValue;

        FrameworkElement BuildMonitorPage()
        {
            var list = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var id in Settings.Shown("monitorItems"))
            {
                var content = new StackPanel();
                content.Children.Add(UI.Text(Settings.Name(id), 10, UI.Gray));
                var value = UI.Text("—", 14, UI.Fg, FontWeights.SemiBold); value.TextTrimming = TextTrimming.CharacterEllipsis;
                monitorValues[id] = value; content.Children.Add(value);
                list.Children.Add(new Border { Width = id == "net" ? 324 : 158, Height = 42, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(12, 3, 8, 3), CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(UI.Card), Child = content });
            }
            if (list.Children.Count == 0) list.Children.Add(UI.Text("Выберите показатели в настройках", 13, UI.Gray));
            return monitorPage = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        }

        static string Rate(double bytes)
        {
            return bytes >= 1024 * 1024 ? (bytes / (1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB/s" : (bytes / 1024).ToString("0", CultureInfo.InvariantCulture) + " KB/s";
        }

        void MonitorText(string id, string value, string hint)
        {
            TextBlock t;
            if (monitorValues.TryGetValue(id, out t)) { t.Text = value; t.ToolTip = hint; }
        }

        void UpdateMonitor(double cpu, uint ram, ulong used, ulong total)
        {
            bool enabled = Settings.Get("monitoring") == "1";
            gCpu.Visibility = gRam.Visibility = gGpu.Visibility = gTemp.Visibility = gNet.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            if (!enabled)
            {
                foreach (var t in monitorValues.Values) { t.Text = "—"; t.ToolTip = L.T("Мониторинг выключен в настройках"); }
                return;
            }
            if (mode != Mode.Expanded) return;
            var s = monitor.Latest;
            string tempHint = s.Temperature.HasValue ? s.TemperatureSource : L.T("Датчик недоступен. Поддерживаются NVIDIA GPU и датчики запущенного LibreHardwareMonitor / OpenHardwareMonitor.");
            string gpu = s.Gpu.HasValue ? Math.Round(s.Gpu.Value) + "%" : "—";
            string temp = s.Temperature.HasValue ? Math.Round(s.Temperature.Value) + "°" : "—";
            MonitorText("cpu", Math.Round(cpu * 100) + "%", "CPU");
            MonitorText("ram", ram + "%", (used / 1073741824.0).ToString("0.0") + " / " + (total / 1073741824.0).ToString("0.0") + " GB");
            MonitorText("gpu", gpu, L.T("Самый загруженный движок GPU · Windows WDDM"));
            MonitorText("temp", temp, tempHint);
            MonitorText("net", "↓ " + Rate(s.Download) + "   ↑ " + Rate(s.Upload), s.Adapter);
            gGpu.Set((s.Gpu ?? 0) / 100, gpu); gTemp.Set((s.Temperature ?? 0) / 100, temp);
            gTemp.ToolTip = tempHint;
            gNet.Set(Math.Min(1, s.Download / (12.5 * 1024 * 1024)), (s.Download / (1024 * 1024)).ToString("0.0") );
            gNet.ToolTip = "↓ " + Rate(s.Download) + "   ↑ " + Rate(s.Upload) + " · " + s.Adapter;
            bool needsExtra = !SettingsOpen && ((monitorPage != null && monitorPage.IsVisible) || Settings.Shown("gauges").Any(id => id == "gpu" || id == "temp" || id == "net"));
            if (monitorBusy || !needsExtra || (DateTime.UtcNow - lastMonitorPoll).TotalSeconds < 2) return;
            lastMonitorPoll = DateTime.UtcNow;
            monitorBusy = true;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { monitor.Poll(true); } catch (Exception e) { Log.Write("monitor", e.Message); }
                finally { Dispatcher.BeginInvoke(new Action(() => monitorBusy = false)); }
            });
        }
    }
}
