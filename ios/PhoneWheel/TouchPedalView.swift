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

    /// 越靠屏幕底部踩得越深（符合踩踏板的直觉）。
    /// 旧实现是「越靠上越大」，按在屏幕底部（拇指自然位置）反而得到 0，
    /// 用户会感觉「踩了没反应 / 延迟很高」。现在按下即给 55% 基线，
    /// 也避免老游戏把 LT/RT 当数字键时低模拟量完全不触发。
    private func intensity(_ touch: UITouch) -> UInt8 {
        let y = touch.location(in: self).y
        let height = max(1, bounds.height - pedalTop)
        let t = max(0, min(1, (y - pedalTop) / height))
        return UInt8(140 + t * 115)
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
    /// 把当前踏板量回传给 SwiftUI，用于界面上实时显示百分比（也方便排障）。
    let onState: (UInt8, UInt8) -> Void

    func makeUIView(context: Context) -> PedalUIView {
        let view = PedalUIView()
        view.onState = { throttle, brake in
            sender.setPedals(throttle: throttle, brake: brake)
            onState(throttle, brake)
        }
        return view
    }

    func updateUIView(_ uiView: PedalUIView, context: Context) {}
}
