using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;

namespace Island
{
    static class SelectionReader
    {
        static string Selected(AutomationElement element)
        {
            try { return SelectedCore(element); }
            catch (System.Runtime.InteropServices.COMException) { return null; }
            catch (ElementNotAvailableException) { return null; }
        }

        static string SelectedCore(AutomationElement element)
        {
            if (element == null || element.Current.IsPassword) return null;
            object pattern;
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) return null;
            var parts = new List<string>(); int remaining = 4001;
            foreach (var range in ((TextPattern)pattern).GetSelection())
            {
                string text = range.GetText(remaining);
                if (string.IsNullOrEmpty(text)) continue; // a caret is not a selection
                parts.Add(text); remaining -= text.Length;
                if (remaining <= 0) break;
            }
            return parts.Count == 0 ? null : string.Join("\n", parts);
        }

        static bool Within(AutomationElement element, AutomationElement root)
        {
            if (element == null) return false;
            for (int i = 0; i < 32 && element != null; i++)
            {
                if (Automation.Compare(element, root)) return true;
                element = TreeWalker.RawViewWalker.GetParent(element);
            }
            return false;
        }

        internal static string Read(IntPtr window, Point? pointer)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { return ReadOnce(window, pointer); }
                catch (System.Runtime.InteropServices.COMException) { if (attempt >= 2) throw; }
                catch (ElementNotAvailableException) { if (attempt >= 2) throw; }
                System.Threading.Thread.Sleep(80);
            }
        }

        static string ReadOnce(IntPtr window, Point? pointer)
        {
            var root = AutomationElement.FromHandle(window);
            if (root == null) return null;
            AutomationElement focused = null;
            try { focused = AutomationElement.FocusedElement; }
            catch (System.Runtime.InteropServices.COMException) { } // use the known window/document instead
            catch (ElementNotAvailableException) { }
            // Never search sibling controls if the focus is in a password field.
            if (focused != null && focused.Current.IsPassword) return null;
            var starts = new List<AutomationElement>();
            if (Within(focused, root)) starts.Add(focused);
            if (pointer.HasValue)
            {
                var pointed = AutomationElement.FromPoint(pointer.Value);
                if (pointed != null && pointed.Current.IsPassword) return null;
                if (Within(pointed, root)) starts.Add(pointed);
            }
            foreach (var start in starts)
            {
                var current = start;
                for (int i = 0; i < 24 && current != null; i++)
                {
                    string selected = Selected(current);
                    if (!string.IsNullOrWhiteSpace(selected)) return selected;
                    if (Automation.Compare(current, root)) break;
                    current = TreeWalker.RawViewWalker.GetParent(current);
                }
            }
            // Browser selection belongs to Document, even when focus remains on a link
            // or a non-text child. Breadth-first traversal keeps cross-process work bounded.
            var queue = new Queue<AutomationElement>(); queue.Enqueue(root); int visited = 0;
            while (queue.Count > 0 && visited++ < 160)
            {
                var element = queue.Dequeue();
                if (element.Current.IsPassword || element.Current.IsOffscreen) continue;
                string selected = Selected(element);
                if (!string.IsNullOrWhiteSpace(selected)) return selected;
                var child = TreeWalker.ControlViewWalker.GetFirstChild(element);
                for (int siblings = 0; child != null && siblings < 40 && queue.Count < 160; siblings++)
                {
                    queue.Enqueue(child); child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                }
            }
            return null;
        }
    }
}
