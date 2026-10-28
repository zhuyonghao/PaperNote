// Run after publishing: swift scripts/verify-macos-openwith.swift APP_PATH FILE_PATH...
import AppKit
import CoreServices
import UniformTypeIdentifiers

guard CommandLine.arguments.count >= 3 else {
    fputs("Usage: verify-macos-openwith.swift APP_PATH FILE_PATH...\n", stderr)
    exit(2)
}
let app = URL(fileURLWithPath: CommandLine.arguments[1]).standardizedFileURL
var failed = false
for path in CommandLine.arguments.dropFirst(2) {
    let file = URL(fileURLWithPath: path).standardizedFileURL
    do {
        let type = try file.resourceValues(forKeys: [.contentTypeKey]).contentType?.identifier ?? "unknown"
        var accepts = DarwinBoolean(false)
        let status = LSCanURLAcceptURL(file as CFURL, app as CFURL, .editor, [], &accepts)
        let listed = NSWorkspace.shared.urlsForApplications(toOpen: file).contains {
            $0.standardizedFileURL.resolvingSymlinksInPath().path == app.resolvingSymlinksInPath().path
        }
        let passed = status == noErr && accepts.boolValue && listed
        print("\(passed ? "PASS" : "FAIL"): \(file.lastPathComponent) · \(type) · accepts=\(accepts.boolValue) · listed=\(listed)")
        if !passed { failed = true }
    } catch {
        print("FAIL: \(path) · \(error.localizedDescription)")
        failed = true
    }
}
exit(failed ? 1 : 0)
