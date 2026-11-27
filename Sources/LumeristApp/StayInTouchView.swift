import AppKit
import SwiftUI
import LumeristCore

/// Decides when the "Stay in touch" sheet appears on the main window and keeps
/// its schedule (`StayInTouchSchedule`) in the app's UserDefaults.
///
/// The sheet only appears while the user is looking at the main window: it is
/// visible and key, this app is frontmost (so Lightroom is not), the setup
/// guide is finished and closed. A window that only flashed at launch (started
/// hidden, or by Lightroom) never gets it: every chance waits `settleDelay`
/// and checks again. Nothing is sent until the user types an email and
/// presses "Keep me posted".
@MainActor
@Observable
final class StayInTouchPrompter {
    static let shared = StayInTouchPrompter()

    /// The sheet on the main window, if any.
    var presented: StayInTouchModel?
    private(set) var schedule: StayInTouchSchedule
    /// nil in snapshots: nothing is read, saved or presented.
    @ObservationIgnored private let defaults: UserDefaults?
    @ObservationIgnored private var pendingCheck: Task<Void, Never>?
    /// At most one sheet per launch: once it has been on screen (scheduled or
    /// opened from About & Support), no scheduled prompt follows until the
    /// next launch.
    @ObservationIgnored private var shownThisLaunch = false

    /// How long the main window must stay in front before a scheduled prompt.
    static let settleDelay: Duration = .milliseconds(1200)

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        schedule = StayInTouchSchedule(reading: { defaults.object(forKey: $0) })
    }

    /// Snapshots only: a schedule in a given state; it never saves or presents.
    init(preview schedule: StayInTouchSchedule) {
        defaults = nil
        self.schedule = schedule
    }

    var subscribedAt: Date? { schedule.subscribedAt }

    /// A chance to ask: the main window appeared or became key, the app became
    /// active, or the setup guide closed.
    func considerPrompting() {
        guard defaults != nil, !shownThisLaunch, presented == nil, pendingCheck == nil,
              schedule.isDue(at: .now) else { return }
        pendingCheck = Task { [weak self] in
            try? await Task.sleep(for: Self.settleDelay)
            guard !Task.isCancelled, let self else { return }
            self.pendingCheck = nil
            self.promptIfDue()
        }
    }

    private func promptIfDue() {
        let now = Date()
        guard !shownThisLaunch, presented == nil, schedule.isDue(at: now), let number = schedule.nextPrompt,
              Self.userIsLookingAtMainWindow else { return }
        // Counted as soon as it appears: "Not now" and closing count too.
        schedule.recordShown(at: now)
        save()
        shownThisLaunch = true
        presented = StayInTouchModel(prompt: .scheduled(number), prompter: self)
    }

    /// The main window is visible, key and not minimised; this app is the
    /// frontmost one (never over Lightroom); the setup guide is done and
    /// closed; and nothing else is going on in the window (another sheet, a
    /// key being recorded).
    static var userIsLookingAtMainWindow: Bool {
        guard !Onboarding.shouldShowAtLaunch, !Onboarding.isOpen,
              NSApp.isActive,
              NSWorkspace.shared.frontmostApplication?.processIdentifier == ProcessInfo.processInfo.processIdentifier,
              let window = MainWindow.window,
              window.isVisible, window.isKeyWindow, !window.isMiniaturized,
              window.attachedSheet == nil,
              ChordRecorder.shared.recordingID == nil
        else { return false }
        return true
    }

    /// About & Support: the same sheet, on request. Never counted.
    func presentFromAbout() {
        guard defaults != nil else { return }
        pendingCheck?.cancel()
        pendingCheck = nil
        shownThisLaunch = true
        presented = StayInTouchModel(prompt: .about, prompter: self)
    }

    /// A sign-up the server confirmed: only the date is kept, never the email.
    func didSubscribe(at date: Date) {
        schedule.recordSubscribed(at: date)
        save()
    }

    /// The sheet closed, however it closed ("Not now", Esc, the window).
    /// "Don't ask me again" ticked on a scheduled prompt means never again.
    func closed(_ model: StayInTouchModel) {
        if model.dontAskAgain, model.offersDontAskAgain, !model.isSubscribed {
            schedule.stopAsking()
            save()
        }
        if presented === model { presented = nil }
    }

    private func save() {
        guard let defaults else { return }
        schedule.write { key, value in defaults.set(value, forKey: key) }
    }
}

