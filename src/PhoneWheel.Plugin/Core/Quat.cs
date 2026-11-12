using System;

namespace PhoneWheel.Plugin.Core;

/// <summary>轻量三维向量。</summary>
public readonly struct Vec3
{
    public readonly float X, Y, Z;

    public Vec3(float x, float y, float z) => (X, Y, Z) = (x, y, z);

    /// <summary>参考系世界上轴（Z 轴恒为重力反向，见 CoreMotion .xArbitraryZVertical）。</summary>
    public static Vec3 Up => new(0f, 0f, 1f);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3 operator *(Vec3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);

    public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);

    public Vec3 Normalized()
    {
        var l = Length;
        return l < 1e-9f ? this : new Vec3(X / l, Y / l, Z / l);
    }

    /// <summary>去掉参考系竖直(Z)分量，保留水平面投影（方向盘角只取水平面内转角）。</summary>
    public Vec3 Horizontal() => new Vec3(X, Y, 0f).Normalized();
}

/// <summary>
/// 四元数 (x, y, z, w)，语义与 CoreMotion attitude 一致：
/// 表示"设备坐标系 → 参考坐标系"的旋转。
/// </summary>
public readonly struct Quat
{
    public readonly float X, Y, Z, W;

    public Quat(float x, float y, float z, float w) => (X, Y, Z, W) = (x, y, z, w);

    public static readonly Quat Identity = new(0f, 0f, 0f, 1f);

    public static Quat operator *(Quat a, Quat b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    public Quat Conjugate() => new(-X, -Y, -Z, W);

    /// <summary>把设备坐标系向量旋转到参考（世界）坐标系。</summary>
    public Vec3 Rotate(Vec3 v)
    {
        var qv = new Quat(v.X, v.Y, v.Z, 0f);
        var r = this * qv * Conjugate();
        return new Vec3(r.X, r.Y, r.Z);
    }
}
