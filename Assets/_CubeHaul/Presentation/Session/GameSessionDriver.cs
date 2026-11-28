using System;
using CubeHaul.Gameplay.Flow;
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
    ///
    /// The level flow (Task 17, docs/GAME_FLOW.md): a <see cref="GameFlow"/> holds the start screen (nothing moves
    /// until Play), and opens the won or stuck panel only once the game is over and every truck is home. Restart and
    /// Retry play the level again, Next and Previous go round the library (after the last level, the first), +1 Slot
    /// calls GameEngine.AddSlot through the session (free: no ad or purchase in front of it).
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
        [SerializeField, Tooltip("Open on the start screen (the level shown, nothing moving until Play).")]
        bool showStartScreen = true;

        readonly GameFlow flow = new GameFlow();
        LevelRepository levels;
        LevelDefinition level;
        SessionLoop loop;
        int playing = -1;
        FlowScreen reported = FlowScreen.Playing;

        /// <summary>A level started (a new session).</summary>
        public event Action<GameSessionDriver> LevelStarted;
        /// <summary>The speed changed (1, 2 or 3).</summary>
        public event Action<int> SpeedChanged;
        /// <summary>The start screen, playing, or an end panel (GameFlow.ScreenChanged).</summary>
        public event Action<FlowScreen> ScreenChanged
        {
            add => flow.ScreenChanged += value;
            remove => flow.ScreenChanged -= value;
        }

        public GameFlow Flow => flow;
        public FlowScreen Screen => flow.Screen;
        /// <summary>The library's levels (read once), or those given to <see cref="UseLevels"/>; null without a library.</summary>
        public LevelRepository Levels
        {
            get
            {
                if (levels == null && library != null)
                {
                    LevelRepository repo = library.Load();
                    if (!repo.HasErrors && repo.Count > 0) levels = repo;
                    else Debug.LogWarning("GameSessionDriver: the level library has no playable levels.", this);
                }
                return levels;
            }
        }
        public int LevelCount => Levels?.Count ?? 0;
        /// <summary>The library index of the level being played, or -1 for a level given to <see cref="Play"/> directly.</summary>
        public int PlayingIndex => playing;
        /// <summary>The top bar's title: "2/10 · Mushroom", or the level's name for a level from outside the library.</summary>
        public string LevelTitle => level == null ? "" : playing >= 0 ? $"{playing + 1}/{LevelCount} · {level.Name}" : level.Name;
        /// <summary>The won panel's button: "Back to level 1" after the last level.</summary>
        public bool IsLastLevel => playing >= 0 && GameFlow.IsLast(playing, LevelCount);

        public bool ShowStartScreen
        {
            get => showStartScreen;
            set => showStartScreen = value;
        }

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
            if (playOnStart && Levels != null) LoadLevel(levelIndex, showStartScreen);
        }

        /// <summary>Plays these levels instead of the library's (tests, tools).</summary>
        public void UseLevels(LevelRepository repository)
        {
            levels = repository;
        }

        /// <summary>
        /// Starts level <paramref name="index"/> of the library (wrapping round, B-37) from the beginning, on the start
        /// screen if <paramref name="showStart"/>.
        /// </summary>
        public bool LoadLevel(int index, bool showStart = false)
        {
            LevelRepository repo = Levels;
            if (repo == null || repo.Count == 0) return false;
            levelIndex = ((index % repo.Count) + repo.Count) % repo.Count;
            Begin(repo.GetAt(levelIndex), levelIndex, showStart);
            return true;
        }

        /// <summary>The next level of the library (after the last, the first); a level from outside it is played again.</summary>
        public bool NextLevel() => playing >= 0 ? LoadLevel(GameFlow.NextIndex(playing, LevelCount)) : Restart();

        public bool PreviousLevel() => playing >= 0 ? LoadLevel(GameFlow.PreviousIndex(playing, LevelCount)) : Restart();

        /// <summary>The start screen's Play: the level starts moving.</summary>
        public void StartPlaying() => flow.Play();

        /// <summary>Starts this level from the beginning, straight into play.</summary>
        public bool Play(LevelDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Begin(definition, -1, false);
            return true;
        }

        void Begin(LevelDefinition definition, int index, bool showStart)
        {
            if (loop != null) loop.Session.SpeedChanged -= OnSpeedChanged;
            level = definition;
            var session = GameSession.For(level);
            session.SetSpeed(Mathf.Clamp(speed, 1, 3));
            session.SpeedChanged += OnSpeedChanged;
            loop = new SessionLoop(session) { AutoPlay = autoPlay };
            playing = index;
            flow.Begin(showStart);
            reported = flow.Screen;
            // The trucks first: cubes they are loading go back to the grid before it shows the new picture.
            if (trucks != null) trucks.Bind(session, grid);
            if (grid != null)
            {
                grid.Show(level.Picture);
                grid.Bind(session.Game.Board);
            }
            LevelStarted?.Invoke(this);
        }

        /// <summary>The same level again, from the beginning (the top bar's restart, the stuck panel's Retry).</summary>
        public bool Restart()
        {
            if (level == null) return false;
            Begin(level, playing, false);
            return true;
        }

        public bool Retry() => Restart();

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

        /// <summary>
        /// The +1 Slot booster: GameEngine.AddSlot through the session, once per level, up to six slots, not after a win.
        /// It un-sticks a stuck game; the stuck panel closes on the next frame. Free for now.
        /// </summary>
        public bool AddSlot() => loop != null && loop.Session.AddSlot();

        public bool CanAddSlot => loop != null && loop.Session.CanAddSlot;

        void OnSpeedChanged(int value)
        {
            speed = value;
            SpeedChanged?.Invoke(value);
        }

        void Update()
        {
            if (Application.isPlaying) Frame(Time.deltaTime);
        }

        /// <summary>
        /// One device frame of <paramref name="seconds"/>: whole steps of the session (none on the start screen), the
        /// flow, then the trucks are drawn.
        /// </summary>
        public void Frame(float seconds)
        {
            if (loop == null) return;
            if (flow.Screen != FlowScreen.Start)
            {
                loop.Frame(seconds);
                flow.Update(loop.Session, seconds);
            }
            if (trucks != null) trucks.Render((float)loop.Alpha);
            if (flow.Screen != reported)
            {
                reported = flow.Screen;
                GameSession s = loop.Session;
                if (reported == FlowScreen.Won || reported == FlowScreen.Stuck)
                    Debug.Log($"{level.Name}: {(reported == FlowScreen.Won ? "won" : "stuck")} after {s.RealTime:0.0} s at {s.Speed}x, " +
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