/// One showing of the sheet: what the user typed, and the request's state.
@MainActor
@Observable
final class StayInTouchModel: Identifiable {
    enum Phase: Equatable {
        case editing
        case sending
        case failed(String)
        case subscribed(Date)
    }

    let prompt: GaleristSubscription.Prompt
    /// What the user typed; emptied after a confirmed sign-up.
    var form = GaleristSignUpForm()
    /// Unticked when the sheet opens.
    var dontAskAgain = false
    private(set) var phase: Phase = .editing
    @ObservationIgnored private weak var prompter: StayInTouchPrompter?
    @ObservationIgnored private let isPreview: Bool

    init(prompt: GaleristSubscription.Prompt, prompter: StayInTouchPrompter) {
        self.prompt = prompt
        self.prompter = prompter
        isPreview = false
    }

    /// Snapshots only: a sheet in a given state; it never sends or saves.
    init(preview phase: Phase, prompt: GaleristSubscription.Prompt = .scheduled(1), email: String = "",
         dontAskAgain: Bool = false) {
        self.prompt = prompt
        self.phase = phase
        form = GaleristSignUpForm(email: email)
        self.dontAskAgain = dontAskAgain
        isPreview = true
    }

    var isSending: Bool { phase == .sending }

    var isSubscribed: Bool {
        if case .subscribed = phase { true } else { false }
    }

    var canSubmit: Bool { form.emailLooksValid && !isSending && !isSubscribed }

    /// "Don't ask me again" belongs to the scheduled prompts, not to the
    /// sheet the user opened from About & Support.
    var offersDontAskAgain: Bool {
        if case .scheduled = prompt { true } else { false }
    }

    /// The one network call, from "Keep me posted" or Return in the field.
    func submit() {
        guard !isPreview, !isSending, !isSubscribed else { return }
        switch form.submission(environment: ProcessInfo.processInfo.environment,
                               preferredLanguages: Locale.preferredLanguages,
                               appVersion: SupportLinks.appVersion,
                               prompt: prompt) {
        case .refuse(let outcome):
            fail(outcome)
        case .send(let base, let subscription):
            let client = GaleristSubscriptionClient(baseURL: base, appVersion: SupportLinks.appVersion)
            phase = .sending
            Task {
                let outcome = await client.subscribe(subscription)
                if outcome == .subscribed {
                    let now = Date()
                    // Recorded even if the sheet was closed meanwhile.
                    prompter?.didSubscribe(at: now)
                    form.reset()
                    phase = .subscribed(now)
                    announce(outcome.message)
                } else {
                    fail(outcome)
                }
            }
        }
    }

    func clearFailure() {
        if case .failed = phase { phase = .editing }
    }

    func sheetClosed() {
        guard !isPreview else { return }
        prompter?.closed(self)
    }

    private func fail(_ outcome: GaleristSubscriptionOutcome) {
        phase = .failed(outcome.message)
        announce(outcome.message)
    }

    /// VoiceOver hears the result even though the sheet changes under it.
    private func announce(_ message: String) {
        AccessibilityNotification.Announcement(message).post()
    }
}

/// The "Stay in touch" sheet: one email field, a one-line notice with the
/// Privacy link, "Not now" and "Keep me posted". Return sends, Esc is "Not now".
struct StayInTouchSheet: View {
    let model: StayInTouchModel

    @Environment(\.dismiss) private var dismiss
    @Environment(\.isSnapshotRendering) private var isSnapshotRendering
    @FocusState private var emailFocused: Bool
    @State private var autoClose: Task<Void, Never>?

