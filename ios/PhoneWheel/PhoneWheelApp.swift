import SwiftUI
import UIKit

@main
struct PhoneWheelApp: App {
    var body: some Scene {
        WindowGroup {
            ContentView()
                .onAppear {
                    // SwiftUI 的 onAppear 在 MainActor 上执行，UIKit 的屏幕常亮 /
                    // 电量监控设置必须放在这里，不能放到后台队列的 MotionSender 里。
                    UIApplication.shared.isIdleTimerDisabled = true
                    UIDevice.current.isBatteryMonitoringEnabled = true
                    MotionSender.shared.start()
                }
        }
    }
}
