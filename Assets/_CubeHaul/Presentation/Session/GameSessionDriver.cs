using System;
using CubeHaul.Gameplay.Session;
using CubeHaul.Levels;
using CubeHaul.Presentation.Grid;
using CubeHaul.Presentation.Trucks;
using UnityEngine;

namespace CubeHaul.Presentation.Session
{
    /// <summary>
    /// The game loop in the scene (Task 15, docs/SESSION.md): plays a level of the <see cref="LevelLibrary"/> as a
    /// <see cref="GameSession"/>. Every frame the frame time goes into a <see cref="SessionLoop"/> (whole 1/60 s steps,
    /// as cube-hunt at 60 Hz); the <see cref="GridPresenter"/> follows the board, the <see cref="TruckFleetView"/> draws
    /// the trucks and the cubes they load, in between steps. The player taps the tray (Task 16); <see cref="AutoPlay"/> taps
    /// the crates by themselves instead (demos, tests). 1x / 2x / 3x through <see cref="CycleSpeed"/> (the top bar's speed button).
    /// </summary>
    public sealed class GameSessionDriver : MonoBehaviour
    {
        [SerializeField] GridPresenter grid;
        [SerializeField] TruckFleetView trucks;
        [SerializeField] LevelLibrary library;
        [SerializeField, Min(0)] int levelIndex;
        [SerializeField, Tooltip("Tap the crates by themselves (cube-hunt's reference policy): demos and tests. The tray takes touches.")]
        bool autoPlay;
        [SerializeField, Range(1, 3), Tooltip("1x, 2x or 3x.")]
        int speed = 1;
        [SerializeField, Tooltip("Start the level when Play mode starts. Off: call LoadLevel.")]
        bool playOnStart = true;

        LevelDefinition level;
        SessionLoop loop;
        AutoPlayOutcome reported;

        /// <summary>A level started (a new session).</summary>
        public event Action<GameSessionDriver> LevelStarted;
        /// <summary>The speed changed (1, 2 or 3).</summary>
        public event Action<int> SpeedChanged;

        public GameSession Session => loop?.Session;
        public SessionLoop Loop => loop;
        public LevelDefinition Level => level;
        public int LevelIndex => levelIndex;
        public GridPresenter Grid => grid;
        public TruckFleetView Trucks => trucks;

        public bool AutoPlay
        {
            get => autoPlay;
            set
            {
                autoPlay = value;
                if (loop != null) loop.AutoPlay = value;
            }
        }

        public bool PlayOnStart
        {
            get => playOnStart;
            set => playOnStart = value;
        }

        public int Speed => loop?.Session.Speed ?? speed;

        public void Configure(GridPresenter presenter, TruckFleetView fleet, LevelLibrary levels)
        {
            grid = presenter;
            trucks = fleet;
            library = levels;
        }

        void Start()
        {
            if (playOnStart && library != null) LoadLevel(levelIndex);
        }

        /// <summary>Starts level <paramref name="index"/> of the library (wrapping round, B-37) from the beginning.</summary>
        public bool LoadLevel(int index)
        {
            if (library == null) return false;
            LevelRepository repo = library.Load();
            if (repo.HasErrors || repo.Count == 0)
            {
                Debug.LogWarning("GameSessionDriver: the level library has no playable levels.", this);
                return false;
            }
            levelIndex = ((index % repo.Count) + repo.Count) % repo.Count;
            return Play(repo.GetAt(levelIndex));
        }

        /// <summary>Starts this level from the beginning.</summary>
        public bool Play(LevelDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (loop != null) loop.Session.SpeedChanged -= OnSpeedChanged;
            level = definition;
            var session = GameSession.For(level);
            session.SetSpeed(Mathf.Clamp(speed, 1, 3));
            session.SpeedChanged += OnSpeedChanged;
            loop = new SessionLoop(session) { AutoPlay = autoPlay };
            reported = AutoPlayOutcome.Playing;
            // The trucks first: cubes they are loading go back to the grid before it shows the new picture.
            if (trucks != null) trucks.Bind(session, grid);
            if (grid != null)
            {
                grid.Show(level.Picture);
                grid.Bind(session.Game.Board);
            }
            LevelStarted?.Invoke(this);
            return true;
        }

        /// <summary>The same level again.</summary>
        public void Restart()
        {
            if (level != null) Play(level);
        }

        /// <summary>1x → 2x → 3x → 1x; returns the new speed.</summary>
        public int CycleSpeed()
        {
            if (loop == null)
            {
                speed = speed % 3 + 1;
                SpeedChanged?.Invoke(speed);
                return speed;
            }
            return loop.Session.CycleSpeed();
        }

        public void SetSpeed(int value)
        {
            speed = Mathf.Clamp(value, 1, 3);
            loop?.Session.SetSpeed(speed);
        }

        /// <summary>
        /// A tap on a tray column (main.ts tapColumn): its front crate goes to the first free slot through
        /// GameSession.Tap → GameEngine.Tap. Returns the slot, or -1 (empty column, game over, or every slot full; the
        /// last is reported by GameSession.TapRefused).
        /// </summary>
        public int Tap(int column) => loop != null ? loop.Session.Tap(column) : -1;

        /// <summary>The +1 Slot booster (once per level).</summary>
        public bool AddSlot() => loop != null && loop.Session.AddSlot();

        void OnSpeedChanged(int value)
        {
            speed = value;
            SpeedChanged?.Invoke(value);
        }

        void Update()
        {
            if (Application.isPlaying) Frame(Time.deltaTime);
        }

        /// <summary>One device frame of <paramref name="seconds"/>: whole steps of the session, then the trucks are drawn.</summary>
        public void Frame(float seconds)
        {
            if (loop == null) return;
            loop.Frame(seconds);
            if (trucks != null) trucks.Render((float)loop.Alpha);
            if (loop.Outcome != reported)
            {
                reported = loop.Outcome;
                GameSession s = loop.Session;
                if (reported != AutoPlayOutcome.Playing)
                    Debug.Log($"{level.Name}: {(reported == AutoPlayOutcome.Won ? "won" : "stuck")} after {s.RealTime:0.0} s at {s.Speed}x, " +
                              $"{level.Picture.CubeCount - s.Game.Board.Remaining} of {level.Picture.CubeCount} cubes collected.", this);
            }
        }

        void OnValidate()
        {
            speed = Mathf.Clamp(speed, 1, 3);
            if (loop != null && Application.isPlaying)
            {
                loop.AutoPlay = autoPlay;
                if (loop.Session.Speed != speed) loop.Session.SetSpeed(speed);
            }
        }

        void OnDestroy()
        {
            if (loop != null) loop.Session.SpeedChanged -= OnSpeedChanged;
            if (trucks != null) trucks.Bind(null, null);
        }
    }
}
