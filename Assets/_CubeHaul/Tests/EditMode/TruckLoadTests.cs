using System;
using CubeHaul.Gameplay.Roads;
using CubeHaul.Gameplay.Trucks;
using CubeHaul.Levels.Generation;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// Task 15 pieces under the session (docs/SESSION.md): JavaScript's Math.hypot, the loading animation of main.ts
    /// drawLoading, and drawing a truck between two simulation steps.
    /// </summary>
    public class TruckLoadTests
    {
        // Values and results printed by Node (V8) with 17 significant digits, which read back to the same doubles.
        [TestCase(3.0000000000000000, 4.0000000000000000, 5.0000000000000000)]
        [TestCase(0.0000000000000000, -7.5000000000000000, 7.5000000000000000)]
        [TestCase(9.9998886718268301e-321, 0.0000000000000000, 9.9998886718268301e-321)] // Math.Sqrt(x * x + y * y) differs
        [TestCase(0.0000000000000000, 0.0000000000000000, 0.0000000000000000)]
        [TestCase(62.061619386076927, -78.074264526367188, 99.735827976487144)]
        [TestCase(52.993392944335938, 198.35243225097656, 205.30949095361140)]
        [TestCase(71.784591674804688, 69.249343872070313, 99.742163745482699)] // Math.Sqrt(x * x + y * y) differs
        [TestCase(-130.92079162597656, 153.26882600784302, 201.57278265131637)]
        [TestCase(82.909536361694336, 131.01382255554199, 155.04390642758636)]
        [TestCase(95.767736434936523, -64.903306961059570, 115.68880065223682)]
        [TestCase(117.55843162536621, -123.11596870422363, 170.22786668520308)]
        [TestCase(31.948626041412354, -106.49518966674805, 111.18426205219266)]
        [TestCase(-116.42063856124878, 115.02940654754639, 163.66285300485762)]
        [TestCase(148.52256774902344, 137.59498596191406, 202.46316527364323)] // Math.Sqrt(x * x + y * y) differs
        [TestCase(-155.46455383300781, -11.845004558563232, 155.91514240601703)]
        [TestCase(92.533254623413086, -53.594636917114258, 106.93356964713516)]
        public void HypotIsV8s(double x, double y, double expected)
        {
            Assert.That(JsMath.Hypot(x, y), Is.EqualTo(expected));
            Assert.That(JsMath.Hypot(-x, -y), Is.EqualTo(expected));
            Assert.That(JsMath.Hypot(y, x), Is.EqualTo(expected), "symmetric");
        }

        [Test]
        public void HypotOfInfinityAndNaNIsJavaScripts()
        {
            Assert.That(JsMath.Hypot(double.NegativeInfinity, double.NaN), Is.EqualTo(double.PositiveInfinity));
            Assert.That(double.IsNaN(JsMath.Hypot(1, double.NaN)), Is.True);
        }

        [Test]
        public void TheCubeRocksLooseRisesArcsIntoTheBedAndLands()
        {
            const double cs = 10;
            CubeLoadFrame start = CubeLoad.At(0, cs);
            Assert.That((start.Along, start.Height, start.Rock, start.InFlight), Is.EqualTo((0.0, 0.0, 0.0, true)), "starts in its spot");
            // Rising: straight up, faster and faster, rocking.
            double last = 0;
            bool rocked = false;
            for (double t = 0.01; t < CubeLoad.LiftEnd; t += 0.01)
            {
                CubeLoadFrame f = CubeLoad.At(t, cs);
                Assert.That(f.Along, Is.Zero, $"t {t}: still over its cell");
                Assert.That(f.Height, Is.GreaterThan(last), $"t {t}: rising");
                rocked |= Math.Abs(f.Rock) > 0.01;
                last = f.Height;
            }
            Assert.That(rocked, Is.True);
            // The two phases meet: at LIFT_END it is cs·0.3 up and starts moving over.
            CubeLoadFrame lifted = CubeLoad.At(CubeLoad.LiftEnd, cs);
            Assert.That(lifted.Height, Is.EqualTo(cs * CubeLoad.Rise).Within(1e-12));
            Assert.That(CubeLoad.At(CubeLoad.LiftEnd - 1e-9, cs).Height, Is.EqualTo(lifted.Height).Within(1e-6));
            Assert.That(lifted.Along, Is.Zero);
            // The arc: over toward the bed, eased, higher than the lift, down into the bed.
            double peak = 0, along = 0;
            for (double t = CubeLoad.LiftEnd; t < CubeLoad.LandAt; t += 0.005)
            {
                CubeLoadFrame f = CubeLoad.At(t, cs);
                Assert.That(f.Along, Is.GreaterThanOrEqualTo(along), $"t {t}: never back");
                Assert.That(f.Rock, Is.Zero);
                Assert.That(f.InFlight, Is.True);
                along = f.Along;
                peak = Math.Max(peak, f.Height);
            }
            Assert.That(peak, Is.GreaterThan(cs * CubeLoad.Peak * 0.9), "it arcs well over the cubes");
            Assert.That(CubeLoad.At(CubeLoad.LandAt - 1e-9, cs).Height, Is.EqualTo(0).Within(1e-6), "and comes down into the bed");
            Assert.That(CubeLoad.At(CubeLoad.LandAt - 1e-9, cs).Along, Is.EqualTo(1).Within(1e-6));
            CubeLoadFrame inBed = CubeLoad.At(CubeLoad.LandAt, cs);
            Assert.That((inBed.Along, inBed.Height, inBed.InFlight), Is.EqualTo((1.0, 0.0, false)), "from LAND_AT on the truck's cargo shows");
            Assert.That(CubeLoad.At(1, cs).InFlight, Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => CubeLoad.At(double.NaN, cs));
        }

        [TestCase(0, 0)]
        [TestCase(Math.PI / 2, 0)]
        [TestCase(-Math.PI / 2, 0)]
        [TestCase(Math.PI, 0)]
        [TestCase(0.3, 0.3)]
        [TestCase(Math.PI / 2 + 0.2, 0.2)]
        [TestCase(-Math.PI + 0.1, 0.1)]
        [TestCase(Math.PI / 4 + 0.01, Math.PI / 4 + 0.01 - Math.PI / 2)]
        public void TheCubeTurnsTheShortWayToSitSquareInTheBed(double heading, double turn)
        {
            Assert.That(CubeLoad.AlignTurn(heading), Is.EqualTo(turn).Within(1e-12));
            Assert.That(Math.Abs(CubeLoad.AlignTurn(heading)), Is.LessThanOrEqualTo(Math.PI / 4 + 1e-12));
        }

        [Test]
        public void ATruckIsDrawnBetweenItsLastTwoSteps()
        {
            var (cs, fx, fy, ring) = RoadLayout.Geometry(10, 10);
            var start = RoadLayout.TunnelStart(1);
            var outward = new[] { RoadLayout.LaneEntry(ring, 1), new PointD(ring.X0, ring.Y1), new PointD(ring.X0, ring.Y0) };
            var home = new[] { outward[1], outward[0], start };
            var route = new TruckRoute(0, 1, start, outward, home, 0, 0, CubeHaul.Domain.Side.Left);
            var t = new TruckMotion(route, new PointD(fx, fy), cs, ring);
            t.Interpolate(0.5, out PointD p0, out double a0, out double f0);
            Assert.That((p0.X, p0.Y, a0, f0), Is.EqualTo((t.Pose().X, t.Pose().Y, t.Angle, t.Fade)), "not moved yet: where it is");
            for (int k = 0; k < 40; k++) t.Advance(1 / 60.0);
            double px = t.X, py = t.Y;
            t.Advance(1 / 60.0);
            t.Interpolate(1, out PointD now, out double angle, out double fade);
            Assert.That((now.X, now.Y, angle, fade), Is.EqualTo((t.Pose().X, t.Pose().Y, t.Angle, t.Fade)), "alpha 1: the current pose");
            Assert.That((t.PreviousX, t.PreviousY), Is.EqualTo((px, py)));
            t.Interpolate(0, out PointD before, out _, out _);
            t.Interpolate(0.5, out PointD half, out _, out _);
            Assert.That(half.X, Is.EqualTo((before.X + now.X) / 2).Within(0.05));
            Assert.That(half.Y, Is.EqualTo((before.Y + now.Y) / 2).Within(0.05));
            // Through the corner the heading blends the short way round.
            while (t.Step < 2) t.Advance(1 / 60.0);
            for (int k = 0; k < 10; k++)
            {
                t.Advance(1 / 60.0);
                t.Interpolate(0.5, out _, out double mid, out _);
                double lo = Math.Min(t.PreviousAngle, t.Angle) - 1e-12, hi = Math.Max(t.PreviousAngle, t.Angle) + 1e-12;
                Assert.That(mid, Is.InRange(lo, hi));
            }
        }
    }
}
