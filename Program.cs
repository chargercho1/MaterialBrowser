using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MaterialBrowser
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            TryDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && args[0] == "--selftest")
            {
                SelfTest.Run(args.Length > 1 ? args[1] : ".");
                return;
            }
            if (args.Length > 0 && args[0] == "--apitest")
            {
                ApiTest.Run(args.Length > 1 ? args[1] : null);
                return;
            }
            // --live <page> [url]: opens the window in a given state and keeps it on screen,
            // so an external tool can take a real window screenshot.
            if (args.Length > 0 && args[0] == "--live")
            {
                MainForm.DisableBackdrop = true;
                var form = new MainForm();
                // MBR_ENGINE=2 exercises the Firefox engine from an external screenshot tool.
                string engine = Environment.GetEnvironmentVariable("MBR_ENGINE") ?? "";
                if (engine.Length > 0) Settings.EngineMode = Convert.ToInt32(engine);
                string page = args.Length > 1 ? args[1] : "home";
                if (page == "settings") form.DebugShowSettings();
                else if (page == "bookmarks") form.DebugShowBookmarks();
                else if (page == "history") form.DebugShowHistory();
                else if (page == "downloads") form.DebugShowDownloads();
                if (args.Length > 2)
                {
                    Debug.Log("live navigate " + args[2]);
                    form.DebugNavigate(args[2]);
                }
                Application.Run(form);
                return;
            }

            Http.Prepare();
            SettingsFile.Ensure();
            if (Environment.GetEnvironmentVariable("MBR_NOBACKDROP") == "1") MainForm.DisableBackdrop = true;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                try
                {
                    File.AppendAllText(Path.Combine(SettingsFile.Folder, "error.log"),
                        DateTime.Now.ToString("s") + " " + e.Exception + Environment.NewLine);
                }
                catch { }
                MessageBox.Show("Произошла ошибка: " + e.Exception.Message + Environment.NewLine +
                    "Подробности записаны в error.log в папке данных приложения.",
                    "Material Browser", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            Application.Run(new MainForm());
        }

        static void TryDpiAwareness()
        {
            try
            {
                IntPtr context = new IntPtr(-4);
                if (!Native.SetProcessDpiAwarenessContext(context))
                    Native.SetProcessDPIAware();
            }
            catch { try { Native.SetProcessDPIAware(); } catch { } }
        }
    }

    /// <summary>Renders the window in a few states and writes PNGs plus a layout map.</summary>
    static class SelfTest
    {
        static string _sample = "data:text/html;charset=utf-8,%3Chtml%3E";

        public static void Run(string directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "selftest.log"), "start\r\n");
            MainForm.DisableBackdrop = true;

            string mode = Environment.GetEnvironmentVariable("MSM_SCHEME");
            if (mode == "light") AppTheme.DarkOverride = false;
            if (mode == "dark") AppTheme.DarkOverride = true;

            string sample = Environment.GetEnvironmentVariable("MBR_SAMPLE");
            if (!string.IsNullOrEmpty(sample)) _sample = sample;

            using (var form = new MainForm())
            {
                form.Show();
                Pump(6);
                Save(form, Path.Combine(directory, "01-start.png"));

                form.DebugNavigate(_sample);
                Pump(8);
                Save(form, Path.Combine(directory, "02-page.png"));

                form.DebugShowSettings();
                Pump(4);
                Save(form, Path.Combine(directory, "03-settings.png"));

                form.DebugShowBookmarks();
                Pump(3);
                Save(form, Path.Combine(directory, "04-bookmarks.png"));

                DumpLayout(form, directory);
            }
            File.AppendAllText(Path.Combine(directory, "selftest.log"), "done\r\n");
        }

        static void Pump(int times)
        {
            for (int i = 0; i < times; i++)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(140);
            }
        }

        static void Save(Form form, string path)
        {
            using (Bitmap bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format32bppArgb))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void DumpLayout(Form form, string directory)
        {
            const int cols = 100;
            int fw = Math.Max(1, form.ClientSize.Width);
            int fh = Math.Max(1, form.ClientSize.Height);
            int rows = Math.Max(10, fh * cols / fw / 2);
            var grid = new char[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++) grid[r, c] = '.';

            var sb = new StringBuilder();
            sb.Append("size " + fw + "x" + fh + "  dpi " + form.DeviceDpi + "\r\n");
            Paint(form, Point.Empty, fw, fh, grid, 'W', sb);

            sb.Append("+");
            for (int c = 0; c < cols; c++) sb.Append('-');
            sb.Append("+\r\n");
            for (int r = 0; r < rows; r++)
            {
                sb.Append('|');
                for (int c = 0; c < cols; c++) sb.Append(grid[r, c]);
                sb.Append("|\r\n");
            }
            sb.Append("+");
            for (int c = 0; c < cols; c++) sb.Append('-');
            sb.Append("+\r\n");

            string root = string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : Path.GetFullPath(directory);
            File.WriteAllText(Path.Combine(root, "layout.txt"), sb.ToString());
        }

        static void Paint(Control c, Point origin, int fw, int fh, char[,] grid, char mark, StringBuilder log)
        {
            Rectangle r = c.RectangleToScreen(c.ClientRectangle);
            int x0 = (r.Left - origin.X) * grid.GetLength(1) / fw;
            int y0 = (r.Top - origin.Y) * grid.GetLength(0) / fh;
            int x1 = (r.Right - origin.X) * grid.GetLength(1) / fw;
            int y1 = (r.Bottom - origin.Y) * grid.GetLength(0) / fh;
            for (int y = Math.Max(0, y0); y < Math.Min(grid.GetLength(0), y1); y++)
                for (int x = Math.Max(0, x0); x < Math.Min(grid.GetLength(1), x1); x++)
                    grid[y, x] = mark;

            log.Append(mark).Append(' ')
               .Append(c.GetType().Name.PadRight(16))
               .Append(c.Bounds.ToString().PadRight(24))
               .Append(c.Visible ? "vis " : "hid ")
               .Append(c.Enabled ? "en " : "dis")
               .Append(' ').Append(c.Name).AppendLine();

            foreach (Control child in c.Controls) Paint(child, origin, fw, fh, grid, NextMark(mark), log);
        }

        static char NextMark(char mark)
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            int index = alphabet.IndexOf(mark);
            return index < 0 || index + 1 >= alphabet.Length ? '*' : alphabet[index + 1];
        }
    }
}
