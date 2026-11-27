using System;
using System.Collections.Generic;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Layout;
using CubeHaul.Gameplay.Session;

namespace CubeHaul.Gameplay.Hud
{
    /// <summary>How a crate in the tray is drawn this frame (main.ts drawTrayStacks), design units.</summary>
    public struct TrayCrateLook
    {
        public bool Visible;
        public int Column, Row, Color, Count;
        /// <summary>Middle of the box horizontally, and its bottom edge (y down): it stretches and dips from the bottom.</summary>
        public double CenterX, Bottom;
        public double Width, Height;
        /// <summary>Opacity of the body and of the number (main.ts fades them a little differently).</summary>
        public double BodyAlpha, LabelAlpha;
        public double LabelSize;
        /// <summary>In the front row, settled: the one a tap takes. Front crates have a raised label, the rest a flat one.</summary>
        public bool Front;
        /// <summary>Press dip, 0..1 (scale 1 − 0.06·dip by 1 − 0.12·dip, 3·dip lower) and its flash.</summary>
        public double Dip;
    }

    /// <summary>What a slot shows this frame (main.ts drawSlots).</summary>
    public enum SlotShows
    {
        /// <summary>An empty bay.</summary>
        Empty,
        /// <summary>A crate is flying into it: drawn by the flight, the bay stays empty.</summary>
        Incoming,
        /// <summary>A crate sending trucks (or blocked).</summary>
        Crate,
        /// <summary>An emptied crate popping out.</summary>
        Leaving,
    }

    public struct SlotLook
    {
        public SlotShows Shows;
        /// <summary>Where the slot's crate is drawn: the bay, shaken sideways while a tap is refused.</summary>
        public RectD Rect;
        public int Color, Count;
        /// <summary>Trucks still to go, as a share of the crate's trucks when it was tapped (the progress bar).</summary>
        public double Progress;
        /// <summary>GameState.SlotBlocked: nothing of its color reachable now or once the trucks out collect.</summary>
        public bool Blocked;
        /// <summary>Landing bounce left (1 → 0) and number pop left (1 → 0).</summary>
        public double Land, Bump;
        /// <summary>Leaving: how far (0..1).</summary>
        public double LeaveT;
    }

    /// <summary>A crate in flight from the tray into its slot (main.ts drawFlyers), design units.</summary>
    public struct FlyerLook
    {
        public int Slot, Column, Color, Count;
        public double CenterX, CenterY, Width, Height, LabelSize;
    }

    /// <summary>
    /// What the tray and the slots show (Task 16, docs/TRAY_UI.md): main.ts's view state around the rules, engine-free.
    /// It never decides anything: what is in the tray and in each slot, the counts and whether a crate is blocked come
    /// straight from <see cref="GameState"/> (SlotBlocked included), taps go to <see cref="GameSession.Tap"/>, which calls
    /// GameEngine.Tap. Here are only the things main.ts keeps for drawing, in real time: each crate's count when it was
    /// tapped (for its progress bar), where a flying crate took off, the columns gliding together, crates rising a row,
    /// the press dip, the dock shaking when every slot is full, landings, number pops and emptied crates leaving.
    /// </summary>
    public sealed class HudModel
    {
        /// <summary>main.ts timings, seconds (real time).</summary>
        public const double LandTime = 0.45, PressTime = 0.28, LeaveTime = 0.5, RiseTime = 0.32, RiseStagger = 0.06, ShakeTime = 0.35;
        /// <summary>How high a tapped crate arcs on its way to the slot (FLY_ARC), and the dock's shake amplitude.</summary>
        public const double FlyArc = 26, ShakeAmplitude = 6;
        /// <summary>Opacity, side inset and label size by depth in the tray (TRAY_ALPHA, TRAY_INSET, TRAY_LABEL).</summary>
        public static readonly double[] TrayAlpha = { 1, 0.55, 0.32, 0 };
        public static readonly double[] TrayInset = { 0, 5, 9, 12 };
        public static readonly double[] TrayLabel = { 25, 19, 19, 17 };

        readonly GameSession session;
        readonly Dictionary<int, int> startCounts = new Dictionary<int, int>();
        readonly double[] slotBump = new double[SimConstants.MaxSlots];
        readonly double[] slotLand = new double[SimConstants.MaxSlots];
        readonly (double fromX, double fromY, int col, int color, int count)[] flights = new (double, double, int, int, int)[SimConstants.MaxSlots];
        readonly List<(int slot, int color, double t)> leavers = new List<(int, int, double)>(SimConstants.MaxSlots);
        double[] trayRise;
        double[] colX;
        int pressCol = -1;
        double pressT;

