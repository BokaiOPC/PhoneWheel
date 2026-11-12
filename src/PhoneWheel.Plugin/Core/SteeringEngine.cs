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
    /// <summary>
    /// 解算模式：
    /// "roll" = 重力在屏幕平面内的方向角，手机竖握/斜握、屏幕朝向自己，像真方向盘一样绕视线轴转（默认）；
    /// "yaw"  = 手机 +Y 轴在参考系水平面投影的夹角，手机平放、像转盘一样绕竖直轴转。
    /// 两种握法的物理动作完全不同，选错会表现为「转 90° 才有反应 / 只有一边能转」。
    /// </summary>
    public string Mode { get; set; } = "roll";

    /// <summary>虚拟方向盘总锁止角（度）。roll 模式建议 180（手腕舒适范围 ±90°），yaw/GT 可 540–900。</summary>
    public float MaxLockDeg { get; set; } = 180f;

    /// <summary>中位死区（度），消除手部微抖与支架旷量。</summary>
    public float DeadzoneDeg { get; set; } = 2.0f;

    /// <summary>灵敏度系数，1 = 线性缩放终点。</summary>
    public float Sensitivity { get; set; } = 1.0f;

    /// <summary>反转左右方向。</summary>
    public bool Invert { get; set; }

    /// <summary>手机"12 点方向"在设备坐标系中的轴，默认 (0,1,0) = 横屏顶部朝前（yaw 模式用）。</summary>
    public Vec3 ForwardAxis { get; set; } = new(0f, 1f, 0f);

    /// <summary>
    /// 中位柔化指数 0..0.9。0 = 纯线性；越大中位越钝、大角度越快。
    /// curve(x) = (1-e)·x + e·x³，端点仍为 ±1，不会损失满打行程。
    /// 手机陀螺仪的抖动比实体摇杆大，这一项显著改善直线稳定性。
    /// </summary>
    public float Expo { get; set; } = 0.35f;

    /// <summary>角度变化速率上限（度/秒），抑制姿态解算异常跳变。</summary>
    public float MaxStepDegPerSec { get; set; } = 1400f;

    /// <summary>看门狗超时（毫秒），超时未收到任何帧即回中熄火。</summary>
    public int WatchdogMs { get; set; } = 200;
}

/// <summary>
/// 方向盘引擎：姿态 → 方向盘角（度）→ XInput 轴值。
///
/// 支持两种互补的物理动作（见 SteeringConfig.Mode）：
///  - roll：θ = -wrap( atan2(g.x, g.y) - 中位 )。重力在设备屏幕平面(XY)内的方向角
///    随手机绕屏幕法线（视线轴）的滚转线性变化，±180° 满量程，正是「转方向盘」。
///    手机竖握/斜握时 |g.xy|≈1 可靠；平放时 |g.xy|≈0 不可靠。
///  - yaw：f = Horizontal(q ⊗ a_fwd ⊗ q*)，θ = atan2((f × f₀)·ẑ, f·f₀)。测绕世界竖直轴
///    的偏航，手机平放时 |f.xy|≈1 可靠；竖握时 |f.xy|≈0 不可靠。
/// </summary>
public sealed class SteeringEngine
{
    private const float Rad2Deg = 180f / MathF.PI;

    private readonly SteeringConfig _cfg;

    private Vec3 _neutralForward;
    private float _neutralRollDeg;
    private bool _calibrated;
    private float _rawAngle;
    private float _angle;
    private float _signalQuality;
    private long _lastFrameTicks = -1;
    private bool _online;

    public SteeringEngine(SteeringConfig cfg)
    {
        _cfg = cfg;
        Normalize();
    }

    public bool IsOnline => _online;
    public bool IsCalibrated => _calibrated;
    public float AngleDeg => _angle;
    public Quat LastAttitude { get; private set; } = Quat.Identity;
    public Vec3 LastGravity { get; private set; } = new(0f, -1f, 0f);

    /// <summary>姿态信号质量 0..1。过低说明握法与该模式不匹配。</summary>
    public float SignalQuality => _signalQuality;

    /// <summary>当前握法是否适合所选模式，以及该怎么拿手机。</summary>
    public string ModeHint => _signalQuality >= 0.35f
        ? "信号良好"
        : _cfg.Mode == "yaw"
            ? "信号弱：把手机平放，像转盘一样绕竖直轴转"
            : "信号弱：把手机竖起来/向后倾，屏幕朝向自己，像方向盘一样转";

    /// <summary>当前生效参数，显示在状态窗口，便于边试边调。</summary>
    public string ConfigSummary =>
        $"模式 {_cfg.Mode}  锁止 {_cfg.MaxLockDeg:F0}°  死区 {_cfg.DeadzoneDeg:F1}°  " +
        $"Expo {_cfg.Expo:F2}  灵敏度 {_cfg.Sensitivity:F2}  限速 {_cfg.MaxStepDegPerSec:F0}°/s";

    /// <summary>把配置夹到合法范围，避免 0 锁止角导致除零、或非法模式字符串。</summary>
    private void Normalize()
    {
        _cfg.Mode = _cfg.Mode == "yaw" ? "yaw" : "roll";
        _cfg.MaxLockDeg = Math.Clamp(_cfg.MaxLockDeg, 30f, 1200f);
        _cfg.DeadzoneDeg = Math.Clamp(_cfg.DeadzoneDeg, 0f, 20f);
        _cfg.Sensitivity = Math.Clamp(_cfg.Sensitivity, 0.1f, 3f);
        _cfg.Expo = Math.Clamp(_cfg.Expo, 0f, 0.9f);
        _cfg.MaxStepDegPerSec = Math.Clamp(_cfg.MaxStepDegPerSec, 100f, 5000f);
        _cfg.WatchdogMs = Math.Clamp(_cfg.WatchdogMs, 50, 5000);
    }

