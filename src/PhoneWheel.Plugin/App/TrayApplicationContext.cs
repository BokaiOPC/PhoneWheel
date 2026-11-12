using System;
using System.IO;
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

    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private DateTime _lastFrameUtc = DateTime.UtcNow;
    private DateTime _lastTunnelRestartUtc = DateTime.MinValue;
    private bool _everReceivedFrame;
    private int _staleRestartCount;

    private readonly string _configPath;
    private DateTime _configWriteUtc;
    private DateTime _lastConfigCheckUtc = DateTime.MinValue;

    public TrayApplicationContext(string iproxyPath, AppConfig config)
    {
        _engine = new SteeringEngine(config.Steering);
        _iproxy = new IproxyLauncher(iproxyPath, config.Transport.LocalPort, config.Transport.DevicePort);
        _configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        _configWriteUtc = File.Exists(_configPath) ? File.GetLastWriteTimeUtc(_configPath) : DateTime.MinValue;

        _tray = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "PhoneWheel 方向盘插件",
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("状态窗口", null, (_, _) => _status.Show());
        menu.Items.Add("校准中位（保持当前握持姿势）", null, (_, _) => CalibrateNeutral());
        menu.Items.Add("重连 USB 隧道", null, (_, _) =>
        {
            _status.AppendLog("手动重连 USB 隧道…");
            _iproxy.RequestRestart();
        });
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;

        _sensor.FrameReceived += OnFrame;
        _sensor.CalibrationRequested += () =>
        {
            _engine.CaptureNeutral();
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

            RecoverStaleTunnel();
            ReloadConfigIfChanged();
            _status.UpdateStatus(_engine, _engine.IsOnline && _sensor.IsConnected);
        };
        _tick.Start();

        _ = _iproxy.StartAsync();
        _ = _sensor.RunAsync("127.0.0.1", _iproxy.LocalPort, CancellationToken.None);

        _status.AppendLog("插件已启动：插入数据线 → 手机运行伴侣 App → 保持握持姿势点'校准中位'");
    }

    private void OnFrame(AttitudeSample s)
    {
        _lastFrameUtc = DateTime.UtcNow;
        _everReceivedFrame = true;
        _staleRestartCount = 0;
        _lastTunnelRestartUtc = DateTime.MinValue;
        _engine.Update(s);

        // 首帧自动以当前姿态为中位，随后可由托盘菜单显式重新校准
        if (!_engine.IsCalibrated)
        {
            _engine.CaptureNeutral();
        }
    }

    /// <summary>
    /// appsettings.json 热重载：调手感需要反复试，改完存盘 0.5s 内生效，
    /// 不必重启插件、不必重新校准中位。只替换标量参数，不动传输与虚拟手柄。
    /// </summary>
    private void ReloadConfigIfChanged()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastConfigCheckUtc).TotalMilliseconds < 500)
        {
            return;
        }
        _lastConfigCheckUtc = now;

        DateTime write;
        try
        {
            write = File.Exists(_configPath) ? File.GetLastWriteTimeUtc(_configPath) : DateTime.MinValue;
        }
        catch
        {
            return; // 文件正被编辑器写入，下一轮再读
        }

        if (write == _configWriteUtc)
        {
            return;
        }
        _configWriteUtc = write;

        var fresh = AppConfig.Load(_configPath);
        _engine.ApplyConfig(fresh.Steering);
        _status.AppendLog($"配置已热重载 → {_engine.ConfigSummary}");
    }

    /// <summary>
    /// 隧道静默看门狗，覆盖两种卡死：重装 App / 数据线重插后，
    /// iproxy 可能仍存活且本地端口仍可连接，但设备侧连接已死。
    ///  A) 曾收到帧后连续 4s 无帧（App 挂起 / 拔线 / Sideloadly 重装后 usbmux 重枚举）；
    ///  B) 冷启动僵死：插件启动时手机 listener 尚未就绪，SensorClient 连上半开连接、
    ///     从未收到任何帧，ReadAsync 一直阻塞不会重连。旧版要求「曾收到帧」才触发，
    ///     这种场景会永久卡死，手机一直显示「等待电脑」。
    /// 重拉 iproxy 会同时打断 SensorClient 的半开连接，触发其 2s 退避重连。
    /// </summary>
    private void RecoverStaleTunnel()
    {
        var now = DateTime.UtcNow;
        var uptime = (now - _startedUtc).TotalSeconds;
        var silentSec = (now - _lastFrameUtc).TotalSeconds;
        var stalled = _everReceivedFrame ? silentSec > 4 : uptime > 10;
        if (!stalled)
        {
            return;
        }

        // 连续重拉仍无数据时降频，避免手机锁屏时刷屏。
        var minInterval = _staleRestartCount >= 3 ? 30 : 15;
        if ((now - _lastTunnelRestartUtc).TotalSeconds <= minInterval)
        {
            return;
        }

        _lastTunnelRestartUtc = now;
        _staleRestartCount++;
        if (_staleRestartCount == 1)
        {
            _status.AppendLog(_everReceivedFrame
                ? "USB 隧道静默超过 4 秒，自动重拉 iproxy…"
                : "启动后一直没收到手机数据，自动重拉 USB 隧道…");
        }
        else if (_staleRestartCount == 3)
        {
            _status.AppendLog("多次重拉仍无数据：请确认手机 PhoneWheel 在前台、未锁屏，且数据线已点『信任』");
        }
        _iproxy.RequestRestart();
    }

    private void CalibrateNeutral()
    {
        _engine.CaptureNeutral();
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
