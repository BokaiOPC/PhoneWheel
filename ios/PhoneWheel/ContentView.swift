import SwiftUI
import UIKit

/// 主界面：上半屏是功能键与状态，下半屏是左右触摸踏板。
/// 手机横屏握持，屏幕朝上略向后倾；保持姿势后点"校准中位"即可开始转向。
struct ContentView: View {
    @ObservedObject private var sender = MotionSender.shared
    @State private var showInfo = false

    var body: some View {
        ZStack(alignment: .top) {
            Color(red: 0.04, green: 0.05, blue: 0.07).ignoresSafeArea()

            TouchPedalView(sender: sender).ignoresSafeArea()

            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 10) {
                    Text("PhoneWheel")
                        .font(.system(size: 17, weight: .semibold))
                        .foregroundColor(.white)
                    Spacer()
                    statusPill
                    Button(action: {
                        sender.sendCalibrationRequest()
                        UIImpactFeedbackGenerator(style: .medium).impactOccurred()
                    }) {
                        Text("校准中位")
                            .font(.system(size: 13, weight: .semibold))
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(.orange)

                    Button(action: { showInfo = true }) {
                        Image(systemName: "info.circle")
                            .foregroundColor(.white.opacity(0.8))
                    }
                }

                HStack(spacing: 8) {
                    HoldButton(title: "升挡", bit: 1 << 0, sender: sender)
                    HoldButton(title: "降挡", bit: 1 << 1, sender: sender)
                    HoldButton(title: "手刹", bit: 1 << 2, sender: sender)
                    HoldButton(title: "视角左", bit: 1 << 3, sender: sender)
                    HoldButton(title: "视角右", bit: 1 << 4, sender: sender)
                    HoldButton(title: "菜单", bit: 1 << 5, sender: sender)
                    HoldButton(title: "返回", bit: 1 << 6, sender: sender)
                    HoldButton(title: "重置", bit: 1 << 7, sender: sender)
                }

                Text(sender.lastMessage)
                    .font(.system(size: 11))
                    .foregroundColor(.white.opacity(0.5))
            }
            .padding(.horizontal, 14)
            .padding(.top, 10)

            VStack {
                Spacer()
                HStack {
                    Text("左下：刹车")
                    Spacer()
                    Text("右下：油门")
                }
                .font(.system(size: 13, weight: .medium))
                .foregroundColor(.white.opacity(0.35))
                .padding(.horizontal, 20)
                .padding(.bottom, 12)
            }
            .allowsHitTesting(false)
        }
        .statusBarHidden(true)
        .sheet(isPresented: $showInfo) {
            InfoSheet()
        }
    }

    private var statusPill: some View {
        HStack(spacing: 6) {
            Circle()
                .fill(sender.clientCount > 0 ? Color.green : Color.orange)
                .frame(width: 8, height: 8)
            Text(sender.clientCount > 0 ? "电脑已连接" : "等待电脑")
                .font(.system(size: 12))
                .foregroundColor(.white.opacity(0.85))
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 5)
        .background(Color.white.opacity(0.1))
        .cornerRadius(20)
    }
}

/// 按住生效的功能键（松开即释放）。
private struct HoldButton: View {
    let title: String
    let bit: UInt16
    let sender: MotionSender

    var body: some View {
        Text(title)
            .font(.system(size: 12, weight: .medium))
            .foregroundColor(.white)
            .frame(maxWidth: .infinity, minHeight: 34)
            .background(Color.white.opacity(0.12))
            .cornerRadius(8)
            .onLongPressGesture(minimumDuration: 0.01, pressing: { pressing in
                sender.setButton(bit, pressed: pressing)
            }, perform: {})
    }
}

private struct InfoSheet: View {
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationView {
            VStack(alignment: .leading, spacing: 14) {
                Text("PhoneWheel 伴侣 App")
                    .font(.title3.weight(.semibold))
                Text("手机通过数据线连接电脑，本机监听 TCP 12345；电脑端 iproxy 把该端口转发到本地 27183。")
                Text("使用步骤")
                    .font(.headline)
                Text("1. 插好数据线并在手机上点「信任此电脑」\n2. 电脑上启动 PhoneWheel 插件\n3. 保持驾驶握持姿势，点「校准中位」\n4. 进入神力科莎 / F1，绑定 Xbox 手柄预设")
                Text("左右下角按住为刹车/油门，手指上下滑动控制深度；两指可同时按（循迹刹车）。")
                Text("请保持本 App 在前台、屏幕常亮（数据线会同时充电）。")
                    .foregroundColor(.secondary)
                Spacer()
            }
            .padding()
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("完成") { dismiss() }
                }
            }
        }
    }
}
