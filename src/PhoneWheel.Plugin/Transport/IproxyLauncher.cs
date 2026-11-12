using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneWheel.Plugin.Transport;

/// <summary>
/// 管理 iproxy（libimobiledevice）进程：把 PC 本地 TCP 端口转发到 iPhone 上的监听端口。
/// 依赖 iTunes 的 Apple Mobile Device Service（AMDS，127.0.0.1:27015）。
/// 设备插拔 / AMDS 重启会导致 iproxy 退出，本类负责按退避策略自动重拉。
/// </summary>
public sealed class IproxyLauncher : IAsyncDisposable
{
    private readonly string _exePath;
    private readonly int _localPort;
    private readonly int _devicePort;
    private readonly CancellationTokenSource _cts = new();

    private Process? _proc;
    private int _restartCount;

    public int LocalPort => _localPort;
    public bool IsRunning => _proc is { HasExited: false };

    public event Action<string>? Log;

    public IproxyLauncher(string exePath, int localPort = 27183, int devicePort = 12345)
    {
        _exePath = exePath;
        _localPort = localPort;
        _devicePort = devicePort;
    }

    public Task StartAsync() => Task.Run(SuperviseAsync);

    private async Task SuperviseAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            if (!File.Exists(_exePath))
            {
                Log?.Invoke($"缺少 {_exePath}：请随插件分发 libimobiledevice 的 iproxy.exe");
                await SleepAsync(5000);
                continue;
            }

            try
            {
                // 新版 libimobiledevice（含 jrjr/libimobiledevice-windows 预编译包）要求
                // 单个 "LOCAL_PORT:DEVICE_PORT" 参数，旧版才是两个空格分隔的数字。
                // 传旧格式会直接打印 usage 并退出，导致 USB 隧道反复重拉、永远连不上。
                var psi = new ProcessStartInfo(_exePath, $"{_localPort}:{_devicePort}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                _proc = Process.Start(psi);
                if (_proc is null)
                {
                    await SleepAsync(2000);
                    continue;
                }

                _ = DrainAsync(_proc);
                Log?.Invoke($"iproxy 已启动（本地 {_localPort} → 设备 {_devicePort}）");

                var ok = await WaitPortAsync(_localPort, 8000);
                Log?.Invoke(ok
                    ? "USB 隧道就绪"
                    : "USB 隧道未就绪（设备未连接 / 未信任此电脑 / AMDS 未运行？）");

                await _proc.WaitForExitAsync(_cts.Token);
                Log?.Invoke($"iproxy 退出 code={_proc.ExitCode}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"iproxy 异常: {ex.Message}");
            }

            if (_cts.IsCancellationRequested)
            {
                break;
            }

            _restartCount++;
            var delay = Math.Min(500 * _restartCount, 5000);
            Log?.Invoke($"{delay}ms 后重拉 USB 隧道…");
            await SleepAsync(delay);
        }
    }

    private async Task SleepAsync(int ms)
    {
        try
        {
            await Task.Delay(ms, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // 退出
        }
    }

    private static async Task DrainAsync(Process p)
    {
        try
        {
            await p.StandardOutput.ReadToEndAsync();
        }
        catch
        {
            // ignore
        }

        try
        {
            await p.StandardError.ReadToEndAsync();
        }
        catch
        {
            // ignore
        }
    }

    private static async Task<bool> WaitPortAsync(int port, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                using var c = new TcpClient();
                await c.ConnectAsync("127.0.0.1", port);
                return true;
            }
            catch
            {
                await Task.Delay(250);
            }
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            if (_proc is { HasExited: false })
            {
                _proc.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
        }

        _proc?.Dispose();
        _cts.Dispose();
    }
}
