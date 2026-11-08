using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Island
{
    // %APPDATA%\Island\settings.txt, one "key=value" per line
    static class Settings
    {
        static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "settings.txt");
        static readonly Dictionary<string, string> values = new Dictionary<string, string>();

        // ordered lists: "id" is shown, "-id" is hidden
        static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            { "theme", "dark" }, { "lang", "ru" }, { "clock24", "1" }, { "seconds", "0" },
            { "weather", "1" }, { "city", "" }, { "pill", "auto" },
            { "tabs", "actions,clip,shelf,timer,note,monitor" },
            { "buttons", "media,picker,shot,island,lock,-winset,-translate" },
            { "gauges", "cpu,ram,bat,ping,-gpu,-temp,-net" },
            { "monitorItems", "cpu,ram,gpu,temp,net" },
            { "monitoring", "1" }, { "updates", "1" },
            { "translator", "1" }, { "translateAuto", "0" }, { "translateTarget", "auto" },
        };

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "actions", "Действия" }, { "clip", "Буфер" }, { "shelf", "Полка" }, { "timer", "Таймер" }, { "note", "Заметка" },
            { "media", "Плеер" }, { "picker", "Пипетка" }, { "shot", "Скриншот" }, { "winset", "Параметры Windows" }, { "lock", "Блокировка" }, { "island", "Настройки" },
            { "cpu", "ЦП" }, { "ram", "ОЗУ" }, { "bat", "Питание" }, { "ping", "Пинг" },
            { "gpu", "GPU" }, { "temp", "Температура" }, { "net", "Сеть" },
            { "monitor", "Система" }, { "translate", "Переводчик" },
        };

        public static string Name(string id) { string n; return Names.TryGetValue(id, out n) ? n : id; }

        public static void Load()
        {
            values.Clear();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) values[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    }
            }
            catch (Exception e) { Log.Write("settings", "не прочитались: " + e.Message); }
        }

        public static void Save(Dictionary<string, string> changed)
        {
            foreach (var kv in changed) values[kv.Key] = kv.Value;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, values.Select(kv => kv.Key + "=" + kv.Value), new UTF8Encoding(false));
        }

        public static string Get(string key)
        {
            string v;
            if (values.TryGetValue(key, out v)) return v;
            return Defaults.TryGetValue(key, out v) ? v : "";
        }

        // the full list with visibility, new ids from the defaults appended at the end
        public static List<KeyValuePair<string, bool>> List(string key)
        {
            var res = new List<KeyValuePair<string, bool>>();
            foreach (var raw in Get(key).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var id = raw.TrimStart('-');
                if (Names.ContainsKey(id) && !res.Any(p => p.Key == id)) res.Add(new KeyValuePair<string, bool>(id, !raw.StartsWith("-")));
            }
            foreach (var raw in Defaults[key].Split(','))
            {
                var id = raw.TrimStart('-');
                if (!res.Any(p => p.Key == id)) res.Add(new KeyValuePair<string, bool>(id, !raw.StartsWith("-")));
            }
            return res;
        }

        public static List<string> Shown(string key) { return List(key).Where(p => p.Value).Select(p => p.Key).ToList(); }

        public static string Theme { get { return Get("theme"); } }
        public static bool English { get { return Get("lang") == "en"; } }
        public static bool Clock24 { get { return Get("clock24") != "0"; } }
        public static bool Seconds { get { return Get("seconds") == "1"; } }
        public static bool WeatherOn { get { return Get("weather") != "0"; } }
        public static string City { get { return Get("city"); } }
        public static string PillRight { get { return Get("pill"); } }

        public static void Restart()
        {
            Log.Write("settings", "перезапуск с новыми настройками");
            Process.Start(Process.GetCurrentProcess().MainModule.FileName, "--restart");
            Application.Current.Shutdown();
        }
    }

    // interface language: Russian strings in the code are the keys
    static class L
    {
        static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "Система", "System" }, { "Сеть", "Network" }, { "Температура", "Temperature" }, { "Переводчик", "Translator" },
            { "Читаем выделение…", "Reading selection…" },
            { "Активное окно изменилось. Выделите текст и повторите Ctrl+Alt+T.", "The active window changed. Select text and press Ctrl+Alt+T again." },
            { "Приложение не отвечает на запрос выделения. Скопируйте текст и нажмите «Перевести буфер».", "The app did not respond to the selection request. Copy the text, then click Translate clipboard." },
            { "Ctrl+Alt+T занято другой программой", "Ctrl+Alt+T is already used by another app" },
            { "Системный мониторинг", "System monitoring" }, { "Показатели на вкладке «Система»", "Metrics in the System tab" },
            { "Переводить при выделении", "Translate on selection" }, { "Язык перевода", "Target language" },
            { "Обновления", "Updates" }, { "Автообновления", "Automatic updates" }, { "Проверить сейчас", "Check now" },
            { "Перезапустить и обновить", "Restart and update" }, { "Обновление готово", "Update ready" },
            { "Нажмите, чтобы перезапустить Island", "Click to restart Island" }, { "Обновления ещё не проверялись", "Updates have not been checked yet" },
            { "Проверка уже идёт…", "Already checking…" }, { "Проверяем обновления…", "Checking for updates…" },
            { "Установлена последняя версия", "Island is up to date" }, { "Не удалось проверить обновления. Попробуйте позже.", "Could not check for updates. Try again later." },
            { "Сначала проверьте обновления", "Check for updates first" }, { "Не удалось установить обновление. Проверьте доступ к папке приложения.", "Could not install the update. Check access to the app folder." },
            { "Выберите показатели в настройках", "Choose metrics in settings" }, { "Мониторинг выключен в настройках", "Monitoring is disabled in settings" },
            { "Самый загруженный движок GPU · Windows WDDM", "Busiest GPU engine · Windows WDDM" },
            { "Датчик недоступен. Поддерживаются NVIDIA GPU и датчики запущенного LibreHardwareMonitor / OpenHardwareMonitor.", "Sensor unavailable. Supports NVIDIA GPUs and sensors from running LibreHardwareMonitor / OpenHardwareMonitor." },
            { "Слишком длинный символ в выделении", "A text element is too long" }, { "Сервис не вернул перевод", "The service returned no translation" },
            { "Сервис перевода недоступен или исчерпан дневной лимит. Попробуйте позже.", "Translation is unavailable or the daily quota is exhausted. Try again later." },
            { "Не удалось прочитать выделение. Скопируйте текст и нажмите «Перевести буфер».", "Could not read the selection. Copy the text, then click Translate clipboard." },
            { "Переводчик выключен в настройках", "Translator is disabled in settings" }, { "Сначала скопируйте текст", "Copy some text first" },
            { "Буфер обмена занят. Попробуйте ещё раз.", "Clipboard is busy. Try again." }, { "Выделите не более 4000 символов", "Select at most 4000 characters" },
            { "Переводим…", "Translating…" }, { "Нет связи с сервисом перевода. Проверьте интернет и попробуйте ещё раз.", "Could not reach the translation service. Check your connection and try again." },
            { "Не удалось перевести текст", "Could not translate the text" }, { "Скопировать", "Copy" }, { "Перевести буфер", "Translate clipboard" },
            { "Русский", "Russian" }, { "Вкладки", "Tabs" },
            { "Выделите текст и нажмите Ctrl+Alt+T. Текст отправляется в Google Translate только по команде или при включённом переводе выделения. Нужен интернет.", "Select text and press Ctrl+Alt+T. Text is sent to Google Translate only on request, or when selection translation is enabled. Internet access is required." },
            { "Действия", "Actions" }, { "Буфер", "Clipboard" }, { "Полка", "Shelf" }, { "Таймер", "Timer" }, { "Заметка", "Notes" },
            { "Плеер", "Player" }, { "Пипетка", "Color picker" }, { "Скриншот", "Screenshot" }, { "Параметры Windows", "Windows settings" },
            { "Блокировка", "Lock" }, { "Настройки островка", "Island settings" }, { "ЦП", "CPU" }, { "ОЗУ", "RAM" }, { "Питание", "Power" }, { "Пинг", "Ping" },
            { "Пипетка - взять цвет с экрана", "Pick a color from the screen" }, { "Скриншот области", "Screenshot of an area" },
            { "Заблокировать", "Lock" }, { "Параметры", "Settings" }, { "Настройки", "Settings" },
            { "Скрыть островок", "Hide island" }, { "Показать островок", "Show island" }, { "Вернуть наверх по центру", "Move back to top center" },
            { "Запускать с Windows", "Start with Windows" }, { "Выход", "Exit" }, { "Закрыть", "Close" }, { "Вид", "Look" }, { "Время", "Time" }, { "Погода", "Weather" }, { "Островок", "Island" }, { "Кнопки", "Buttons" }, { "Показатели", "Gauges" }, { "Тема", "Theme" }, { "Язык", "Language" }, { "Формат", "Format" }, { "Сохранить", "Save" }, { "Тёмная", "Dark" }, { "Серая", "Gray" }, { "Светлая", "Light" }, { "24 часа", "24 hours" }, { "12 часов", "12 hours" }, { "Показывать секунды", "Show seconds" }, { "Показывать погоду", "Show weather" }, { "Справа на свёрнутом островке", "Right side of the closed island" }, { "Авто", "Auto" }, { "Заряд", "Battery" }, { "Ничего", "Nothing" },
            { "Положить на полку", "Drop on the shelf" }, { "Отпустите файлы или текст", "Release files or text" },
            { "Скопируйте что-нибудь - история появится здесь", "Copy something and it will show up here" },
            { "Перетащите файлы на островок · ПКМ по элементу - убрать", "Drag files onto the island · right-click to remove" },
            { "Запустить выставленное время", "Start" }, { "Сбросить", "Reset" }, { "Нажмите, чтобы скопировать снова", "Click to copy again" },
            { "Время вышло", "Time is up" }, { "Caps Lock включён", "Caps Lock on" }, { "Caps Lock выключен", "Caps Lock off" },
            { "Зарядка подключена", "Charger connected" }, { "Работа от батареи", "On battery" }, { "Низкий заряд", "Low battery" },
            { "Нет интернета", "No internet" }, { "Соединение потеряно", "Connection lost" }, { "Интернет вернулся", "Back online" },
            { "Ничего не играет", "Nothing playing" }, { "Сейчас играет", "Now playing" },
            { "Не беспокоить (или ПКМ по островку)", "Do not disturb (or right-click the island)" }, { "Не беспокоить", "Do not disturb" },
            { "Сообщения не будут всплывать", "Messages won't pop up" }, { "Уведомления включены", "Notifications on" },
            { "Камера включена", "Camera on" }, { "Микрофон включён", "Microphone on" },
            { "Принять - открыть звонок", "Answer" }, { "Отклонить", "Decline" },
            { "К списку (Esc)", "Back (Esc)" }, { "Удалить заметку", "Delete note" }, { "Новая заметка", "New note" }, { "Создать заметку", "New note" },
            { "Открыть · ПКМ - удалить", "Open · right-click to delete" }, { "Пустая заметка", "Empty note" },
            { "Посчитать, перевести единицы, открыть программу…", "Calculate, convert, open an app…" },
            { "Найти в интернете", "Search the web" }, { "Открыть программу", "Open app" }, { "Запустить", "Run" },
            { "Исправить раскладку · скопировать", "Fix keyboard layout · copy" }, { "Раскладка исправлена", "Layout fixed" },
            { "клик - взять · Esc", "click to pick · Esc" }, { "Цвет скопирован", "Color copied" },
            { "Скопировано", "Copied" }, { "Текст скопирован", "Text copied" }, { "Ссылка скопирована", "Link copied" },
            { "Почта скопирована", "Email copied" }, { "Изображение скопировано", "Image copied" }, { "Файл скопирован", "File copied" },
            { "Снова в буфере", "Back in clipboard" }, { "Загружено", "Downloaded" }, { "Уведомление", "Notification" }, { "Текст", "Text" },
            { "Часы", "Hours" }, { "Минуты", "Minutes" }, { "Секунды", "Seconds" },
        };

        static readonly string[][] Prefixes =
        {
            new[] { "Громкость ", "Volume " }, new[] { "Заряд ", "Battery " }, new[] { "Пинг ", "Ping " }, new[] { "Таймер на ", "Timer for " },
            new[] { "Скопировано файлов: ", "Files copied: " }, new[] { "На полке: ", "On shelf: " }, new[] { "На полку добавлено: ", "Added to shelf: " },
            new[] { "Всего на полке: ", "Total on shelf: " }, new[] { "Пропущено: ", "Skipped: " }, new[] { "Осталось ", "" },
        };
        static readonly string[][] Suffixes =
        {
            new[] { "% - подключите зарядку", "% left, plug in the charger" }, new[] { " · наведите → «Полка»", " · hover → Shelf" },
            new[] { " · Enter - скопировать", " · Enter to copy" }, new[] { " завершён", " is done" }, new[] { " источник(ов)", " source(s)" },
            new[] { " · играет", " · playing" }, new[] { " играет", " playing" }, new[] { " пауза", " paused" },
            new[] { " мин", " min" }, new[] { " мс", " ms" }, new[] { " сек", " s" }, new[] { " ч", " h" },
        };

        public static string T(string s)
        {
            if (string.IsNullOrEmpty(s) || !Settings.English) return s;
            string v;
            if (En.TryGetValue(s, out v)) return v;
            foreach (var p in Prefixes) if (s.StartsWith(p[0])) { s = p[1] + s.Substring(p[0].Length); break; }
            foreach (var p in Suffixes) if (s.EndsWith(p[0])) { s = s.Substring(0, s.Length - p[0].Length) + p[1]; break; }
            return s.Replace(" мин ", " min ").Replace(" ч ", " h ");
        }
    }

    // current weather from open-meteo.com, refreshed every 15 minutes
    static class Weather
    {
        public static string Text, Short, Place;

        public static void Start()
        {
            if (!Settings.WeatherOn) return;
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                while (true)
                {
                    try { Update(); }
                    catch (Exception e) { Log.Write("weather", "не получилось: " + e.Message); }
                    System.Threading.Thread.Sleep(Text == null ? 60 * 1000 : 15 * 60 * 1000);
                }
            });
        }

        static string Get(string url)
        {
            using (var wc = new WebClient { Encoding = Encoding.UTF8 })
            {
                // met.no and nominatim ask apps to name themselves
                wc.Headers[HttpRequestHeader.UserAgent] = "Island/1.1 github.com/Zeryxol/Island";
                return wc.DownloadString(url);
            }
        }

        static string Num(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\":\"?(-?[0-9.]+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        static string Round(string v) { return Math.Round(double.Parse(v, CultureInfo.InvariantCulture), 3).ToString(CultureInfo.InvariantCulture); }

        static void Update()
        {
            string lat, lon, place;
            if (Settings.City != "")
            {
                var g = Get("https://nominatim.openstreetmap.org/search?format=json&limit=1&accept-language=" + (Settings.English ? "en" : "ru") + "&q=" + Uri.EscapeDataString(Settings.City));
                lat = Num(g, "lat"); lon = Num(g, "lon");
                var n = Regex.Match(g, "\"name\":\"([^\"]+)\"");
                place = n.Success ? Regex.Unescape(n.Groups[1].Value) : Settings.City;
            }
            else
            {
                // rough location by IP; with a VPN it is the VPN's city, so a city can be set in the settings
                var g = Get("http://ip-api.com/json/?fields=lat,lon,city&lang=" + (Settings.English ? "en" : "ru"));
                lat = Num(g, "lat"); lon = Num(g, "lon");
                var n = Regex.Match(g, "\"city\":\"([^\"]+)\"");
                place = n.Success ? n.Groups[1].Value : null;
            }
            if (lat == null || lon == null) { Log.Write("weather", "город не найден"); return; }
            // the first time step of the met.no forecast is the current hour
            var w = Get("https://api.met.no/weatherapi/locationforecast/2.0/compact?lat=" + Round(lat) + "&lon=" + Round(lon));
            var t = Num(w, "air_temperature");
            var sym = Regex.Match(w, "\"symbol_code\":\"([a-z_]+)\"");
            if (t == null) return;
            int deg = (int)Math.Round(double.Parse(t, CultureInfo.InvariantCulture));
            var temp = (deg > 0 ? "+" : "") + deg + "°";
            Short = temp;
            Place = place;
            Text = Describe(sym.Success ? sym.Groups[1].Value : "");
        }

        static string Describe(string s)
        {
            bool en = Settings.English;
            if (s.Contains("thunder")) return en ? "thunderstorm" : "гроза";
            if (s.Contains("snow") || s.Contains("sleet")) return en ? "snow" : "снег";
            if (s.Contains("rain")) return en ? "rain" : "дождь";
            if (s.Contains("fog")) return en ? "fog" : "туман";
            if (s.StartsWith("clearsky")) return en ? "clear" : "ясно";
            if (s.StartsWith("fair") || s.StartsWith("partlycloudy")) return en ? "partly cloudy" : "переменная облачность";
            if (s.StartsWith("cloudy")) return en ? "cloudy" : "пасмурно";
            return "";
        }
    }

    // settings open right inside the expanded island
    partial class IslandWindow
    {
        Grid settingsHost;
        bool SettingsOpen { get { return settingsHost != null && settingsHost.Visibility == Visibility.Visible; } }

        public void ShowSettings()
        {
            selectionRequest++; selectionBusy = false;
            translationVisible = false; translationRequest++;
            if (settingsHost == null)
            {
                settingsHost = new Grid { Background = UI.Bg, Visibility = Visibility.Collapsed, RenderTransform = new TranslateTransform() };
                layerExpanded.Children.Add(settingsHost);
            }
            if (hidden) ToggleHidden();
            settingsHost.Children.Clear();
            settingsHost.Children.Add(new SettingsPanel(HideSettings, box => BeginTyping(box)));
            settingsHost.Visibility = Visibility.Visible;
            settingsHost.Opacity = 0; ((TranslateTransform)settingsHost.RenderTransform).Y = 14;
            UI.Ease(settingsHost, OpacityProperty, 1, 220);
            UI.Ease(settingsHost.RenderTransform, TranslateTransform.YProperty, 0, 320);
            SetMode(Mode.Expanded);
        }

        void HideSettings()
        {
            selectionRequest++; selectionBusy = false;
            translationVisible = false; translationRequest++;
            if (settingsHost == null || settingsHost.Visibility != Visibility.Visible) return;
            settingsHost.Visibility = Visibility.Collapsed;
            settingsHost.Children.Clear();
            EndTyping();
        }
    }

    class SettingsPanel : DockPanel
    {
        readonly Dictionary<string, string> draft = new Dictionary<string, string>();
        readonly Action<TextBox> focus;

        Border saveBtn;
        TextBlock saveText;

        // a list of sections on the left, the chosen one on the right
        public SettingsPanel(Action close, Action<TextBox> focus)
        {
            this.focus = focus;
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var back = UI.Pressable(UI.Glyph("\uE711", 11, UI.Fg), 30, 30, 15, close);
            back.ToolTip = L.T("Закрыть");
            DockPanel.SetDock(back, Dock.Right);
            saveText = UI.Text("Сохранить", 12.5, UI.Gray, FontWeights.SemiBold);
            saveBtn = UI.Pressable(Centered(saveText), 104, 30, 15, () => { if (draft.Count == 0) return; Settings.Save(draft); Settings.Restart(); });
            saveBtn.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(saveBtn, Dock.Right);
            head.Children.Add(back); head.Children.Add(saveBtn);
            var title = UI.Text("Настройки", 20, UI.Fg, FontWeights.SemiBold); title.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);
            DockPanel.SetDock(head, Dock.Top);
            Children.Add(head);

            var sections = new List<KeyValuePair<string, Func<UIElement>>>
            {
                Page("Вид", p => { Label(p, "Тема"); p.Children.Add(Choice("theme", new[] { "dark", "Тёмная", "gray", "Серая", "light", "Светлая" }));
                                   Label(p, "Язык"); p.Children.Add(Choice("lang", new[] { "ru", "Русский", "en", "English" })); }),
                Page("Время", p => { Label(p, "Формат"); p.Children.Add(Choice("clock24", new[] { "1", "24 часа", "0", "12 часов" }));
                                     p.Children.Add(Toggle("seconds", "Показывать секунды")); }),
                Page("Погода", p => { p.Children.Add(Toggle("weather", "Показывать погоду")); p.Children.Add(CityBox()); }),
                Page("Островок", p => { Label(p, "Справа на свёрнутом островке"); p.Children.Add(Choice("pill", new[] { "auto", "Авто", "battery", "Заряд", "ping", "Пинг", "weather", "Погода", "none", "Ничего" })); }),
                Page("Вкладки", p => p.Children.Add(Order("tabs"))),
                Page("Кнопки", p => p.Children.Add(Order("buttons"))),
                Page("Показатели", p => p.Children.Add(Order("gauges"))),
                Page("Система", p => { p.Children.Add(Toggle("monitoring", "Системный мониторинг")); Label(p, "Показатели на вкладке «Система»"); p.Children.Add(Order("monitorItems")); }),
                Page("Переводчик", p => { p.Children.Add(Toggle("translator", "Переводчик")); p.Children.Add(Toggle("translateAuto", "Переводить при выделении")); Label(p, "Язык перевода"); p.Children.Add(Choice("translateTarget", new[] { "auto", "RU ↔ EN", "ru", "Русский", "en", "English" })); Hint(p, "Выделите текст и нажмите Ctrl+Alt+T. Текст отправляется в Google Translate только по команде или при включённом переводе выделения. Нужен интернет."); }),
                Page("Обновления", p => { p.Children.Add(Toggle("updates", "Автообновления")); Hint(p, "Island " + Updates.Current + (Settings.English ? ". Checks at startup and every 6 hours. Updates download in the background; click the notification to restart." : ". Проверка при запуске и каждые 6 часов. Новая версия загружается в фоне; нажмите уведомление, чтобы перезапустить островок.")); var status = UI.Text(Updates.Status, 12, UI.Gray); status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(2, 8, 0, 8); p.Children.Add(status); var check = UI.Pressable(Centered(UI.Text("Проверить сейчас", 12, UI.Fg)), 170, 30, 15, () => Updates.Check(true, text => status.Text = text)); p.Children.Add(check); var install = UI.Pressable(Centered(UI.Text("Перезапустить и обновить", 12, UI.Fg)), 220, 30, 15, () => { Updates.Install(); status.Text = Updates.Status; }); install.Margin = new Thickness(0, 8, 0, 0); p.Children.Add(install); }),
            };

            var nav = new StackPanel { Width = 118, Margin = new Thickness(0, 0, 14, 0) };
            DockPanel.SetDock(nav, Dock.Left);
            var navScroll = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
            DockPanel.SetDock(navScroll, Dock.Left);
            Children.Add(navScroll);
            var body = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
            Children.Add(body);

            var items = new List<Border>();
            Action<int> select = null;
            select = i =>
            {
                for (int k = 0; k < items.Count; k++)
                {
                    items[k].Background = k == i ? new SolidColorBrush(UI.Line) : Brushes.Transparent;
                    ((TextBlock)items[k].Child).Foreground = k == i ? UI.Fg : UI.Gray;
                }
                body.Content = sections[i].Value();
                body.ScrollToTop();
            };
            for (int i = 0; i < sections.Count; i++)
            {
                int idx = i;
                var t = UI.Text(sections[i].Key, 13, UI.Gray, FontWeights.SemiBold); t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(12, 0, 0, 0);
                var item = new Border { Height = 28, CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 2), Child = t };
                item.MouseEnter += (s, e) => { if (body.Content != null && items.IndexOf(item) != current) item.Background = new SolidColorBrush(UI.Card); };
                item.MouseLeave += (s, e) => { if (idx != current) item.Background = Brushes.Transparent; };
                item.MouseLeftButtonUp += (s, e) => { current = idx; select(idx); };
                items.Add(item); nav.Children.Add(item);
            }
            select(0);
            Margin = new Thickness(22, 16, 22, 14);
        }

        int current;

        static KeyValuePair<string, Func<UIElement>> Page(string name, Action<StackPanel> fill)
        {
            return new KeyValuePair<string, Func<UIElement>>(name, () => { var p = new StackPanel(); fill(p); return p; });
        }

        static void Label(Panel p, string text)
        {
            var t = UI.Text(text, 12, UI.Gray, FontWeights.SemiBold);
            t.Margin = new Thickness(2, p.Children.Count == 0 ? 0 : 12, 0, 6);
            p.Children.Add(t);
        }

        static void Hint(Panel p, string text)
        {
            var t = UI.Text(text, 11.5, UI.Gray); t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(2, 10, 0, 2); p.Children.Add(t);
        }

        // "Save" lights up once something changed
        void Set(string key, string value)
        {
            if (value == Settings.Get(key)) draft.Remove(key); else draft[key] = value;
            bool dirty = draft.Count > 0;
            saveBtn.Background = dirty ? UI.Fg : new SolidColorBrush(UI.Card);
            saveText.Foreground = dirty ? UI.Bg : UI.Gray;
            saveBtn.Cursor = dirty ? Cursors.Hand : null;
        }

        static UIElement Centered(TextBlock t)
        {
            t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        string Value(string key) { string v; return draft.TryGetValue(key, out v) ? v : Settings.Get(key); }

        // segmented control: pairs of value, label
        UIElement Choice(string key, string[] opts)
        {
            var grid = new UniformGrid { Rows = 1 };
            var bg = new Border { CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(UI.Card), Padding = new Thickness(3), Child = grid };
            var cells = new List<Border>();
            Action paint = () =>
            {
                for (int i = 0; i < cells.Count; i++)
                {
                    bool on = opts[i * 2] == Value(key);
                    cells[i].Background = on ? new SolidColorBrush(UI.Line) : Brushes.Transparent;
                    ((TextBlock)cells[i].Child).Foreground = on ? UI.Fg : UI.Gray;
                }
            };
            for (int i = 0; i < opts.Length; i += 2)
            {
                var v = opts[i];
                var c = new Border { Height = 30, CornerRadius = new CornerRadius(11), Cursor = Cursors.Hand, Child = Centered(UI.Text(opts[i + 1], 12.5, UI.Gray, FontWeights.SemiBold)) };
                c.MouseLeftButtonUp += (s, e) => { Set(key, v); paint(); };
                cells.Add(c); grid.Children.Add(c);
            }
            paint();
            return bg;
        }

        UIElement Toggle(string key, string label)
        {
            var row = new DockPanel { Margin = new Thickness(2, 8, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var track = new Border { Width = 40, Height = 24, CornerRadius = new CornerRadius(12) };
            var knob = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0), RenderTransform = new TranslateTransform() };
            track.Child = knob;
            DockPanel.SetDock(track, Dock.Right);
            row.Children.Add(track);
            var t = UI.Text(label, 13.5, UI.Fg); t.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(t);
            Action paint = () =>
            {
                bool on = Value(key) == "1";
                track.Background = new SolidColorBrush(on ? UI.Green : UI.Line);
                UI.Ease(knob.RenderTransform, TranslateTransform.XProperty, on ? 16 : 0, 180);
            };
            row.MouseLeftButtonUp += (s, e) => { Set(key, Value(key) == "1" ? "0" : "1"); paint(); };
            paint();
            return row;
        }

        UIElement CityBox()
        {
            var box = new TextBox
            {
                Text = Settings.City, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = UI.Fg, CaretBrush = UI.Fg,
                FontSize = 13.5, VerticalContentAlignment = VerticalAlignment.Center, SelectionBrush = new SolidColorBrush(UI.Blue)
            };
            var hint = UI.Text("Город (пусто - определить автоматически)", 13.5, new SolidColorBrush(UI.Dim));
            hint.IsHitTestVisible = false; hint.VerticalAlignment = VerticalAlignment.Center; hint.Margin = new Thickness(2, 0, 0, 0);
            hint.Visibility = box.Text == "" ? Visibility.Visible : Visibility.Collapsed;
            box.PreviewMouseLeftButtonDown += (s, e) => focus(box);
            box.TextChanged += (s, e) => { Set("city", box.Text.Trim()); hint.Visibility = box.Text == "" ? Visibility.Visible : Visibility.Collapsed; };
            var g = new Grid(); g.Children.Add(hint); g.Children.Add(box);
            var st = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            st.Children.Add(new Border { Background = new SolidColorBrush(UI.Card), CornerRadius = new CornerRadius(12), Height = 38, Padding = new Thickness(12, 0, 12, 0), Child = g });
            var note = UI.Text("С VPN город по IP может определиться неверно - тогда впишите свой", 11.5, UI.Gray);
            note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(2, 6, 0, 0);
            st.Children.Add(note);
            return st;
        }

        // reorderable list with show/hide checkboxes
        UIElement Order(string key)
        {
            var items = Settings.List(key);
            var list = new StackPanel();
            var frame = new Border { Background = new SolidColorBrush(UI.Card), CornerRadius = new CornerRadius(14), Padding = new Thickness(4), Child = list };
            Action store = () => Set(key, string.Join(",", items.Select(p => (p.Value ? "" : "-") + p.Key)));
            Action render = null;
            render = () =>
            {
                list.Children.Clear();
                for (int i = 0; i < items.Count; i++)
                {
                    int idx = i;
                    var row = new DockPanel { Height = 38, Margin = new Thickness(6, 0, 4, 0) };
                    Func<string, bool, Action, UIElement> btn = (glyph, enabled, act) =>
                    {
                        var b = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Brushes.Transparent, Cursor = enabled ? Cursors.Hand : null, Child = UI.Glyph(glyph, 11, enabled ? UI.Fg : new SolidColorBrush(UI.Dim)) };
                        if (enabled)
                        {
                            b.MouseEnter += (s, e) => b.Background = new SolidColorBrush(UI.CardHover);
                            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
                            b.MouseLeftButtonUp += (s, e) => { act(); store(); render(); };
                        }
                        return b;
                    };
                    var down = btn("", idx < items.Count - 1, () => { var t = items[idx]; items[idx] = items[idx + 1]; items[idx + 1] = t; });
                    var up = btn("", idx > 0, () => { var t = items[idx]; items[idx] = items[idx - 1]; items[idx - 1] = t; });
                    DockPanel.SetDock(down, Dock.Right); DockPanel.SetDock(up, Dock.Right);
                    row.Children.Add(down); row.Children.Add(up);

                    bool on = items[idx].Value;
                    var check = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 12, 0), Background = on ? new SolidColorBrush(UI.Blue) : Brushes.Transparent, BorderBrush = new SolidColorBrush(on ? UI.Blue : UI.Dim), BorderThickness = new Thickness(1.5) };
                    if (on) check.Child = UI.Glyph("", 11, Brushes.White);
                    var label = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand };
                    label.Children.Add(check);
                    var name = UI.Text(Settings.Name(items[idx].Key), 13.5, on ? UI.Fg : UI.Gray); name.VerticalAlignment = VerticalAlignment.Center;
                    label.Children.Add(name);
                    label.MouseLeftButtonUp += (s, e) => { items[idx] = new KeyValuePair<string, bool>(items[idx].Key, !items[idx].Value); store(); render(); };
                    row.Children.Add(label);
                    list.Children.Add(row);
                }
            };
            render();
            return frame;
        }
    }
}
