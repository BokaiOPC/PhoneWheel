using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PhoneWheel.Plugin.Core;

namespace PhoneWheel.Plugin.Transport;

/// <summary>
/// PWR1 帧协议客户端（见 docs/PROTOCOL.md）。
/// 帧头 20 字节小端：magic[4]="PWR1" | version u8 | flags u8 | payloadLen u16 | seq u32 | tSend i64
/// 连接目标为 iproxy 转发的本地 TCP 端口（默认 127.0.0.1:27183）。
/// </summary>
public sealed class SensorClient : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();

    private TcpClient? _client;
    private NetworkStream? _stream;

    public event Action<AttitudeSample>? FrameReceived;
    public event Action? CalibrationRequested;
    public event Action<string>? Log;

    public bool IsConnected => _client?.Connected == true;

    /// <summary>连接并持续收帧；断线自动重连（2s 退避）。</summary>
    public async Task RunAsync(string host, int port, CancellationToken external = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, external);
        while (!linked.IsCancellationRequested)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(host, port, linked.Token);
                _stream = _client.GetStream();
                Log?.Invoke($"已连接 {host}:{port}");
                await ReadLoopAsync(_stream, linked.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"连接失败: {ex.Message}，2s 后重试");
                try
                {
                    await Task.Delay(2000, linked.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                _stream?.Dispose();
                _client?.Dispose();
                _stream = null;
                _client = null;
            }
        }
    }

    private async Task ReadLoopAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[20];
        while (!ct.IsCancellationRequested)
        {
            if (!await ReadExactAsync(stream, header, 20, ct))
            {
                return;
            }

            if (header[0] != (byte)'P' || header[1] != (byte)'W' ||
                header[2] != (byte)'R' || header[3] != (byte)'1')
            {
                throw new InvalidDataException("帧魔术不匹配（非 PWR1 流）");
            }

            var version = header[4];
            if (version != 1)
            {
                throw new InvalidDataException($"不支持的协议版本 {version}");
            }

            var payloadLen = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6, 2));
            if (payloadLen > 1024)
            {
                throw new InvalidDataException($"载荷长度非法: {payloadLen}");
            }

            var seq = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));
            var ts = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(12, 8));

            var payload = new byte[payloadLen];
            if (!await ReadExactAsync(stream, payload, payloadLen, ct))
            {
                return;
            }

            // flags bit4：手机端「校准中位」请求，PC 用最近一帧姿态重置中位
            if ((header[5] & 0x10) != 0)
            {
                CalibrationRequested?.Invoke();
            }

            if (payloadLen == 45)
            {
                FrameReceived?.Invoke(DecodeMotion(ts, seq, payload));
            }
            // 其它帧类型（Hello/Keepalive/命令/错误）按需扩展
        }
    }

    private static async Task<bool> ReadExactAsync(Stream s, byte[] buf, int count, CancellationToken ct)
    {
        var read = 0;
        while (read < count)
        {
            var n = await s.ReadAsync(buf.AsMemory(read, count - read), ct);
            if (n <= 0)
            {
                return false;
            }

            read += n;
        }

        return true;
    }

    /// <summary>运动帧载荷（45 字节，偏移见 docs/PROTOCOL.md 第 2 节）。</summary>
    internal static AttitudeSample DecodeMotion(long tsUs, uint seq, ReadOnlySpan<byte> p)
    {
        _ = seq;
        var q = new Quat(
            BinaryPrimitives.ReadSingleLittleEndian(p.Slice(0, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(p.Slice(4, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(p.Slice(8, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(p.Slice(12, 4)));
        var g = new Vec3(
            BinaryPrimitives.ReadSingleLittleEndian(p.Slice(16, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(p.Slice(20, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(p.Slice(24, 4)));
        var r = new Vec3(
            BinaryPrimitives.ReadSingleLittleEndian(p.Slice(28, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(p.Slice(32, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(p.Slice(36, 4)));
        var throttle = p[40];
        var brake = p[41];
        var buttons = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(42, 2));
        var battery = p[44];
        return new AttitudeSample(tsUs, q, g, r, throttle, brake, buttons, battery);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _stream?.Dispose();
        _client?.Dispose();
        _cts.Dispose();
    }
}
