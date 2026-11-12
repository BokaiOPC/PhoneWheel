import SwiftUI

@main
struct PhoneWheelApp: App {
    var body: some Scene {
        WindowGroup {
            ContentView()
                .onAppear {
                    MotionSender.shared.start()
                }
        }
    }
}
