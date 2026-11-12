import SwiftUI
import UIKit

/// 强制横屏：不依赖 Info.plist（Xcode 15+ 有时会用自动生成的 Info.plist 覆盖
/// 我们提供的那份，导致 UISupportedInterfaceOrientations 丢失）。
/// 通过 UIApplicationDelegate 返回 .landscape 是最可靠的一层保障。
final class PhoneWheelAppDelegate: NSObject, UIApplicationDelegate {
    func application(_ application: UIApplication,
                     supportedInterfaceOrientationsFor window: UIWindow?) -> UIInterfaceOrientationMask {
        .landscape
    }
}

@main
struct PhoneWheelApp: App {
    @UIApplicationDelegateAdaptor(PhoneWheelAppDelegate.self) var appDelegate

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
