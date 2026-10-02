using System;
using System.Windows.Forms;
using KFTCTaxCAP.KioskSim.Forms;

namespace KFTCTaxCAP.KioskSim
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
