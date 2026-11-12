using System;
using System.Drawing;
using System.Windows.Forms;
using PhoneWheel.Plugin.Core;

namespace PhoneWheel.Plugin.App;

/// <summary>状态窗口：实时转角 / 踏板 / 链路 / 日志，便于校准与排障。</summary>
public sealed class StatusForm : Form
{
    private readonly ProgressBar _steer = new() { Minimum = -100, Maximum = 100, Width = 280, Height = 18 };
    private readonly ProgressBar _throttle = new() { Minimum = 0, Maximum = 100, Width = 280, Height = 18 };
    private readonly ProgressBar _brake = new() { Minimum = 0, Maximum = 100, Width = 280, Height = 18 };
    private readonly Label _info = new() { AutoSize = true };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Width = 460,
        Height = 180,
        Font = new Font("Consolas", 9f),
    };

    public StatusForm()
    {
        Text = "PhoneWheel 状态";
        ClientSize = new Size(500, 360);
        FormClosing += (_, e) =>
        {
            Hide();
            e.Cancel = true;
        };

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(12),
            WrapContents = false,
        };
        panel.Controls.Add(new Label { Text = "转向（-100..100）", AutoSize = true });
        panel.Controls.Add(_steer);
        panel.Controls.Add(new Label { Text = "油门", AutoSize = true });
        panel.Controls.Add(_throttle);
        panel.Controls.Add(new Label { Text = "刹车", AutoSize = true });
        panel.Controls.Add(_brake);
        panel.Controls.Add(_info);
        panel.Controls.Add(_log);
        Controls.Add(panel);
    }

    public void AppendLog(string msg)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(msg));
            return;
        }

        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
    }

    public void UpdateStatus(SteeringEngine engine, bool link)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateStatus(engine, link));
            return;
        }

        _steer.Value = Math.Clamp(engine.AxisValue / 327, -100, 100);
        _throttle.Value = engine.Throttle * 100 / 255;
        _brake.Value = engine.Brake * 100 / 255;
        _info.Text = $"链路: {(link ? "在线" : "离线")}   " +
                     $"校准: {(engine.IsCalibrated ? "完成" : "未完成（请保持握持姿势点'校准中位'）")}   " +
                     $"当前角: {engine.AngleDeg:F1}°";
    }
}
