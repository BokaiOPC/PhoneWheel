import UIKit

/// 触觉反馈：接收 PC 端命令帧，用 Taptic Engine 近似路肩/打滑/油门震感。
/// cmd: 1=短震, 2=持续震动, 3=停止；intensity 0-100。
final class HapticController {
    static let shared = HapticController()

    private let impact = UIImpactFeedbackGenerator(style: .rigid)
    private let soft = UIImpactFeedbackGenerator(style: .soft)
    private var timer: Timer?

    private init() {
        impact.prepare()
        soft.prepare()
    }

    func handle(cmd: UInt8, intensity: UInt8) {
        DispatchQueue.main.async {
            switch cmd {
            case 1:
                self.impact.impactOccurred(intensity: CGFloat(max(0.2, Double(intensity) / 100.0)))
            case 2:
                self.startContinuous(intensity: intensity)
            default:
                self.stop()
            }
        }
    }

    private func startContinuous(intensity: UInt8) {
        timer?.invalidate()
        let strength = CGFloat(max(0.2, Double(intensity) / 100.0))
        let generator = intensity > 60 ? impact : soft
        timer = Timer.scheduledTimer(withTimeInterval: 0.08, repeats: true) { _ in
            generator.impactOccurred(intensity: strength)
        }
    }

    private func stop() {
        timer?.invalidate()
        timer = nil
    }
}
