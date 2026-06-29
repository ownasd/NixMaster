using System;
using System.Windows.Forms;
using NixMaster.Core;
using NixMaster.UI;

namespace NixMaster
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            AppState.LoadSettings();
            Application.Run(new LoginForm());
        }
    }
}
