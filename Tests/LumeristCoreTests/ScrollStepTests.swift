import Foundation
import Testing
@testable import LumeristCore

struct ScrollStepAccumulatorTests {
    @Test func notchedWheelLinesMapOneToOne() {
        var accumulator = ScrollStepAccumulator()
        #expect(accumulator.steps(lineDelta: 1, pointDelta: 10, isContinuous: false, isMomentum: false) == 1)
        #expect(accumulator.steps(lineDelta: -1, pointDelta: -10, isContinuous: false, isMomentum: false) == -1)
        #expect(accumulator.steps(lineDelta: 3, pointDelta: 30, isContinuous: false, isMomentum: false) == 3)
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 0, isContinuous: false, isMomentum: false) == 0)
    }

    @Test func notchedWheelClampsAtTenStepsPerEvent() {
        var accumulator = ScrollStepAccumulator()
        #expect(accumulator.steps(lineDelta: 25, pointDelta: 250, isContinuous: false, isMomentum: false) == 10)
        #expect(accumulator.steps(lineDelta: -25, pointDelta: -250, isContinuous: false, isMomentum: false) == -10)
    }

    @Test func fractionalNotchNeverLosesAStep() {
        var accumulator = ScrollStepAccumulator()
        #expect(accumulator.steps(lineDelta: 0.4, pointDelta: 4, isContinuous: false, isMomentum: false) == 1)
        #expect(accumulator.steps(lineDelta: -0.4, pointDelta: -4, isContinuous: false, isMomentum: false) == -1)
        #expect(accumulator.steps(lineDelta: .nan, pointDelta: 4, isContinuous: false, isMomentum: false) == 0)
    }

    @Test func continuousScrollAccumulatesTwelvePointsPerStep() {
        #expect(ScrollStepAccumulator.pointsPerStep == 12)
        var accumulator = ScrollStepAccumulator()
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 5, isContinuous: true, isMomentum: false) == 0)
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 5, isContinuous: true, isMomentum: false) == 0)
        // 15 points: one step, 3 points kept.
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 5, isContinuous: true, isMomentum: false) == 1)
        // 3 + 9 = 12: exactly one more step.
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 9, isContinuous: true, isMomentum: false) == 1)
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 11, isContinuous: true, isMomentum: false) == 0)
    }

    @Test func continuousScrollClampsLargeSwipes() {
        var accumulator = ScrollStepAccumulator()
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 600, isContinuous: true, isMomentum: false) == 10)
    }

    @Test func directionReversalDiscardsTheResidual() {
        var accumulator = ScrollStepAccumulator()
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 10, isContinuous: true, isMomentum: false) == 0)
        // Without a reset, 10 - 5 would leave +5; reversal starts again from -5.
        #expect(accumulator.steps(lineDelta: 0, pointDelta: -5, isContinuous: true, isMomentum: false) == 0)
        #expect(accumulator.steps(lineDelta: 0, pointDelta: -7, isContinuous: true, isMomentum: false) == -1)
    }

    @Test func momentumIsIgnoredAndKeepsTheResidual() {
        var accumulator = ScrollStepAccumulator()
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 10, isContinuous: true, isMomentum: false) == 0)
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 500, isContinuous: true, isMomentum: true) == 0)
        #expect(accumulator.steps(lineDelta: 5, pointDelta: 50, isContinuous: false, isMomentum: true) == 0)
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 2, isContinuous: true, isMomentum: false) == 1)
    }

    @Test func sensitivityChangesTheStepSizeWithinItsRange() {
        var accumulator = ScrollStepAccumulator()
        accumulator.pointsPerStep = 6
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 12, isContinuous: true, isMomentum: false) == 2)
        accumulator.pointsPerStep = 1
        #expect(accumulator.pointsPerStep == ScrollStepAccumulator.pointsPerStepRange.lowerBound)
    }

    @Test func resetDiscardsTheResidual() {
        var accumulator = ScrollStepAccumulator()
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 10, isContinuous: true, isMomentum: false) == 0)
        accumulator.reset()
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 2, isContinuous: true, isMomentum: false) == 0)
    }
}
