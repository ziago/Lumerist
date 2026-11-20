import AppKit
import SwiftUI
import LumeristCore

/// General → "Lightroom plug-in": status, install/update/remove, setup guide.
struct PluginSection: View {
    @State private var installer = PluginInstaller.shared
    @State private var confirmsRemove = false
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        SettingsSection(title: "Lightroom plug-in", systemImage: "puzzlepiece.extension",
                        footer: "The plug-in ships inside Lumerist. Replaced or removed copies are kept in ~/Library/Application Support/Lumerist/plugin-backups.") {
            SettingRow(title: "Status", detail: statusDetail) {
                HStack(spacing: Synth.Space.s) {
                    statusPill
                    primaryAction
                }
            }
            SettingRow(title: "Setup guide", detail: "The four first-run steps, checked live.") {
                Button("Open guide…") {
                    NSApp.activate()
                    openWindow(id: Onboarding.windowID)
                }
                .buttonStyle(.neonSecondary)
            }
            SettingRow(title: "Plug-in files", detail: installer.installedLocation?.path(percentEncoded: false)) {
                HStack(spacing: Synth.Space.s) {
                    Button("Show in Finder") { installer.revealInFinder() }
                        .buttonStyle(.neonQuiet)
                    Button("Remove…") { confirmsRemove = true }
                        .buttonStyle(.neonDestructive)
                        .disabled(installer.status == .notInstalled || installer.status == nil
                                  || installer.isLightroomRunning || installer.isWorking)
                }
            }
            if let phaseText {
                Text(phaseText.text)
                    .synthBody(12, color: phaseText.color)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.horizontal, Synth.rowInset)
                    .padding(.vertical, 10)
            }
        }
        .onAppear { installer.refresh() }
        .confirmationDialog("Remove the plug-in from Lightroom?", isPresented: $confirmsRemove) {
            Button("Remove plug-in", role: .destructive) { installer.remove() }
        } message: {
            Text("Lightroom Classic will no longer load Lumerist. A copy is kept in the backup folder; install it again from here at any time.")
        }
    }

    private var statusDetail: String {
        if let problem = installer.bundleProblem { return problem }
        let lightroom = installer.isLightroomRunning ? " Lightroom Classic is open." : ""
        switch installer.status {
        case .notInstalled?: return "Not installed." + lightroom
        case .outdated(let installed, let bundled)?:
            return "Installed \(installed?.description ?? "unknown"); this app brings \(bundled)." + lightroom
        case .modified(let version)?: return "Version \(version), but the files differ from this app's copy." + lightroom
        case .upToDate(let version)?: return "Version \(version) installed."
        case .newerInstalled(let installed, let bundled)?:
            return "A newer plug-in (\(installed)) is installed than this app's (\(bundled))."
        case nil: return "Checking…"
        }
    }

    @ViewBuilder
    private var statusPill: some View {
        if installer.isReady {
            StatusPill(text: "Installed", systemImage: "checkmark", tone: .good)
        } else if installer.status == .notInstalled {
            StatusPill(text: "Missing", systemImage: "xmark", tone: .off)
        } else if case .modified? = installer.status {
            StatusPill(text: "Changed", systemImage: "exclamationmark", tone: .waiting)
        } else if installer.status != nil {
            StatusPill(text: "Outdated", systemImage: "arrow.down", tone: .waiting)
        }
    }

    @ViewBuilder
    private var primaryAction: some View {
        if installer.bundleProblem == nil, installer.status?.needsAction == true {
            let label: String = switch installer.status {
            case .notInstalled?: "Install"
            case .modified?: "Reinstall"
            default: "Update"
            }
            if installer.isLightroomRunning {
                Button("Quit Lightroom & \(label.lowercased())") { Task { await installer.quitLightroomAndInstall() } }
                    .buttonStyle(.neonPrimary)
                    .disabled(installer.isWorking)
            } else {
                Button(label) { installer.install() }
                    .buttonStyle(.neonPrimary)
                    .disabled(installer.isWorking)
            }
        }
    }

    private var phaseText: (text: String, color: Color)? {
        switch installer.phase {
        case .idle: nil
        // Progress, not a warning: grey in Galerist (its amber is the REC red).
        case .working(let text): (text, Synth.isFlat ? Synth.steel : Synth.amber)
        case .finished(let text): (text, Synth.teal)
        case .failed(let text): (text, Synth.error)
        }
    }
}

/// General → "Updates": manual check, and the daily check at launch (on unless turned off).
struct UpdatesSection: View {
    @State private var checker = UpdateChecker.shared

