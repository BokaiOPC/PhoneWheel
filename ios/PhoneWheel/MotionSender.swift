import UIKit
import SwiftUI
import Combine
import CoreMotion
import Network

/// iPhone 伴侣 App 核心（PhoneWheel.iOS）。
///
/// - 以 100Hz 采集 CoreMotion 融合姿态（陀螺仪 + 重力，参考系 .xArbitraryZVertical），
///   经 NWListener(TCP :12345) 向 PC 插件推送 PWR1 帧（见 docs/PROTOCOL.md）；
/// - 由触摸层写入踏板/按钮状态；接收 PC 端触觉指令与校准请求回执；
/// - 数据线连接时由 PC 端 iproxy 做端口转发（本地 27183 ↔ 设备 12345）。
///
/// 需前台运行、屏幕常亮；Info.plist 已声明 NSMotionUsageDescription。
///
/// 线程约定：
/// - workQueue（DispatchQueue）= Network.framework 回调 + 组帧发送，独占 connections / sequence；
/// - motionQueue（OperationQueue）= CoreMotion 回调，收到数据后立即转交 workQueue；
/// - 主线程只做 UI 与 UIKit 设置；踏板/按钮标量用 NSLock 保护。
final class MotionSender: ObservableObject {
    static let shared = MotionSender()

    private let motion = CMMotionManager()
    private let workQueue = DispatchQueue(label: "phonewheel.motion", qos: .userInteractive)
    private let motionQueue: OperationQueue = {
        let q = OperationQueue()
        q.name = "phonewheel.coremotion"
        q.qualityOfService = .userInitiated
        q.maxConcurrentOperationCount = 1
        return q
    }()
    private let lock = NSLock()

    private var listener: NWListener?
    private var connections: [NWConnection] = []
    private var sequence: UInt32 = 0

    private var throttleValue: UInt8 = 0
    private var brakeValue: UInt8 = 0
    private var buttonBits: UInt16 = 0

    @Published private(set) var clientCount: Int = 0
    @Published private(set) var lastMessage: String = "正在启动…"

    private(set) var latestAttitude: CMAttitude?

    private init() {}

    // MARK: - 生命周期

    func start(port: UInt16 = 12345) {
        // 屏幕常亮 / 电量监控由 PhoneWheelApp 的 onAppear（MainActor）设置。
        startListener(port: port)
        startMotion()
    }

    func stop() {
        motion.stopDeviceMotionUpdates()
        listener?.cancel()
        connections.forEach { $0.cancel() }
        connections.removeAll()
        DispatchQueue.main.async {
            self.clientCount = 0
            self.lastMessage = "已停止"
        }
    }

    // MARK: - 输入状态（线程安全）

    func setPedals(throttle: UInt8, brake: UInt8) {
        lock.lock()
        throttleValue = throttle
        brakeValue = brake
        lock.unlock()
    }

    func setButton(_ bit: UInt16, pressed: Bool) {
        lock.lock()
        if pressed {
            buttonBits |= bit
        } else {
            buttonBits &= ~bit
        }
        lock.unlock()
    }

    private func snapshot() -> (UInt8, UInt8, UInt16) {
        lock.lock()
        defer { lock.unlock() }
        return (throttleValue, brakeValue, buttonBits)
    }

    private func batteryByte() -> UInt8 {
        let level = UIDevice.current.batteryLevel
        guard level >= 0 else { return 0 }
        return UInt8(max(0, min(100, Int((level * 100).rounded()))))
    }

    // MARK: - TCP 服务端

    private func startListener(port: UInt16) {
        do {
            listener = try NWListener(using: .tcp, on: NWEndpoint.Port(rawValue: port)!)
            listener?.newConnectionHandler = { [weak self] conn in
                guard let self else { return }
                self.connections.append(conn)
                conn.stateUpdateHandler = { [weak self] state in
                    switch state {
                    case .failed, .cancelled:
                        self?.remove(conn)
                    default:
                        break
                    }
                }
                conn.start(queue: self.workQueue)
                self.receiveCommands(conn)
                DispatchQueue.main.async {
                    self.clientCount = self.connections.count
                    self.lastMessage = "电脑已连接 · 姿态 100Hz"
                }
            }
            listener?.stateUpdateHandler = { [weak self] state in
                DispatchQueue.main.async {
                    switch state {
                    case .ready:
                        self?.lastMessage = "监听 :\(port)，等待电脑连接"
                    case .failed(let error):
                        self?.lastMessage = "监听失败: \(error.localizedDescription)"
                    default:
                        break
                    }
                }
            }
            listener?.start(queue: workQueue)
        } catch {
            DispatchQueue.main.async {
                self.lastMessage = "监听失败: \(error.localizedDescription)"
            }
        }
    }

