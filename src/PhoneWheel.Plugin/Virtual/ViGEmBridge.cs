using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using PhoneWheel.Plugin.Core;

namespace PhoneWheel.Plugin.Virtual;

/// <summary>按钮位标志（与 iPhone 端协议 buttons 字段一致）。</summary>
[Flags]
public enum WheelButtons : ushort
{
    None = 0,
    ShiftUp = 1 << 0,
    ShiftDown = 1 << 1,
    Handbrake = 1 << 2,
    LookLeft = 1 << 3,
    LookRight = 1 << 4,
    Menu = 1 << 5,
    View = 1 << 6,
    ResetCar = 1 << 7,
}

/// <summary>
/// 基于 ViGEmBus 的虚拟 Xbox 360 手柄封装。
/// 神力科莎 / F1 系列将其识别为标准 XInput 手柄：左摇杆 X = 转向，LT/RT = 刹车/油门。
/// 需预先安装 ViGEmBus 驱动：https://github.com/nefarius/ViGEmBus
///
/// API 说明（Nefarius.ViGEm.Client 1.21.256）：
/// Xbox360Axis / Xbox360Slider / Xbox360Button 是带静态字段的强类型属性类（非枚举）；
/// SetAxisValue / SetSliderValue / SetButtonState 写入报告，SubmitReport() 提交；
/// AutoSubmitReport 设为 false 以免每写入一个值就提交一次（100Hz 下开销过高）。
/// </summary>
public sealed class ViGEmBridge : IDisposable
{
    private ViGEmClient? _client;
    private IXbox360Controller? _pad;

    public bool IsConnected { get; private set; }
    public string? LastError { get; private set; }

    public bool Connect()
    {
        try
        {
            _client = new ViGEmClient();
            _pad = _client.CreateXbox360Controller();
            _pad.AutoSubmitReport = false;
            _pad.Connect();
            IsConnected = true;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"虚拟手柄创建失败: {ex.Message}（请确认已安装 ViGEmBus）";
            IsConnected = false;
            return false;
        }
    }

    /// <summary>提交一帧输入（由主循环以 ~100Hz 调用）。</summary>
    public void Submit(short steerAxis, byte throttle, byte brake, WheelButtons buttons)
    {
        if (!IsConnected || _pad is null)
        {
            return;
        }

        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, steerAxis);
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, brake);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, throttle);
        _pad.SetButtonState(Xbox360Button.RightShoulder, (buttons & WheelButtons.ShiftUp) != 0);
        _pad.SetButtonState(Xbox360Button.LeftShoulder, (buttons & WheelButtons.ShiftDown) != 0);
        _pad.SetButtonState(Xbox360Button.A, (buttons & WheelButtons.Handbrake) != 0);
        _pad.SetButtonState(Xbox360Button.X, (buttons & WheelButtons.LookLeft) != 0);
        _pad.SetButtonState(Xbox360Button.B, (buttons & WheelButtons.LookRight) != 0);
        _pad.SetButtonState(Xbox360Button.Start, (buttons & WheelButtons.Menu) != 0);
        _pad.SetButtonState(Xbox360Button.Back, (buttons & WheelButtons.View) != 0);
        _pad.SetButtonState(Xbox360Button.Y, (buttons & WheelButtons.ResetCar) != 0);
        _pad.SubmitReport();
    }

    /// <summary>熄火：回中、松踏板，供离线/退出时调用。</summary>
    public void Release()
    {
        if (!IsConnected || _pad is null)
        {
            return;
        }

        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, 0);
        _pad.SubmitReport();
    }

    public void Dispose()
    {
        try
        {
            _pad?.Disconnect();
        }
        catch
        {
            // ignore
        }

        try
        {
            _client?.Dispose();
        }
        catch
        {
            // ignore
        }

        IsConnected = false;
    }
}