        public HudModel(GameSession session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            int cols = session.Game.State.ColumnCount;
            trayRise = new double[cols];
            colX = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                trayRise[c] = double.PositiveInfinity;
                colX[c] = double.NaN;
            }
            for (int c = 0; c < cols; c++) colX[c] = ColumnTargetX(c);
            session.CrateTapped += OnTapped;
            session.TapRefused += OnRefused;
            session.CrateLanded += OnLanded;
            session.CrateEmptied += OnEmptied;
            session.TruckSent += OnTruckSent;
        }

        public GameSession Session => session;
        GameState State => session.Game.State;

        /// <summary>Seconds of shaking left: every slot was full when a crate was tapped.</summary>
        public double Shake { get; private set; }
        /// <summary>The dock's sideways offset now, design units (main.ts: sin(shake · 60) · 6).</summary>
        public double ShakeOffset => Shake > 0 ? Math.Sin(Shake * 60) * ShakeAmplitude : 0;
        /// <summary>Taps refused so far (every slot full).</summary>
        public int RefusedCount { get; private set; }

        /// <summary>Stops following the session.</summary>
        public void Detach()
        {
            session.CrateTapped -= OnTapped;
            session.TapRefused -= OnRefused;
            session.CrateLanded -= OnLanded;
            session.CrateEmptied -= OnEmptied;
            session.TruckSent -= OnTruckSent;
        }

        void OnTapped(int col, int slot)
        {
            Crate crate = State.SlotAt(slot);
            if (crate != null) startCounts[crate.Id] = crate.Count;
            if (slot >= 0 && slot < flights.Length)
                flights[slot] = (ColumnX(col), DesignLayout.TrayY, col, crate?.Color ?? 0, crate?.Count ?? 0);
            if (col >= 0 && col < trayRise.Length) trayRise[col] = 0; // the crates behind move up a row
            Press(col);
        }

        void OnRefused(int col)
        {
            Shake = ShakeTime;
            RefusedCount++;
            Press(col);
        }

        void Press(int col)
        {
            pressCol = col;
            pressT = 0;
        }

        void OnLanded(int slot)
        {
            if (slot >= 0 && slot < slotLand.Length) slotLand[slot] = 1;
        }

        void OnTruckSent(Trucks.TruckMotion t)
        {
            if (t.Slot >= 0 && t.Slot < slotBump.Length) slotBump[t.Slot] = 1;
        }

        void OnEmptied(int slot, int color)
        {
            for (int k = leavers.Count - 1; k >= 0; k--)
                if (leavers[k].slot == slot) leavers.RemoveAt(k);
            leavers.Add((slot, color, 0));
        }

        /// <summary>Moves the animations on by <paramref name="realSeconds"/> (main.ts update, the parts in real time).</summary>
        public void Advance(double realSeconds)
        {
            double dt = Math.Max(0, realSeconds);
            Shake = Math.Max(0, Shake - dt);
            for (int k = 0; k < slotBump.Length; k++)
            {
                slotBump[k] = Math.Max(0, slotBump[k] - dt * 6);
                slotLand[k] = Math.Max(0, slotLand[k] - dt / LandTime);
            }
            if (pressCol >= 0)
            {
                pressT += dt;
                if (pressT >= PressTime) pressCol = -1;
            }
            for (int k = leavers.Count - 1; k >= 0; k--)
            {
                (int slot, int color, double t) = leavers[k];
                t += dt / LeaveTime;
                // A leaving crate gives way at once to a new crate flying into its slot.
                if (t >= 1 || State.SlotAt(slot) != null || session.Scheduler.IsFlying(slot)) leavers.RemoveAt(k);
                else leavers[k] = (slot, color, t);
            }
            EnsureColumns();
            for (int c = 0; c < trayRise.Length; c++) trayRise[c] += dt;
            // Columns glide to their places, e.g. closing up when one runs out of crates.
            double glide = 1 - Math.Exp(-dt * 12);
            for (int c = 0; c < colX.Length; c++)
            {
                double target = ColumnTargetX(c);
                double x = colX[c];
                colX[c] = Math.Abs(target - x) < 0.1 ? target : x + (target - x) * glide;
            }
        }

        void EnsureColumns()
        {
            int cols = State.ColumnCount;
            if (cols == colX.Length) return;
            Array.Resize(ref colX, cols);
            Array.Resize(ref trayRise, cols);
        }

        // ---- The tray ----