    private typealias Copy = GaleristOptIn.Copy

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            if case .subscribed = model.phase {
                success
            } else {
                form
            }
        }
        .padding(.horizontal, 28)
        .padding(.top, 26)
        .padding(.bottom, 24)
        .frame(width: 480, alignment: .leading)
        .background(Synth.isFlat ? Synth.panel : Synth.abyss)
        .synthColorScheme()
        .tint(Synth.teal)
        .onAppear {
            // After the sheet is on screen, or the focus doesn't stick.
            Task { @MainActor in emailFocused = true }
        }
        .onChange(of: model.phase) { phaseChanged() }
        .onChange(of: model.form.email) { model.clearFailure() }
        .onDisappear {
            autoClose?.cancel()
            model.sheetClosed()
        }
    }

    // MARK: Parts

    private var header: some View {
        HStack(alignment: .center, spacing: 14) {
            badge
            Text(Copy.title)
                .synthTitle(Synth.isFlat ? 24 : 20)
                .accessibilityAddTraits(.isHeader)
        }
        .padding(.bottom, 14)
    }

    /// An envelope in a hairline square (Galerist) or a teal ring (Miami).
    private var badge: some View {
        Image(systemName: "envelope")
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

    @ViewBuilder
    private var form: some View {
        @Bindable var model = model
        Text(Copy.message)
            .synthBody(13.5, color: Synth.body)
            .fixedSize(horizontal: false, vertical: true)
            .padding(.bottom, Synth.Space.l)

        emailField($model.form.email)

        if case .failed(let message) = model.phase {
            Label(message, systemImage: "exclamationmark.circle")
                .font(SynthFont.font(.text, size: 12))
                .foregroundStyle(Synth.error)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, Synth.Space.s)
        }

        Text(noticeWithPrivacyLink)
            .font(SynthFont.font(.text, size: 11.5))
            .foregroundStyle(Synth.steel)
            .tint(Synth.teal)
            .fixedSize(horizontal: false, vertical: true)
            .padding(.top, 10)

        HStack(spacing: Synth.Space.m) {
            if model.offersDontAskAgain {
                Toggle(isOn: $model.dontAskAgain) { Text(Copy.dontAskAgain) }
                    .toggleStyle(SynthCheckboxStyle())
                    .fixedSize()
            }
            Spacer(minLength: Synth.Space.s)
            if model.isSending {
                if isSnapshotRendering {
                    Image(systemName: "circle.dotted").foregroundStyle(Synth.steel) // ImageRenderer can't draw ProgressView
                } else {
                    ProgressView().controlSize(.small)
                        .accessibilityLabel("Sending")
                }
            }
            Button(Copy.notNow) { dismiss() }
                .buttonStyle(.neonSecondary)
                .keyboardShortcut(.cancelAction)
                .fixedSize()
            Button(Copy.button) { model.submit() }
                .buttonStyle(.neonPrimary)
                .keyboardShortcut(.defaultAction)
                .disabled(!model.canSubmit)
                .fixedSize()
        }
        .padding(.top, 22)
    }

    @ViewBuilder
    private func emailField(_ email: Binding<String>) -> some View {
        Group {
            if isSnapshotRendering {
                // ImageRenderer can't draw the AppKit text field: same font
                // and frame, as plain text.
                Text(email.wrappedValue.isEmpty ? Copy.emailPlaceholder : email.wrappedValue)
                    .font(SynthFont.font(.text, size: 14))
                    .foregroundStyle(email.wrappedValue.isEmpty ? Synth.disabled : Synth.ice)
                    .frame(maxWidth: .infinity, alignment: .leading)
            } else {
                TextField("Email address", text: email,
                          prompt: Text(Copy.emailPlaceholder).foregroundStyle(Synth.disabled))
                    .textFieldStyle(.plain)
                    .font(SynthFont.font(.text, size: 14))
                    .foregroundStyle(Synth.ice)
                    .autocorrectionDisabled()
                    .focused($emailFocused)
                    // Return sends (the button's default action does too;
                    // the model ignores a second call while sending).
                    .onSubmit { model.submit() }
                    .disabled(model.isSending)
                    .accessibilityLabel("Email address")
            }
        }
        .padding(.horizontal, 12)
        .frame(height: 36)
        .background(RoundedRectangle(cornerRadius: Synth.Radius.control)
            .fill(Synth.isFlat ? Synth.panelRaised : Synth.panel))
        .overlay(
            RoundedRectangle(cornerRadius: Synth.Radius.control)
                .strokeBorder(fieldBorder, lineWidth: 1)
        )
    }

    /// The message under the field says what went wrong; the border stays
    /// quiet (Galerist keeps its red for few things).
    private var fieldBorder: Color {
        emailFocused ? Synth.teal : Synth.outline
    }

    private var success: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 10) {
                Image(systemName: "checkmark.circle.fill")
                    .font(.system(size: 18))
                    .foregroundStyle(Synth.success)
                    .accessibilityHidden(true)
                Text(Copy.success)
                    .synthBody(15, weight: .semibold, color: Synth.ice)
            }
            .accessibilityElement(children: .combine)
            HStack {
                Spacer()
                Button(Copy.done) { dismiss() }
                    .buttonStyle(.neonPrimary)
                    .keyboardShortcut(.defaultAction)
            }
            .padding(.top, 22)
        }
    }

    /// The notice, then the Privacy link (opened in the browser by SwiftUI).
    private var noticeWithPrivacyLink: AttributedString {
        var text = AttributedString(Copy.notice + " ")
        var link = AttributedString(Copy.privacyLink)
        link.link = SupportLinks.galeristPrivacy
        link.foregroundColor = Synth.teal
        link.underlineStyle = .single
        text.append(link)
        return text
    }

    private func phaseChanged() {
        switch model.phase {
        case .failed:
            // Back to the field (it was disabled while sending).
            emailFocused = true
        case .subscribed:
            // Closes by itself, unless VoiceOver is reading: then "Done".
            guard !isSnapshotRendering, !NSWorkspace.shared.isVoiceOverEnabled else { return }
            autoClose = Task { @MainActor in
                try? await Task.sleep(for: .milliseconds(1500))
                if !Task.isCancelled { dismiss() }
            }
        case .editing, .sending:
            break
        }
    }
}

