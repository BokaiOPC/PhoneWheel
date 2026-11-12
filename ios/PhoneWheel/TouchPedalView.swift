import SwiftUI
import UIKit

/// UIKit 多点触控踏板：屏幕下半部分左右分区，可双指同时按住（循迹刹车）。
/// 手指在分区内上下滑动控制 0-255 的模拟量（越靠上越大）。
final class PedalUIView: UIView {
    var onState: ((UInt8, UInt8) -> Void)?

    private var throttleTouch: UITouch?
    private var brakeTouch: UITouch?
    private var throttleValue: UInt8 = 0
    private var brakeValue: UInt8 = 0

    override init(frame: CGRect) {
        super.init(frame: frame)
        isMultipleTouchEnabled = true
        backgroundColor = .clear
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    private var pedalTop: CGFloat { bounds.midY }

    private func inPedalArea(_ touch: UITouch) -> Bool {
        touch.location(in: self).y >= pedalTop
    }

    private func isLeft(_ touch: UITouch) -> Bool {
        touch.location(in: self).x < bounds.midX
    }

    private func intensity(_ touch: UITouch) -> UInt8 {
        let y = touch.location(in: self).y
        let height = max(1, bounds.height - pedalTop)
        let t = max(0, min(1, (bounds.maxY - y) / height))
        return UInt8(t * 255)
    }

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent?) {
        for t in touches where inPedalArea(t) {
            if isLeft(t), brakeTouch == nil {
                brakeTouch = t
                brakeValue = intensity(t)
            } else if !isLeft(t), throttleTouch == nil {
                throttleTouch = t
                throttleValue = intensity(t)
            }
        }
        notify()
    }

    override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent?) {
        for t in touches {
            if t === brakeTouch {
                brakeValue = intensity(t)
            } else if t === throttleTouch {
                throttleValue = intensity(t)
            }
        }
        notify()
    }

    override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent?) {
        clear(touches)
    }

    override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent?) {
        clear(touches)
    }

    private func clear(_ touches: Set<UITouch>) {
        for t in touches {
            if t === brakeTouch {
                brakeTouch = nil
                brakeValue = 0
            } else if t === throttleTouch {
                throttleTouch = nil
                throttleValue = 0
            }
        }
        notify()
    }

    private func notify() {
        onState?(throttleValue, brakeValue)
    }
}

struct TouchPedalView: UIViewRepresentable {
    let sender: MotionSender

    func makeUIView(context: Context) -> PedalUIView {
        let view = PedalUIView()
        view.onState = { throttle, brake in
            sender.setPedals(throttle: throttle, brake: brake)
        }
        return view
    }

    func updateUIView(_ uiView: PedalUIView, context: Context) {}
}
