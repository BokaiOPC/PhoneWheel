using System;
using System.IO;
using System.Windows.Forms;
using PhoneWheel.Plugin.App;
using PhoneWheel.Plugin.Config;

namespace PhoneWheel.Plugin;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var config = AppConfig.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        var iproxyPath = Path.Combine(AppContext.BaseDirectory, "tools", "iproxy", "iproxy.exe");
        Application.Run(new TrayApplicationContext(iproxyPath, config));
    }
}
