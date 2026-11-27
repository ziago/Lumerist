import SwiftUI
import LumeristCore

/// The update window: "Lumerist X is out", "You have Y.", what's new, then
/// "Update now", "Release page", "Later" and a small "Skip this version".
/// The release text is shown as plain text only (`Text(verbatim:)`): no
/// links, Markdown or HTML are rendered.
struct UpdatePromptView: View {
    let model: UpdatePromptModel
    @State private var installer = PluginInstaller.shared
    @Environment(\.isSnapshotRendering) private var isSnapshotRendering

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            if model.phase == .installed {
                installed
            } else {
                whatsNew
                status
                buttons
            }
        }
        .padding(.horizontal, 28)
        .padding(.top, 30)
        .padding(.bottom, 24)
        .frame(width: 540, alignment: .leading)
        .background(Synth.isFlat ? Synth.panel : Synth.abyss)
        .tint(Synth.teal)
    }

    // MARK: Parts

    private var header: some View {
        HStack(alignment: .center, spacing: 14) {
            badge
            VStack(alignment: .leading, spacing: 3) {
                Text(verbatim: model.phase == .installed ? UpdateCopy.installedTitle(model.version) : UpdateCopy.title(model.version))
                    .synthTitle(Synth.isFlat ? 24 : 20)
                    .accessibilityAddTraits(.isHeader)
                if model.phase != .installed, let current = model.current {
                    Text(verbatim: UpdateCopy.current(current)).synthBody(13, color: Synth.steel)
                }
            }
        }
        .padding(.bottom, 18)
    }

    /// An arrow (a tick once installed) in a hairline square (Galerist) or a teal ring (Miami).
    private var badge: some View {
        Image(systemName: model.phase == .installed ? "checkmark" : "arrow.down")
            .font(.system(size: 16, weight: .medium))
            .foregroundStyle(Synth.teal)
            .frame(width: 40, height: 40)
            .background {
                if Synth.isFlat {
                    Rectangle().strokeBorder(Synth.gridLine, lineWidth: 1)
                } else {
                    Circle().strokeBorder(Synth.teal.opacity(0.8), lineWidth: 1)
                        .modifier(Synth.glow(Synth.teal, radius: 8, opacity: 0.35))
                }
            }
            .accessibilityHidden(true)
    }

    private var whatsNew: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(UpdateCopy.whatsNew).synthLabel(10)
            if model.highlights.isEmpty {
                Text(verbatim: UpdateHighlights.fallback).synthBody(13.5, color: Synth.body)
            } else {
                ForEach(Array(model.highlights.enumerated()), id: \.offset) { _, line in
                    HStack(alignment: .firstTextBaseline, spacing: 10) {
                        Rectangle().fill(Synth.isFlat ? Synth.ice : Synth.teal)
                            .frame(width: 5, height: 5)
                            .offset(y: -2)
                            .accessibilityHidden(true)
                        Text(verbatim: line)
                            .synthBody(13.5, color: Synth.body)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
            }
        }
        .padding(.vertical, 14)
        .padding(.horizontal, 16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: Synth.Radius.container).fill(Synth.isFlat ? Synth.abyss : Synth.panel))
        .overlay(RoundedRectangle(cornerRadius: Synth.Radius.container)
            .strokeBorder(Synth.isFlat ? Synth.gridLine : Synth.outline, lineWidth: 1))
    }

    @ViewBuilder
    private var status: some View {
        switch model.phase {
        case .offer:
            if let blocker = model.blocker {
                note(blocker, systemImage: "info.circle", color: Synth.steel)
            }
        case .working(let step):
            VStack(alignment: .leading, spacing: 8) {
                switch step {
                case .downloading(let received, let total):
                    progressBar(total > 0 ? Double(received) / Double(total) : 0)
                    Text(verbatim: UpdateCopy.downloading(received, of: total)).synthLabel(10)
                case .checking:
                    progressBar(nil)
                    Text(UpdateCopy.checking).synthLabel(10)
                case .installing:
                    progressBar(nil)
                    Text(UpdateCopy.installing).synthLabel(10)
                case .restarting:
                    progressBar(1)
                    Text(UpdateCopy.restarting).synthLabel(10)
                }
            }
            .padding(.top, 16)
        case .failed(let failure):
            VStack(alignment: .leading, spacing: 4) {
                Label {
                    Text(verbatim: failure.headline).synthBody(13, weight: .semibold, color: Synth.error)
                } icon: {
                    Image(systemName: "exclamationmark.circle").foregroundStyle(Synth.error)
                }
                Text(verbatim: failure.detail + (failure.kind == .notRestored ? "" : " " + UpdateCopy.unchanged))
                    .synthBody(12, color: Synth.steel)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.leading, 22)
            }
            .padding(.top, 14)
        case .installed:
            EmptyView()
        }
    }

    private func note(_ text: String, systemImage: String, color: Color) -> some View {
        Label {
            Text(verbatim: text).synthBody(12, color: color).fixedSize(horizontal: false, vertical: true)
        } icon: {
            Image(systemName: systemImage).foregroundStyle(color)
        }
        .padding(.top, 14)
    }

    /// A thin bar: the share received, or a short moving segment while checking.
    private func progressBar(_ fraction: Double?) -> some View {
        GeometryReader { geometry in
            ZStack(alignment: .leading) {
                Rectangle().fill(Synth.gridLine)
                if let fraction {
                    Rectangle().fill(Synth.isFlat ? Synth.ice : Synth.teal)
                        .frame(width: geometry.size.width * min(max(fraction, 0), 1))
                } else if isSnapshotRendering {
                    Rectangle().fill(Synth.isFlat ? Synth.ice : Synth.teal).frame(width: geometry.size.width * 0.3)
                } else {
                    IndeterminateSegment(width: geometry.size.width)
                }
            }
        }
        .frame(height: 3)
        .accessibilityElement()
        .accessibilityLabel("Progress")
        .accessibilityValue(fraction.map { "\(Int($0 * 100)) percent" } ?? "Working")
    }

    @ViewBuilder
    private var buttons: some View {
        HStack(spacing: Synth.Space.m) {
            switch model.phase {
            case .offer:
                Button(UpdateCopy.skip) { model.skip() }
                    .buttonStyle(SkipButtonStyle())
                    .fixedSize()
                Spacer(minLength: Synth.Space.s)
                Button(UpdateCopy.later) { model.later() }
                    .buttonStyle(.neonSecondary)
                    .keyboardShortcut(.cancelAction)
                    .fixedSize()
                if model.canUpdateNow {
                    Button(UpdateCopy.releasePage) { model.releasePage() }
                        .buttonStyle(.neonSecondary)
                        .fixedSize()
                    Button(UpdateCopy.updateNow) { model.updateNow() }
                        .buttonStyle(.neonPrimary)
                        .keyboardShortcut(.defaultAction)
                        .fixedSize()
                } else {
                    Button(UpdateCopy.releasePage) { model.releasePage() }
                        .buttonStyle(.neonPrimary)
                        .keyboardShortcut(.defaultAction)
                        .fixedSize()
                }
            case .working:
                Spacer()
                Button(UpdateCopy.cancel) { model.cancel() }
                    .buttonStyle(.neonSecondary)
                    .keyboardShortcut(.cancelAction)
                    .disabled(!model.canCancel)
                    .fixedSize()
            case .failed(let failure):
                Spacer()
                Button(UpdateCopy.close) { model.close() }
                    .buttonStyle(.neonSecondary)
                    .keyboardShortcut(.cancelAction)
                    .fixedSize()
                if failure.canRetry || failure.kind == .cancelled {
                    Button(UpdateCopy.tryAgain) { model.updateNow() }
                        .buttonStyle(.neonSecondary)
                        .fixedSize()
                }
                Button(UpdateCopy.releasePage) { model.releasePage() }
                    .buttonStyle(.neonPrimary)
                    .keyboardShortcut(.defaultAction)
                    .fixedSize()
            case .installed:
                EmptyView()
            }
        }
        .padding(.top, 22)
    }

    /// In the new version: whether Lightroom still has the old plug-in, and the offer to fix that.
    private var installed: some View {
        let waits = model.previewPluginWaits
            ?? (installer.status?.updatesAutomatically == true && installer.isLightroomRunning)
        return VStack(alignment: .leading, spacing: 0) {
            Text(verbatim: waits ? UpdateCopy.pluginWaits : UpdateCopy.keysKept)
                .synthBody(13.5, color: Synth.body)
                .fixedSize(horizontal: false, vertical: true)
            if case .working(let text) = installer.phase {
                Text(verbatim: text).synthBody(12, color: Synth.steel).padding(.top, 8)
            } else if case .failed(let text) = installer.phase {
                Text(verbatim: text).synthBody(12, color: Synth.error).padding(.top, 8)
            }
            HStack(spacing: Synth.Space.m) {
                Spacer()
                if waits {
                    Button(UpdateCopy.later) { model.close() }
                        .buttonStyle(.neonSecondary)
                        .keyboardShortcut(.cancelAction)
                        .fixedSize()
                    Button(UpdateCopy.quitLightroom) { Task { await installer.quitLightroomAndInstall() } }
                        .buttonStyle(.neonPrimary)
                        .keyboardShortcut(.defaultAction)
                        .disabled(installer.isWorking)
                        .fixedSize()
                } else {
                    Button(UpdateCopy.done) { model.close() }
                        .buttonStyle(.neonPrimary)
                        .keyboardShortcut(.defaultAction)
                        .fixedSize()
                }
            }
            .padding(.top, 22)
        }
    }
}

/// "Skip this version": small body text, no box, so it reads as the minor choice.
private struct SkipButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(SynthFont.font(.text, size: 12))
            .foregroundStyle(configuration.isPressed ? Synth.ice : Synth.steel)
            .underline(Synth.isFlat)
            .frame(minHeight: 26)
            .contentShape(Rectangle())
    }
}

/// A segment sliding along the bar while the length of the work is unknown.
private struct IndeterminateSegment: View {
    let width: CGFloat
    @State private var moving = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Rectangle().fill(Synth.isFlat ? Synth.ice : Synth.teal)
            .frame(width: width * 0.3)
            .offset(x: reduceMotion ? width * 0.35 : (moving ? width * 0.7 : 0))
            .onAppear {
                guard !reduceMotion else { return }
                withAnimation(.easeInOut(duration: 0.9).repeatForever(autoreverses: true)) { moving = true }
            }
    }
}
