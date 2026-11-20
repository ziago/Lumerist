import Foundation
import Testing
@testable import LumeristCore

/// When the "Stay in touch" prompt may appear: never once subscribed or after
/// "Don't ask me again"; #1 at once; #2 ≥ 3 days after #1; #3 ≥ 14 days
/// after #2; never more than 3.
struct StayInTouchScheduleTests {
    private let start = Date(timeIntervalSince1970: 1_800_000_000)
    private let day: TimeInterval = 86_400

    private func at(days: Double) -> Date { start.addingTimeInterval(days * day) }

    @Test func asksAtTheFirstOpportunity() {
        let schedule = StayInTouchSchedule()
        #expect(schedule.nextPrompt == 1)
        #expect(schedule.isDue(at: start))
        #expect(schedule.isDue(at: .distantPast))
    }

    @Test func waitsThreeDaysBeforeTheSecondPrompt() {
        var schedule = StayInTouchSchedule()
        schedule.recordShown(at: start)
        #expect(schedule.timesShown == 1)
        #expect(schedule.lastShown == start)
        #expect(schedule.nextPrompt == 2)
        #expect(!schedule.isDue(at: start))
        #expect(!schedule.isDue(at: at(days: 3).addingTimeInterval(-1)))
        #expect(schedule.isDue(at: at(days: 3)))
        #expect(schedule.nextDate == at(days: 3))
    }

    @Test func waitsFourteenDaysAfterTheSecondPromptBeforeTheThird() {
        var schedule = StayInTouchSchedule()
        schedule.recordShown(at: start)
        schedule.recordShown(at: at(days: 5))
        #expect(schedule.nextPrompt == 3)
        #expect(!schedule.isDue(at: at(days: 5 + 14).addingTimeInterval(-1)))
        #expect(schedule.isDue(at: at(days: 5 + 14)))
        #expect(schedule.nextDate == at(days: 19))
    }

    @Test func neverAsksMoreThanThreeTimes() {
        var schedule = StayInTouchSchedule()
        schedule.recordShown(at: start)
        schedule.recordShown(at: at(days: 3))
        schedule.recordShown(at: at(days: 17))
        #expect(schedule.timesShown == 3)
        #expect(schedule.nextPrompt == nil)
        #expect(schedule.nextDate == nil)
        #expect(!schedule.isDue(at: at(days: 10_000)))
        // A fourth showing is not recorded.
        schedule.recordShown(at: at(days: 100))
        #expect(schedule.timesShown == 3)
        #expect(schedule.lastShown == at(days: 17))
    }

    @Test func neverAsksASubscribedUser() {
        var schedule = StayInTouchSchedule()
        schedule.recordSubscribed(at: start)
        #expect(schedule.isSubscribed)
        #expect(schedule.nextPrompt == nil)
        #expect(!schedule.isDue(at: at(days: 365)))
        // Subscribed after the first prompt: no second one.
        var later = StayInTouchSchedule()
        later.recordShown(at: start)
        later.recordSubscribed(at: at(days: 1))
        #expect(!later.isDue(at: at(days: 30)))
    }

    @Test func dontAskAgainMeansNever() {
        var schedule = StayInTouchSchedule()
        schedule.recordShown(at: start)
        schedule.stopAsking()
        #expect(schedule.dontAskAgain)
        #expect(schedule.nextPrompt == nil)
        #expect(!schedule.isDue(at: at(days: 365)))
    }

    @Test func aCountWithoutADateIsDue() {
        // Defaults edited by hand: the count still caps the prompts.
        let schedule = StayInTouchSchedule(timesShown: 1, lastShown: nil)
        #expect(schedule.nextPrompt == 2)
        #expect(schedule.isDue(at: start))
    }

    // MARK: Storage

    @Test func usesTheAppDomainKeysAndTheExistingSubscribedKey() {
        typealias Key = StayInTouchSchedule.Key
        #expect(Key.timesShown == "org.lumerist.stay-in-touch.times-shown")
        #expect(Key.lastShown == "org.lumerist.stay-in-touch.last-shown")
        #expect(Key.dontAskAgain == "org.lumerist.stay-in-touch.dont-ask-again")
        // The date the first sign-up form wrote: whoever subscribed then is never asked.
        #expect(Key.subscribedAt == "lumerist.galerist.subscribed-at")
        #expect(Key.subscribedAt == GaleristOptIn.subscribedAtDefaultsKey)
    }

    @Test func readsNothingAsNeverShown() {
        let schedule = StayInTouchSchedule(reading: { _ in nil })
        #expect(schedule == StayInTouchSchedule())
        #expect(schedule.isDue(at: start))
    }

    @Test func aUserWhoSubscribedWithTheFirstFormIsNeverAsked() {
        let stored: [String: Any] = ["lumerist.galerist.subscribed-at": start]
        let schedule = StayInTouchSchedule(reading: { stored[$0] })
        #expect(schedule.subscribedAt == start)
        #expect(!schedule.isDue(at: at(days: 1)))
    }

    @Test func writesAndReadsBackEveryField() {
        var stored: [String: Any] = [:]
        var schedule = StayInTouchSchedule()
        schedule.recordShown(at: start)
        schedule.stopAsking()
        schedule.write { key, value in stored[key] = value }
        #expect(Set(stored.keys) == Set(StayInTouchSchedule.Key.all).subtracting([StayInTouchSchedule.Key.subscribedAt]))
        #expect(StayInTouchSchedule(reading: { stored[$0] }) == schedule)

        schedule.recordSubscribed(at: at(days: 2))
        schedule.write { key, value in stored[key] = value }
        #expect(stored[StayInTouchSchedule.Key.subscribedAt] as? Date == at(days: 2))
        #expect(StayInTouchSchedule(reading: { stored[$0] }) == schedule)
    }

    @Test func toleratesMalformedValues() {
        let stored: [String: Any] = [
            StayInTouchSchedule.Key.timesShown: 99,
            StayInTouchSchedule.Key.lastShown: "yesterday",
            StayInTouchSchedule.Key.dontAskAgain: "yes",
        ]
        let schedule = StayInTouchSchedule(reading: { stored[$0] })
        #expect(schedule.timesShown == StayInTouchSchedule.maximumPrompts)
        #expect(schedule.lastShown == nil)
        #expect(!schedule.dontAskAgain)
        #expect(!schedule.isDue(at: start))
        let negative = StayInTouchSchedule(reading: { $0 == StayInTouchSchedule.Key.timesShown ? -4 : nil })
        #expect(negative.timesShown == 0)
        #expect(negative.nextPrompt == 1)
    }
}
