using System;
using System.IO;
using System.Threading;
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

        // 单实例保护：重复双击启动器会让两个插件抢同一个 27183 端口和同一条 USB 隧道，
        // 表现为手机「等待电脑」、链路时通时断。第二个实例直接提示后退出。
        using var single = new Mutex(true, @"Local\PhoneWheel.Plugin.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "PhoneWheel 已经在运行了。" + Environment.NewLine + Environment.NewLine +
                "请到右下角托盘（可能在 ^ 里）找图标，右键 → 状态窗口。",
                "PhoneWheel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            var config = AppConfig.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
            var iproxyPath = Path.Combine(AppContext.BaseDirectory, "tools", "iproxy", "iproxy.exe");
            Application.Run(new TrayApplicationContext(iproxyPath, config));
        }
        catch (Exception ex)
        {
            // 托盘程序没有控制台，未处理异常会「双击后什么都没发生」。
            // 这里统一兜底：写 crash.log + 弹窗，让故障可见。
            var logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            try
            {
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}");
            }
            catch
            {
                // 日志写不了也要把错误显示出来
            }

            MessageBox.Show(
                $"PhoneWheel 启动失败：{ex.Message}{Environment.NewLine}{Environment.NewLine}" +
                $"详情见 {logPath}",
                "PhoneWheel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            try { single.ReleaseMutex(); }
            catch { /* 进程退出时系统也会自动释放 */ }
        }
    }
}