        /// <summary>
        /// main.ts columnTargetX: the columns that still hold crates are centered together, so when one runs out the
        /// rest close up around the middle; an empty column keeps its last place.
        /// </summary>
        public double ColumnTargetX(int col)
        {
            GameState s = State;
            int cols = s.ColumnCount;
            if (s.ColumnLength(col) == 0)
            {
                double kept = col < colX.Length ? colX[col] : double.NaN;
                return double.IsNaN(kept) ? DesignLayout.TrayLeft(cols) + col * (DesignLayout.BlockWidth + DesignLayout.BlockGapX) : kept;
            }
            int live = 0, place = 0;
            for (int c = 0; c < cols; c++)
            {
                if (s.ColumnLength(c) == 0) continue;
                if (c < col) place++;
                live++;
            }
            return DesignLayout.TrayLeft(live) + place * (DesignLayout.BlockWidth + DesignLayout.BlockGapX);
        }

        /// <summary>A column's left edge now (gliding toward its target).</summary>
        public double ColumnX(int col) => col >= 0 && col < colX.Length && !double.IsNaN(colX[col]) ? colX[col] : ColumnTargetX(col);

        /// <summary>The front crate's box of a column, where a tap lands (design units), or empty when the column is empty.</summary>
        public RectD FrontBox(int col) =>
            State.ColumnLength(col) == 0 ? new RectD(0, 0, 0, 0) : new RectD(ColumnX(col), DesignLayout.TrayY, DesignLayout.BlockWidth, DesignLayout.BlockHeight);

        /// <summary>The shelf under the stacks: the full tray's width, lined up with the dock (main.ts drawTray).</summary>
        public RectD Shelf
        {
            get
            {
                int cols = Math.Max(1, State.ColumnCount);
                double left = DesignLayout.TrayLeft(cols);
                double width = cols * DesignLayout.BlockWidth + (cols - 1) * DesignLayout.BlockGapX;
                return new RectD(left - 8, DesignLayout.TrayY - 12, width + 16, RowsHeight + 16);
            }
        }

        public static double RowsHeight => DesignLayout.TrayRows * (DesignLayout.BlockHeight + DesignLayout.BlockGapY);

        /// <summary>The recessed lane a column sits in, or empty for an empty column.</summary>
        public RectD Lane(int col) =>
            State.ColumnLength(col) == 0 ? new RectD(0, 0, 0, 0) : new RectD(ColumnX(col) - 3, DesignLayout.TrayY - 5, DesignLayout.BlockWidth + 6, RowsHeight);

        /// <summary>main.ts drawTrayStacks for one crate: rows further back are smaller and fainter; after a tap they rise.</summary>
        public TrayCrateLook TrayCrate(int col, int row)
        {
            var look = new TrayCrateLook { Column = col, Row = row };
            Crate b = State.CrateAt(col, row);
            if (b == null || row >= DesignLayout.TrayRows) return look;
            double since = col < trayRise.Length ? trayRise[col] : double.PositiveInfinity;
            double p = Math.Max(0, Math.Min(1, (since - row * RiseStagger) / RiseTime));
            double rise = 1 - EaseOutBack(p); // rows still to go; a little below 0 when overshooting
            double moving = Math.Sin(Math.PI * p);
            double depth = row + Math.Max(0, rise);
            double alpha = AtDepth(TrayAlpha, depth);
            if (alpha <= 0.01) return look;
            double inset = AtDepth(TrayInset, depth);
            double x = ColumnX(col);
            double y = DesignLayout.TrayY + row * (DesignLayout.BlockHeight + DesignLayout.BlockGapY) + rise * (DesignLayout.BlockHeight + DesignLayout.BlockGapY);
            look.Visible = true;
            look.Color = b.Color;
            look.Count = b.Count;
            look.CenterX = x + DesignLayout.BlockWidth / 2;
            look.Bottom = y + DesignLayout.BlockHeight;
            look.Width = (DesignLayout.BlockWidth - inset * 2) * (1 - 0.05 * moving);
            look.Height = DesignLayout.BlockHeight * (1 + 0.1 * moving);
            look.BodyAlpha = 1 - (1 - alpha) * (1 - alpha);
            look.LabelAlpha = alpha;
            look.LabelSize = AtDepth(TrayLabel, depth);
            look.Front = depth < 0.5;
            // A tap that could not send the crate (every slot full) still shows: it dips in place.
            if (row == 0 && pressCol == col && !IsFlyingFrom(col)) look.Dip = Math.Sin(Math.PI * Math.Min(1, pressT / PressTime));
            return look;
        }

        bool IsFlyingFrom(int col)
        {
            for (int s = 0; s < flights.Length; s++)
                if (session.Scheduler.IsFlying(s) && flights[s].col == col) return true;
            return false;
        }

        // ---- The slots ----