    private func remove(_ conn: NWConnection) {
        connections.removeAll { $0 === conn }
        DispatchQueue.main.async {
            self.clientCount = self.connections.count
            if self.clientCount == 0 {
                self.lastMessage = "电脑已断开 · 等待重连"
            }
        }
    }

    // MARK: - 运动采样

    private func startMotion() {
        guard motion.isDeviceMotionAvailable else {
            DispatchQueue.main.async { self.lastMessage = "此设备不支持运动传感器" }
            return
        }
        motion.deviceMotionUpdateInterval = 1.0 / 100.0
        // .xArbitraryZVertical：Z 轴固定为重力反向，不依赖磁力计，抗车内磁场干扰。
        // CoreMotion 的 handler 版本要 OperationQueue（不是 DispatchQueue）。
        motion.startDeviceMotionUpdates(using: .xArbitraryZVertical, to: motionQueue) { [weak self] data, error in
            guard let self, let data, error == nil else { return }
            // 统一到 workQueue 后再访问 connections / sequence，避免与 NW 回调竞争。
            self.workQueue.async {
                self.latestAttitude = data.attitude
                self.broadcast(data)
            }
        }
    }

    private func broadcast(_ data: CMDeviceMotion) {
        guard !connections.isEmpty else { return }
        let frame = makeFrame(data)
        for conn in connections where conn.state == .ready {
            conn.send(content: frame, completion: .contentProcessed { _ in })
        }
    }

    // MARK: - 帧编码（PWR1，小端；见 docs/PROTOCOL.md）

    private func header(flags: UInt8, payloadLen: UInt16) -> Data {
        var out = Data()
        out.append(contentsOf: [0x50, 0x57, 0x52, 0x31]) // "PWR1"
        out.append(1)                                    // version
        out.append(flags)
        var len = payloadLen.littleEndian
        out.append(Data(bytes: &len, count: 2))
        sequence &+= 1
        var seq = sequence.littleEndian
        out.append(Data(bytes: &seq, count: 4))
        var ts = Int64(Date().timeIntervalSince1970 * 1_000_000).littleEndian
        out.append(Data(bytes: &ts, count: 8))
        return out
    }

    private func makeFrame(_ data: CMDeviceMotion) -> Data {
        let (throttle, brake, buttons) = snapshot()
        var out = header(flags: 0, payloadLen: 45)

        let q = data.attitude.quaternion
        appendF32(&out, Float(q.x))
        appendF32(&out, Float(q.y))
        appendF32(&out, Float(q.z))
        appendF32(&out, Float(q.w))

        appendF32(&out, Float(data.gravity.x))
        appendF32(&out, Float(data.gravity.y))
        appendF32(&out, Float(data.gravity.z))

        appendF32(&out, Float(data.rotationRate.x))
        appendF32(&out, Float(data.rotationRate.y))
        appendF32(&out, Float(data.rotationRate.z))

        out.append(throttle)
        out.append(brake)
        var b = buttons.littleEndian
        out.append(Data(bytes: &b, count: 2))
        out.append(batteryByte())
        return out
    }

    private func appendF32(_ data: inout Data, _ value: Float) {
        var le = value.bitPattern.littleEndian
        data.append(Data(bytes: &le, count: 4))
    }

    /// 手机端一键校准：向 PC 发送 flags=0x10 的空载荷帧，PC 端据此重置中位。
    func sendCalibrationRequest() {
        workQueue.async {
            let frame = self.header(flags: 0x10, payloadLen: 0)
            for conn in self.connections where conn.state == .ready {
                conn.send(content: frame, completion: .contentProcessed { _ in })
            }
            DispatchQueue.main.async {
                self.lastMessage = "已发送校准请求 · 中位已重置"
            }
        }
    }

    // MARK: - 触觉指令（PC → 手机）

    private func receiveCommands(_ conn: NWConnection) {
        conn.receive(minimumIncompleteLength: 20, maximumLength: 512) { [weak self] content, _, isComplete, error in
            if let content, content.count >= 25 {
                self?.handleCommand(content)
            }
            if isComplete || error != nil {
                self?.remove(conn)
                return
            }
            self?.receiveCommands(conn)
        }
    }

    private func handleCommand(_ data: Data) {
        guard data[5] & 0x01 != 0 else { return } // flags bit0 = 命令帧
        HapticController.shared.handle(cmd: data[20], intensity: data[21])
    }
}
