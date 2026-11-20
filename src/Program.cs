using System;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using KavrosSlayerHelper.Core;

[assembly: System.Reflection.AssemblyTitle("Kavros Slayer Helper")]
[assembly: System.Reflection.AssemblyProduct("Kavros Slayer Helper")]
[assembly: System.Reflection.AssemblyDescription("Kavros Slayer Helper by Meleeman112: reads your slayer task from the slayer counter and the master's dialogue")]
[assembly: System.Reflection.AssemblyCompany("Meleeman112")]
[assembly: System.Reflection.AssemblyVersion("0.1.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.1.1.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("0.1.1")]

namespace KavrosSlayerHelper
{
    internal static class Program
    {
        public const string Version = "0.1.1";

        [STAThread]
        static int Main(string[] args)
        {
            var opts = new StartupOptions();
            string render = null, mode = "full", demoMonster = null;
            float scale = 1f;
            bool hover = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if ((a == "--attach" || a == "-a") && i + 1 < args.Length) opts.AttachTitle = args[++i];
                else if (a == "--verbose") opts.Verbose = true;
                else if (a == "--demo") opts.Demo = true;
                else if (a == "--no-ocr") opts.NoOcr = true;
                else if (a == "--render" && i + 1 < args.Length) render = args[++i];
                else if (a == "--mode" && i + 1 < args.Length) mode = args[++i].ToLowerInvariant();
                else if (a == "--monster" && i + 1 < args.Length) demoMonster = args[++i];
                else if (a == "--hover") hover = true;
                else if (a == "--scale" && i + 1 < args.Length) float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out scale);
                else if (a == "--help" || a == "-h" || a == "/?")
                {
                    MessageBox.Show(
                        "KavrosSlayerHelper.exe [options]\n\n" +
                        "--attach \"<title>\"   Debug: dock over any window whose title contains <title>\n" +
                        "--demo               Show an example task (nothing is saved)\n" +
                        "--no-ocr             Do not read the screen\n" +
                        "--render <png>       Draw the panel to a PNG and exit (--mode full|compact|minimised|empty, --scale n, --hover)\n" +
                        "--verbose            Echo the log to the console\n\n" +
                        "Data and settings: " + Settings.Folder,
                        "Kavros Slayer Helper " + Version);
                    return 0;
                }
            }
            Log.Verbose = opts.Verbose;
            if (render != null) return Render(render, mode, scale, hover, demoMonster);

            bool created;
            using (var mutex = new Mutex(true, "Local\\KavrosSlayerHelper", out created))
            {
                if (!created) return 1;   // already running; its panel is already on screen
                Native.EnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Write("error: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("fatal: " + e.ExceptionObject);
                Application.Run(new App(opts));
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        /// <summary>--render: draws the panel with the app's own code, for screenshots and checks.</summary>
        static int Render(string path, string mode, float scale, bool hover, string monster)
        {
            try
            {
                string json = Encoding.UTF8.GetString(Resources.Bytes("slayer.json"));
                SlayerData data = SlayerData.Load(json, null);
                var state = new SlayerState
                {
                    Monster = monster ?? "Dragons", Master = "Laniakea", Assigned = 135, Left = 118, Streak = 291, Points = 6257,
                    LastChat = "", LastSource = "counter",
                };
                if (mode == "empty") state = new SlayerState { Streak = 291, Points = 6257 };
                var t = new SlayerTracker(data, state);
                using (var panel = new SlayerPanel())
                {
                    panel.Compact = mode == "compact";
                    panel.Minimised = mode == "minimised";
                    panel.SetHoverForDemo(hover);
                    panel.SetView(PanelView.Build(t, mode == "empty" ? "Looking for the slayer counter\u2026" : ""));
                    using (var bmp = panel.RenderToBitmap(scale <= 0 ? 1 : scale)) bmp.Save(path, ImageFormat.Png);
                }
                Console.WriteLine("rendered " + path);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 2; }
        }
    }
}
