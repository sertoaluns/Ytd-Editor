using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using YTDEditor.Core;
using YTDEditor.UI;

namespace YTDEditor
{
    static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            try { SetProcessDPIAware(); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var config = AppConfig.LoadOrCreate();
            Application.Run(new MainForm(config));
        }
    }
}