/// A Synth checkbox: unticked outline, magenta (Galerist: ink red) when
/// ticked; still a native Toggle for VoiceOver and the keyboard.
struct SynthCheckboxStyle: ToggleStyle {
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        let on = configuration.isOn
        return Button {
            configuration.isOn.toggle()
        } label: {
            HStack(alignment: .center, spacing: Synth.Space.s) {
                ZStack {
                    // Galerist: a square box, grey outline until ticked.
                    RoundedRectangle(cornerRadius: Synth.isFlat ? 0 : 3)
                        .fill(on && isEnabled ? Synth.magenta : (Synth.isFlat ? Synth.panel : Synth.abyss))
                    RoundedRectangle(cornerRadius: Synth.isFlat ? 0 : 3)
                        .strokeBorder(on && isEnabled ? Synth.magenta
                                      : (isEnabled ? (Synth.isFlat ? Synth.steel : Synth.teal.opacity(0.8)) : Synth.disabled),
                                      lineWidth: 1)
                    if on {
                        Image(systemName: "checkmark")
                            .font(.system(size: 9, weight: .heavy))
                            .foregroundStyle(isEnabled ? Synth.onMagenta : Synth.disabled)
                    }
                }
                .frame(width: 15, height: 15)
                configuration.label
                    .font(SynthFont.font(.text, size: 12))
                    .foregroundStyle(isEnabled ? Synth.body : Synth.disabled)
                    .multilineTextAlignment(.leading)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .animation(Synth.Motion.quick, value: on)
        .accessibilityRepresentation {
            Toggle(isOn: configuration.$isOn) { configuration.label }
        }
    }
}

// MARK: - Snapshots

private struct SnapshotRenderingKey: EnvironmentKey {
    static let defaultValue = false
}

extension EnvironmentValues {
    /// True only in `OnboardingSnapshots` and `HUDSnapshots`: AppKit-backed
    /// controls (text field, spinner) are drawn as look-alikes, and tooltip
    /// anchors are left out, because ImageRenderer can't draw them.
    var isSnapshotRendering: Bool {
        get { self[SnapshotRenderingKey.self] }
        set { self[SnapshotRenderingKey.self] = newValue }
    }
}
