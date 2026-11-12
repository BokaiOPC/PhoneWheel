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

    /// iOS 挂起会回收 App 的 TCP 监听；回到前台必须重建，否则电脑端一直显示离线。
    func applicationDidBecomeActive(_ application: UIApplication) {
        UIApplication.shared.isIdleTimerDisabled = true
        MotionSender.shared.resume()
    }
}

@main
struct PhoneWheelApp: App {
    @UIApplicationDelegateAdaptor(PhoneWheelAppDelegate.self) var appDelegate
    @Environment(\.scenePhase) private var scenePhase

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
                // 双保险：部分 iOS 版本不会转发 applicationDidBecomeActive，
                // scenePhase 一定能感知前台切换。resume() 内部有节流，重复调用安全。
                .onChange(of: scenePhase) { phase in
                    if phase == .active {
                        UIApplication.shared.isIdleTimerDisabled = true
                        MotionSender.shared.resume()
                    }
                }
        }
    }
}