        public int SlotCount => State.SlotCount;

        /// <summary>The dock that holds the slots, sized to however many there are. It stays put; the crates in it shake.</summary>
        public RectD Dock
        {
            get
            {
                int n = Math.Max(1, State.SlotCount);
                RectD first = DesignLayout.SlotRect(0, n), last = DesignLayout.SlotRect(n - 1, n);
                return new RectD(first.X - 8, first.Y - 8, last.XMax - first.X + 16, first.Height + 16);
            }
        }

        /// <summary>main.ts drawSlots for slot <paramref name="i"/>.</summary>
        public SlotLook Slot(int i)
        {
            GameState s = State;
            RectD r = DesignLayout.SlotRect(i, Math.Max(1, s.SlotCount));
            var look = new SlotLook { Rect = new RectD(r.X + ShakeOffset, r.Y, r.Width, r.Height) };
            if (session.Scheduler.IsFlying(i))
            {
                look.Shows = SlotShows.Incoming;
                return look;
            }
            Crate b = s.SlotAt(i);
            if (b == null)
            {
                foreach (var l in leavers)
                    if (l.slot == i)
                    {
                        look.Shows = SlotShows.Leaving;
                        look.Color = l.color;
                        look.LeaveT = l.t;
                        return look;
                    }
                look.Shows = SlotShows.Empty;
                return look;
            }
            look.Shows = SlotShows.Crate;
            look.Color = b.Color;
            look.Count = b.Count;
            int start = startCounts.TryGetValue(b.Id, out int n) ? n : b.Count;
            look.Progress = start > 0 ? (double)b.Count / start : 0;
            look.Blocked = s.SlotBlocked(i);
            look.Land = i < slotLand.Length ? slotLand[i] : 0;
            look.Bump = i < slotBump.Length ? slotBump[i] : 0;
            return look;
        }

        /// <summary>The crate flying into slot <paramref name="slot"/> (main.ts drawFlyers); false when none is.</summary>
        public bool Flyer(int slot, out FlyerLook look)
        {
            look = default;
            if (slot < 0 || slot >= flights.Length || !session.Scheduler.IsFlying(slot)) return false;
            (double fromX, double fromY, int col, int color, int count) = flights[slot];
            double ft = session.Scheduler.FlightOf(slot);
            RectD r = DesignLayout.SlotRect(slot, Math.Max(1, State.SlotCount));
            double t = 1 - Math.Pow(1 - ft, 3);
            double lift = Math.Sin(Math.PI * ft);
            double grow = 1 + 0.12 * lift;
            double bw = DesignLayout.BlockWidth, bh = DesignLayout.BlockHeight;
            look.Slot = slot;
            look.Column = col;
            look.Color = color;
            look.Count = count;
            look.Width = (bw + (r.Width - bw) * t) * grow;
            look.Height = (bh + (r.Height - bh) * t) * grow;
            look.CenterX = fromX + bw / 2 + (r.X + r.Width / 2 - fromX - bw / 2) * t;
            look.CenterY = fromY + bh / 2 + (r.Y + r.Height / 2 - fromY - bh / 2) * t - FlyArc * lift;
            look.LabelSize = 25 * grow;
            return true;
        }

        /// <summary>main.ts drawLeaving: the crate swells a little, then shrinks away with a small spin as it fades.</summary>
        public static (double scale, double alpha, double turn, double ring) Leaving(double t)
        {
            const double swell = 0.25;
            double k = t < swell ? 0 : (t - swell) / (1 - swell);
            double scale = t < swell ? 1 + 0.12 * (t / swell) : 1.12 * (1 - k * k);
            return (scale, 1 - k, 0.35 * k, t * 16);
        }

        /// <summary>Landing wobble of a slot's crate (main.ts: sin((1 − land) · 3π) · land · 0.1).</summary>
        public static double Wobble(double land) => land > 0 ? Math.Sin((1 - land) * Math.PI * 3) * land * 0.1 : 0;

        /// <summary>The count's pop when a truck leaves (main.ts: 1 + 0.22 · bump²).</summary>
        public static double Pop(double bump) => 1 + 0.22 * bump * bump;

        // ---- main.ts helpers ----

        public static double EaseOutBack(double t)
        {
            const double c = 1.9;
            double u = t - 1;
            return 1 + (c + 1) * u * u * u + c * u * u;
        }

        public static double AtDepth(double[] table, double depth)
        {
            double d = Math.Max(0, Math.Min(table.Length - 1, depth));
            int i = Math.Min(table.Length - 2, (int)Math.Floor(d));
            return table[i] + (table[i + 1] - table[i]) * (d - i);
        }
    }
}