    var body: some View {
        SettingsSection(title: "Updates", systemImage: "arrow.down.circle",
                        footer: "Lumerist never downloads or installs anything by itself. Checking asks GitHub for the list of releases; with automatic checks off, Lumerist talks to nothing but Lightroom on this Mac, unless you leave your email under Stay in touch.") {
            SettingRow(title: "Version \(SupportLinks.appVersion)", detail: stateDetail) {
                HStack(spacing: Synth.Space.s) {
                    if case .available = checker.state {
                        Button("Download…") { checker.openReleasePage() }
                            .buttonStyle(.neonPrimary)
                    }
                    Button(checker.state == .checking ? "Checking…" : "Check for updates") {
                        Task { await checker.check() }
                    }
                    .buttonStyle(.neonSecondary)
                    .disabled(checker.state == .checking)
                }
            }
            SettingRow(title: "Check automatically once a day", detail: "When Lumerist starts. A new version shows in the menu bar.") {
                Toggle("Check automatically once a day", isOn: Binding(
                    get: { checker.checksAutomatically },
                    set: { checker.checksAutomatically = $0 }
                ))
                .labelsHidden()
                .toggleStyle(.neon)
            }
        }
    }

    private var stateDetail: String {
        switch checker.state {
        case .idle:
            return checker.lastCheck.map { "Last checked \($0.formatted(.relative(presentation: .named)))." } ?? "Not checked yet."
        case .checking: return "Asking GitHub…"
        case .upToDate(let version): return "Lumerist \(version) is the newest release."
        case .available(let version, _): return "Lumerist \(version) is available on GitHub."
        case .noReleases: return "No release has been published yet."
        case .failed(let message): return message
        }
    }
}

/// General → "About & Support": project links, the maker's credit line and
/// Galerist link, donations, "Stay in touch", licences.
struct AboutSupportSection: View {
    var stayInTouch: StayInTouchPrompter = .shared

    var body: some View {
        SettingsSection(title: "About & Support", systemImage: "heart",
                        footer: "Lumerist is free and open source (GPL-3.0). Donations are gifts, never payments: every feature stays free. Adobe, Lightroom and Lightroom Classic are trademarks of Adobe; Lumerist is not affiliated with Adobe.") {
            SettingRow(title: "Lumerist \(SupportLinks.appVersion)",
                       detail: "Made by a photographer who wanted faster editing in Lightroom Classic. Ideas and bug reports are welcome.") {
                HStack(spacing: Synth.Space.s) {
                    link("GitHub", SupportLinks.repository)
                    link("Report a bug", SupportLinks.issues)
                    link("Ideas", SupportLinks.discussions)
                }
            }
            SettingRow(title: "Galerist", detail: GaleristOptIn.Copy.credit) {
                link("Visit Galerist", SupportLinks.galeristFromSettings)
                    .help(SupportLinks.galeristFromSettings.absoluteString)
            }
            // Text above, buttons below: several buttons beside the text would
            // squeeze it at the minimum window width.
            VStack(alignment: .leading, spacing: Synth.Space.s) {
                Text("Buy me a coffee — or a pizza")
                    .synthBody(13, weight: .semibold, color: Synth.ice)
                Text("If Lumerist saves you time, a small gift keeps it growing.")
                    .synthBody(12, color: Synth.steel)
                HStack(spacing: Synth.Space.s) {
                    ForEach(SupportLinks.availableDonations) { donation in
                        Button {
                            if let url = donation.url { NSWorkspace.shared.open(url) }
                        } label: {
                            Label(donation.title, systemImage: donation.systemImage)
                        }
                        .buttonStyle(.neonSecondary)
                        .help(donation.detail)
                    }
                }
                .padding(.top, 2)
            }
            .padding(.horizontal, Synth.rowInset)
            .padding(.vertical, 12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .overlay(alignment: .bottom) {
                Rectangle().fill(Synth.rowDivider).frame(height: 1)
            }
            // The same sheet as the occasional prompt, opened on request.
            SettingRow(title: GaleristOptIn.Copy.title, detail: GaleristOptIn.Copy.aboutDetail) {
                if let date = stayInTouch.subscribedAt {
                    VStack(alignment: .trailing, spacing: 4) {
                        StatusPill(text: GaleristOptIn.Copy.aboutSubscribed, systemImage: "checkmark", tone: .good)
                        Text("Since \(date.formatted(date: .abbreviated, time: .omitted))")
                            .synthBody(11, color: Synth.steel)
                    }
                    .accessibilityElement(children: .combine)
                } else {
                    Button(GaleristOptIn.Copy.aboutButton) { stayInTouch.presentFromAbout() }
                        .buttonStyle(.neonSecondary)
                }
            }
            SettingRow(title: "Licences",
                       detail: "Lumerist: GNU GPL v3. Fonts Chakra Petch, Outfit, Inter and JetBrains Mono: SIL Open Font License 1.1 (included in the app).") {
                link("GPL-3.0", SupportLinks.license)
            }
        }
    }

    private func link(_ title: String, _ url: URL) -> some View {
        Button(title) { NSWorkspace.shared.open(url) }
            .buttonStyle(.neonQuiet)
    }
}
