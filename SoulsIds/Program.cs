using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SoulsIds
{
    static class Program
    {
        // https://stackoverflow.com/questions/7198639/c-sharp-application-both-gui-and-commandline
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        // Application class is not used, since it's a library now. May split this out.
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && !args.Contains("/gui"))
            {
                Application.Exit();
            }
            else
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }
}
