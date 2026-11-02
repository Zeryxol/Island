using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Island
{
    // hide/show (Ctrl+Alt+I or the tray) and dragging the island around the screen
    partial class IslandWindow
    {
        public static Action<bool> VisibilityChanged;
        static readonly string PosFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "position.txt");

        bool hidden;
        Point dragStart; bool dragArmed;

        void InitPlacement()
        {
            RestorePosition();

            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                // Ctrl+Alt+I, no auto-repeat. If another program already owns it we simply don't take it.
                if (!RegisterHotKey(h, 0x1A02, 0x0002 | 0x0001 | 0x4000, 0x49))
                    Log.Write("start", "Ctrl+Alt+I уже занято другой программой - скрывать можно из трея");
                HwndSource.FromHwnd(h).AddHook((IntPtr hw, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                {
                    if (msg == 0x0312 && w.ToInt32() == 0x1A02) { ToggleHidden(); handled = true; }
                    return IntPtr.Zero;
                });
            };

            // manual drag: the island never takes focus, so Window.DragMove is unreliable here
            pill.PreviewMouseLeftButtonDown += (s, e) =>
            {
                dragArmed = CanDragFrom(e.OriginalSource as DependencyObject);
                dragStart = ScreenPos(e);
                winStart = new Point(Left, Top);
            };
            pill.PreviewMouseMove += (s, e) =>
            {
                if (!dragArmed && !dragging) return;
                if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
                var p = ScreenPos(e);
                double dx = p.X - dragStart.X, dy = p.Y - dragStart.Y;
                if (!dragging)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) < 6) return;
                    dragging = true;
                    pill.CaptureMouse();
                    if (mode == Mode.Expanded) SetMode(Mode.Collapsed);
                }
                Left = winStart.X + dx; Top = winStart.Y + dy;
                e.Handled = true;
            };
            pill.PreviewMouseLeftButtonUp += (s, e) => { if (dragging) { EndDrag(); e.Handled = true; } dragArmed = false; };
            pill.LostMouseCapture += (s, e) => { if (dragging) EndDrag(); };
        }

        bool dragging; Point winStart;

        // in WPF units (DIPs), relative to the screen
        Point ScreenPos(MouseEventArgs e)
        {
            var p = PointToScreen(e.GetPosition(this));
            var src = PresentationSource.FromVisual(this);
            return src != null ? src.CompositionTarget.TransformFromDevice.Transform(p) : p;
        }

        void EndDrag()
        {
            if (!dragging) return;
            dragging = false; dragArmed = false;
            if (pill.IsMouseCaptured) pill.ReleaseMouseCapture();
            SnapAndSave();
        }

        // drag by the pill itself or the header of the open panel, never by cards, buttons or text fields
        bool CanDragFrom(DependencyObject d)
        {
            if (typing || mode == Mode.Search || mode == Mode.Pick || mode == Mode.Call) return false;
            for (var x = d; x != null && x != pill; x = VisualTreeHelper.GetParent(x) ?? LogicalTreeHelper.GetParent(x))
            {
                if (x == pageHost || x is TextBox || x is ButtonBase || x is ScrollViewer) return false;
                var b = x as Border;
                if (b != null && b.Cursor == Cursors.Hand) return false; // our pressable buttons and cards
                var fe = x as FrameworkElement;
                if (fe != null && fe.Cursor == Cursors.Hand) return false;
            }
            return true;
        }

        double HomeLeft { get { return (SystemParameters.PrimaryScreenWidth - Width) / 2; } }

        void SnapAndSave()
        {
            // close to the top centre: click back into the notch position
            if (Math.Abs(Left - HomeLeft) < 60 && Top < 40) { Left = HomeLeft; Top = 0; }
            var wa = SystemParameters.VirtualScreenWidth;
            Left = Math.Max(SystemParameters.VirtualScreenLeft - Width / 2, Math.Min(Left, SystemParameters.VirtualScreenLeft + wa - Width / 2));
            Top = Math.Max(SystemParameters.VirtualScreenTop, Top);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PosFile));
                File.WriteAllText(PosFile, ((int)Left) + "," + ((int)Top));
            }
            catch { }
        }

        void RestorePosition()
        {
            try
            {
                if (!File.Exists(PosFile)) return;
                var p = File.ReadAllText(PosFile).Split(',');
                double x = double.Parse(p[0]), y = double.Parse(p[1]);
                // only if that spot is still on a screen
                if (x + Width / 2 > SystemParameters.VirtualScreenLeft && x + Width / 2 < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                    && y >= SystemParameters.VirtualScreenTop && y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60)
                { Left = x; Top = y; }
            }
            catch { }
        }

        public void ResetPosition()
        {
            Left = HomeLeft; Top = 0;
            try { File.Delete(PosFile); } catch { }
            if (hidden) ToggleHidden();
        }

        public void ToggleHidden()
        {
            hidden = !hidden;
            if (hidden)
            {
                SetMode(Mode.Collapsed);
                UI.Ease(pill, OpacityProperty, 0, 180);
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(190) };
                t.Tick += (s, e) => { t.Stop(); if (hidden) Hide(); };
                t.Start();
            }
            else
            {
                Show();
                Topmost = true;
                UI.Ease(pill, OpacityProperty, 1, 220);
            }
            if (VisibilityChanged != null) VisibilityChanged(!hidden);
        }
    }
}
