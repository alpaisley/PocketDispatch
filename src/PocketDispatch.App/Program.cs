using System;
using System.Threading;
using System.Windows.Forms;

namespace PocketDispatch.App
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // single instance - a second launch just exits
            bool first;
            using (var mutex = new Mutex(true, "PocketDispatch.App.SingleInstance", out first))
            {
                if (!first) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.Run(new MainForm());
            }
        }
    }
}
