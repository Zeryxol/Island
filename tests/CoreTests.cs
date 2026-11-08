using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
class CoreTests
{
    static Assembly app;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) { return app.GetType("Island." + name, true); }
    static void Assert(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            app = Assembly.LoadFrom(args[0]);
            var settings = T("Settings");
            var values = (Dictionary<string,string>)settings.GetField("values", Static).GetValue(null);
            values.Clear(); values["tabs"] = "note,-clip,actions,shelf,timer";
            var tabs = (List<KeyValuePair<string,bool>>)settings.GetMethod("List").Invoke(null, new object[] { "tabs" });
            Assert(tabs[0].Key == "note" && !tabs[1].Value && tabs.Last().Key == "monitor" && tabs.Count == 6, "existing order and hidden tabs survive migration");
            values["buttons"] = "lock,-island,media,picker,shot,-winset";
            var buttons = (List<KeyValuePair<string,bool>>)settings.GetMethod("List").Invoke(null, new object[] { "buttons" });
            Assert(buttons[0].Key == "lock" && buttons.Last().Key == "translate" && !buttons.Last().Value, "translator button stays opt-in");
            Assert((string)settings.GetMethod("Get").Invoke(null, new object[] { "translateAuto" }) == "0", "automatic selection transmission is off by default");
            string json = "{\"sentences\":[{\"trans\":\"Привет, мир!\\n\"},{\"trans\":\"👨‍👩‍👧‍👦 &lt;test&gt;\"}]}";
            string parsed = (string)T("Translator").GetMethod("ParseTranslation", Static).Invoke(null, new object[] { json });
            Assert(parsed == "Привет, мир!\n👨‍👩‍👧‍👦 &lt;test&gt;", "translation response preserves sentences, Unicode, line breaks and literal entities");
            bool rejected = false;
            try { T("Translator").GetMethod("ParseTranslation", Static).Invoke(null, new object[] { "{\"sentences\":[]}" }); }
            catch (TargetInvocationException e) { rejected = e.InnerException is InvalidOperationException; }
            Assert(rejected, "empty service response is rejected");
            string path = Path.Combine(Path.GetTempPath(), "island-hash-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(path, "", new UTF8Encoding(false));
                string hash = (string)T("Updates").GetMethod("Hash", Static).Invoke(null, new object[] { path });
                Assert(hash == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "updater SHA-256 known vector");
                T("Updates").GetField("staged", Static).SetValue(null, path);
                T("Updates").GetField("digest", Static).SetValue(null, new string('0',64));
                T("Updates").GetMethod("Install").Invoke(null, null);
                Assert(File.Exists(path) && (string)T("Updates").GetField("Status").GetValue(null) != "", "tampered update cannot replace running executable");
            }
            finally { File.Delete(path); }
            var v = app.GetName().Version;
            var tag = Version.Parse(v.ToString(3)); tag = new Version(tag.Major, tag.Minor, tag.Build, 0);
            Assert(v == tag, "release tag matches four-component assembly version");
            values["lang"] = "en";
            Assert((string)T("L").GetMethod("T").Invoke(null, new object[] { "Переводчик" }) == "Translator", "English translator label");
            values["lang"] = "ru";
            if (args.Contains("--online"))
            {
                object[] request = { "Hello, world!", null };
                string translated = (string)T("Translator").GetMethod("Translate").Invoke(null, request);
                Assert(!string.IsNullOrWhiteSpace(translated) && translated.Contains("мир") && (string)request[1] == "EN → RU · Google Translate", "live English to Russian translation");
                Console.WriteLine(translated);
                foreach (string word in new[] { "ты", "вы" })
                {
                    request = new object[] { word, null };
                    translated = (string)T("Translator").GetMethod("Translate").Invoke(null, request);
                    Assert(translated.Trim().Equals("you", StringComparison.OrdinalIgnoreCase), "live pronoun regression: " + word + " -> " + translated);
                }
                request = new object[] { "ты мне поможешь?", null };
                translated = (string)T("Translator").GetMethod("Translate").Invoke(null, request);
                Assert(translated.IndexOf("you", StringComparison.OrdinalIgnoreCase) >= 0 && translated.IndexOf("help", StringComparison.OrdinalIgnoreCase) >= 0, "whole phrase keeps its context");
            }
            Console.WriteLine("All checks passed."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