    /// <summary>配置热重载：原地更新参数，不重置中位校准、不打断链路。</summary>
    public void ApplyConfig(SteeringConfig src)
    {
        _cfg.Mode = src.Mode;
        _cfg.MaxLockDeg = src.MaxLockDeg;
        _cfg.DeadzoneDeg = src.DeadzoneDeg;
        _cfg.Sensitivity = src.Sensitivity;
        _cfg.Invert = src.Invert;
        _cfg.ForwardAxis = src.ForwardAxis;
        _cfg.Expo = src.Expo;
        _cfg.MaxStepDegPerSec = src.MaxStepDegPerSec;
        _cfg.WatchdogMs = src.WatchdogMs;
        Normalize();
    }

    /// <summary>解算出的左摇杆 X 轴值（-32767..32767）。</summary>
    public short AxisValue { get; private set; }

    public byte Throttle { get; private set; }
    public byte Brake { get; private set; }
    public ushort Buttons { get; private set; }

    /// <summary>以最近一帧姿态记录中位（校准时调用，握持姿势保持不变）。</summary>
    public void CaptureNeutral()
    {
        _neutralForward = LastAttitude.Rotate(_cfg.ForwardAxis).Horizontal();
        _neutralRollDeg = MathF.Atan2(LastGravity.X, LastGravity.Y) * Rad2Deg;
        _calibrated = true;
        _angle = 0f;
        _rawAngle = 0f;
    }

    /// <summary>喂入一帧采样，更新解算结果。</summary>
    public void Update(AttitudeSample s)
    {
        LastAttitude = s.Attitude;
        LastGravity = s.Gravity;
        _lastFrameTicks = DateTime.UtcNow.Ticks;
        _online = true;

        float theta;
        if (_cfg.Mode == "yaw")
        {
            // 绕世界竖直轴：手机 +Y 轴在参考系水平面的投影方向。
            var f = s.Attitude.Rotate(_cfg.ForwardAxis);
            var horizLen = MathF.Sqrt(f.X * f.X + f.Y * f.Y);
            _signalQuality = MathF.Min(1f, horizLen);
            var fwd = new Vec3(f.X, f.Y, 0f).Normalized();

            if (_calibrated && horizLen > 0.15f && _neutralForward.Length > 0.15f)
            {
                var dot = Vec3.Dot(fwd, _neutralForward);
                var cross = Vec3.Cross(fwd, _neutralForward);
                theta = MathF.Atan2(cross.Z, dot) * Rad2Deg;
            }
            else
            {
                theta = _rawAngle; // 握法不匹配时保持上一帧，不乱跳
            }
        }
        else
        {
            // 绕视线轴（方向盘）：重力在设备屏幕平面内的方向角。
            var g = s.Gravity;
            var planar = MathF.Sqrt(g.X * g.X + g.Y * g.Y);
            _signalQuality = MathF.Min(1f, planar);

            if (planar > 0.15f)
            {
                var a = MathF.Atan2(g.X, g.Y) * Rad2Deg;
                // 负号：常见竖握姿势下顺时针转手机（右打）得到正角。
                theta = -WrapDeg(a - _neutralRollDeg);
            }
            else
            {
                theta = _rawAngle; // 手机平放时重力投影是噪声，保持上一帧
            }
        }

        _rawAngle = _cfg.Invert ? -theta : theta;

        // 2) 自适应 EMA：阈值按半锁止角比例划分「微抖 / 中等 / 大动作」。
        var halfLock = MathF.Max(_cfg.MaxLockDeg * 0.5f, 1f);
        var d = MathF.Abs(_rawAngle - _angle);
        var alpha = d > halfLock * 0.15f ? 1.0f
                  : d > halfLock * 0.05f ? 0.60f
                  : 0.30f;
        var target = _angle + (_rawAngle - _angle) * alpha;

        // 3) slew 限幅（默认 1400°/s @100Hz ≈ 14°/帧）
        var maxStep = _cfg.MaxStepDegPerSec / 100f;
        var step = Math.Clamp(target - _angle, -maxStep, maxStep);
        _angle += step;

        // 4) 死区
        var outAngle = MathF.Abs(_angle) < _cfg.DeadzoneDeg
            ? 0f
            : _angle - MathF.Sign(_angle) * _cfg.DeadzoneDeg;

        // 5) 归一化 + 中位柔化曲线 → XInput 轴（curve 端点仍为 ±1，不损失满打行程）
        var x = Math.Clamp(outAngle / halfLock * _cfg.Sensitivity, -1f, 1f);
        var e = _cfg.Expo;
        var curved = (1f - e) * x + e * x * x * x;
        AxisValue = (short)Math.Clamp(curved * 32767f, -32767f, 32767f);

        Throttle = s.Throttle;
        Brake = s.Brake;
        Buttons = s.Buttons;
    }

    private static float WrapDeg(float d)
    {
        while (d > 180f) d -= 360f;
        while (d < -180f) d += 360f;
        return d;
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
        _signalQuality = 0f;
        AxisValue = 0;
        Throttle = 0;
        Brake = 0;
        Buttons = 0;
    }
}
