using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhoneWheel.Plugin.Core;

namespace PhoneWheel.Plugin.Config;

public sealed class AppConfig
{
    public TransportConfig Transport { get; set; } = new();

    public SteeringConfig Steering { get; set; } = new();

    public bool VigemEnabled { get; set; } = true;

    public static AppConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AppConfig();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new Vec3JsonConverter() },
            }) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }
}

public sealed class TransportConfig
{
    /// <summary>usb = iproxy 隧道（默认）；wifi = 二期 UDP 模式。</summary>
    public string Mode { get; set; } = "usb";

    public int LocalPort { get; set; } = 27183;

    public int DevicePort { get; set; } = 12345;
}

/// <summary>支持 "0,1,0" 字符串形式的 Vec3 反序列化。</summary>
public sealed class Vec3JsonConverter : JsonConverter<Vec3>
{
    public override Vec3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (string.IsNullOrWhiteSpace(s))
        {
            return new Vec3(0f, 1f, 0f);
        }

        var parts = s.Split(',');
        if (parts.Length != 3)
        {
            return new Vec3(0f, 1f, 0f);
        }

        return new Vec3(
            float.Parse(parts[0]),
            float.Parse(parts[1]),
            float.Parse(parts[2]));
    }

    public override void Write(Utf8JsonWriter writer, Vec3 value, JsonSerializerOptions options)
        => writer.WriteStringValue($"{value.X},{value.Y},{value.Z}");
}
