using System;

namespace PhoneWheel.Plugin.Core;

/// <summary>一帧姿态采样（由 iPhone 伴侣 App 上报，字段见 docs/PROTOCOL.md）。</summary>
public readonly struct AttitudeSample
{
    public readonly long TimestampUs;
    public readonly Quat Attitude;
    public readonly Vec3 Gravity;
    public readonly Vec3 RotationRate;
    public readonly byte Throttle;
    public readonly byte Brake;
    public readonly ushort Buttons;
    public readonly byte Battery;

    public AttitudeSample(long timestampUs, Quat attitude, Vec3 gravity, Vec3 rotationRate,
        byte throttle, byte brake, ushort buttons, byte battery)
    {
        TimestampUs = timestampUs;
        Attitude = attitude;
        Gravity = gravity;
        RotationRate = rotationRate;
        Throttle = throttle;
        Brake = brake;
        Buttons = buttons;
        Battery = battery;
    }
}

/// <summary>方向盘手感配置（appsettings.json 映射）。</summary>
public sealed class SteeringConfig
{
    /// <summary>虚拟方向盘总锁止角（度）。神力科莎/F1 常用 270 / 540 / 900。</summary>
    public float MaxLockDeg { get; set; } = 540f;

    /// <summary>中位死区（度），消除支架旷量。</summary>
    public float DeadzoneDeg { get; set; } = 1.0f;

    /// <summary>灵敏度系数，1 = 线性。</summary>
    public float Sensitivity { get; set; } = 1.0f;

    /// <summary>反转左右方向。</summary>
    public bool Invert { get; set; }

    /// <summary>手机"12 点方向"在设备坐标系中的轴，默认 (0,1,0) = 横屏顶部朝前。</summary>
    public Vec3 ForwardAxis { get; set; } = new(0f, 1f, 0f);

    /// <summary>看门狗超时（毫秒），超时未收到任何帧即回中熄火。</summary>
    public int WatchdogMs { get; set; } = 200;
}

/// <summary>
/// 方向盘引擎：四元数姿态 → 方向盘角（度）→ XInput 轴值。
/// 核心思路：手机"12 点方向"轴在当前参考系水平面的投影，与校准中位时的夹角即方向盘角，
/// 与支架俯仰角度、握持抖动无关（详见 docs/DESIGN.md 5.2）。
/// </summary>
public sealed class SteeringEngine
{
    private readonly SteeringConfig _cfg;

    private Vec3 _neutralForward;
    private bool _calibrated;
    private float _rawAngle;
    private float _angle;
    private long _lastFrameTicks = -1;
    private bool _online;

    public SteeringEngine(SteeringConfig cfg) => _cfg = cfg;

    public bool IsOnline => _online;
    public bool IsCalibrated => _calibrated;
    public float AngleDeg => _angle;
    public Quat LastAttitude { get; private set; } = Quat.Identity;

    /// <summary>解算出的左摇杆 X 轴值（-32767..32767）。</summary>
    public short AxisValue { get; private set; }

    public byte Throttle { get; private set; }
    public byte Brake { get; private set; }
    public ushort Buttons { get; private set; }

    /// <summary>以当前姿态记录中位（校准时调用，握持姿势保持不变）。</summary>
    public void CaptureNeutral(Quat attitude)
    {
        _neutralForward = attitude.Rotate(_cfg.ForwardAxis).Horizontal();
        _calibrated = _neutralForward.Length > 0.5f;
        _angle = 0f;
        _rawAngle = 0f;
    }

    /// <summary>喂入一帧采样，更新解算结果。</summary>
    public void Update(AttitudeSample s)
    {
        LastAttitude = s.Attitude;
        _lastFrameTicks = DateTime.UtcNow.Ticks;
        _online = true;

        // 1) 姿态解算：θ = atan2((f × f0)·ẑ, f·f0)，绕参考系世界上轴(Z)
        var fwd = s.Attitude.Rotate(_cfg.ForwardAxis).Horizontal();
        if (_calibrated && fwd.Length > 0.5f && _neutralForward.Length > 0.5f)
        {
            var dot = Vec3.Dot(fwd, _neutralForward);
            var cross = Vec3.Cross(fwd, _neutralForward);
            var theta = MathF.Atan2(cross.Z, dot) * (180f / MathF.PI);
            _rawAngle = _cfg.Invert ? -theta : theta;
        }
        else
        {
            _rawAngle = 0f;
        }

        // 2) 自适应 EMA：小抖动强平滑，大动作快跟随
        var d = MathF.Abs(_rawAngle - _angle);
        var alpha = d > 25f ? 1.0f : d > 8f ? 0.55f : 0.28f;
        var target = _angle + (_rawAngle - _angle) * alpha;

        // 3) slew 限幅（900°/s @100Hz ≈ 9°/帧），抑制异常跳变
        const float maxStep = 9f;
        var step = Math.Clamp(target - _angle, -maxStep, maxStep);
        _angle += step;

        // 4) 死区
        var outAngle = MathF.Abs(_angle) < _cfg.DeadzoneDeg
            ? 0f
            : _angle - MathF.Sign(_angle) * _cfg.DeadzoneDeg;

        // 5) 归一化到 XInput 轴
        var norm = outAngle / (_cfg.MaxLockDeg * 0.5f) * _cfg.Sensitivity;
        AxisValue = (short)Math.Clamp(norm * 32767f, -32767f, 32767f);

        Throttle = s.Throttle;
        Brake = s.Brake;
        Buttons = s.Buttons;
    }

    /// <summary>看门狗：超时未收到帧则回中并置离线（方向盘外设的底线行为）。</summary>
    public void TickWatchdog()
    {
        if (_lastFrameTicks < 0)
        {
            return;
        }

        var elapsedMs = (DateTime.UtcNow.Ticks - _lastFrameTicks) / TimeSpan.TicksPerMillisecond;
        if (!_online || elapsedMs <= _cfg.WatchdogMs)
        {
            return;
        }

        _online = false;
        _angle = 0f;
        _rawAngle = 0f;
        AxisValue = 0;
        Throttle = 0;
        Brake = 0;
        Buttons = 0;
    }
}
