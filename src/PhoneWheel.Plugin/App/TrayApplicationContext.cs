using System;
using System.Threading;
using System.Windows.Forms;
using PhoneWheel.Plugin.Config;
using PhoneWheel.Plugin.Core;
using PhoneWheel.Plugin.Transport;
using PhoneWheel.Plugin.Virtual;

namespace PhoneWheel.Plugin.App;

/// <summary>
/// 托盘应用上下文：组装传输层 → 引擎 → 虚拟手柄，并以 ~100Hz 轮询提交。
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly IproxyLauncher _iproxy;
    private readonly SensorClient _sensor = new();
    private readonly SteeringEngine _engine;
    private readonly ViGEmBridge _vigem = new();
    private readonly StatusForm _status = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 8 };

    public TrayApplicationContext(string iproxyPath, AppConfig config)
    {
        _engine = new SteeringEngine(config.Steering);
        _iproxy = new IproxyLauncher(iproxyPath, config.Transport.LocalPort, config.Transport.DevicePort);

        _tray = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "PhoneWheel 方向盘插件",
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("状态窗口", null, (_, _) => _status.Show());
        menu.Items.Add("校准中位（保持当前握持姿势）", null, (_, _) => CalibrateNeutral());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;

        _sensor.FrameReceived += OnFrame;
        _sensor.CalibrationRequested += () =>
        {
            _engine.CaptureNeutral(_engine.LastAttitude);
            _status.AppendLog("收到手机端校准请求：中位已重置");
        };
        _sensor.Log += _status.AppendLog;
        _iproxy.Log += _status.AppendLog;

        if (!config.VigemEnabled)
        {
            _status.AppendLog("虚拟手柄已在配置中禁用（vigemEnabled=false）");
        }
        else if (!_vigem.Connect())
        {
            _status.AppendLog(_vigem.LastError ?? "虚拟手柄创建失败");
        }
        else
        {
            _status.AppendLog("虚拟 Xbox 360 手柄已创建");
        }

        _tick.Tick += (_, _) =>
        {
            _engine.TickWatchdog();
            if (_vigem.IsConnected)
            {
                _vigem.Submit(_engine.AxisValue, _engine.Throttle, _engine.Brake, (WheelButtons)_engine.Buttons);
            }

            _status.UpdateStatus(_engine, _engine.IsOnline && _sensor.IsConnected);
        };
        _tick.Start();

        _ = _iproxy.StartAsync();
        _ = _sensor.RunAsync("127.0.0.1", _iproxy.LocalPort, CancellationToken.None);

        _status.AppendLog("插件已启动：插入数据线 → 手机运行伴侣 App → 保持握持姿势点'校准中位'");
    }

    private void OnFrame(AttitudeSample s)
    {
        _engine.Update(s);

        // 首帧自动以当前姿态为中位，随后可由托盘菜单显式重新校准
        if (!_engine.IsCalibrated)
        {
            _engine.CaptureNeutral(s.Attitude);
        }
    }

    private void CalibrateNeutral()
    {
        _engine.CaptureNeutral(_engine.LastAttitude);
        _status.AppendLog($"已校准中位（当前角归零，锁止角 {_engine.IsCalibrated}）");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Stop();
            _vigem.Release();
            _vigem.Dispose();
            _sensor.DisposeAsync().AsTask().Wait();
            _iproxy.DisposeAsync().AsTask().Wait();
            _tray.Visible = false;
            _tray.Dispose();
            _status.Dispose();
        }

        base.Dispose(disposing);
    }
}
