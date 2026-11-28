using CubeHaul.Gameplay.Flow;
using CubeHaul.Levels;
using CubeHaul.Presentation.Session;
using UnityEngine;
using UnityEngine.UI;

namespace CubeHaul.UI
{
    /// <summary>
    /// The top and bottom bars acting on the game loop (Tasks 15, 17; main.ts drawTopBar, drawBottomBar): restart,
    /// previous and next level, the title ("2/10 · Mushroom") and the difficulty chip of the level being played, the
    /// speed button (1x → 2x → 3x → 1x), and +1 Slot, which turns a muted slate once it is spent (used this level, six
    /// slots, or the level won). Listeners are added at run time, so nothing but the references is saved.
    /// </summary>
    public sealed class SessionControls : MonoBehaviour
    {
        public static readonly Color SlotGreen = new Color32(0x5c, 0xc4, 0x64, 0xff);
        public static readonly Color SlotSpent = new Color32(0x8a, 0x90, 0x9b, 0xff);

        [SerializeField] GameSessionDriver driver;
        [SerializeField] Button speedButton;
        [SerializeField] Text speedLabel;
        [SerializeField] Button restartButton;
        [SerializeField] Button addSlotButton;
        [SerializeField] Button previousButton;
        [SerializeField] Button nextButton;
        [SerializeField] Text title;
        [SerializeField] Image difficultyChip;
        [SerializeField] Text difficultyLabel;

        public void Configure(GameSessionDriver gameDriver, Button speed, Text speedText, Button restart, Button addSlot)
        {
            bool live = isActiveAndEnabled;
            if (live) OnDisable();
            driver = gameDriver;
            speedButton = speed;
            speedLabel = speedText;
            restartButton = restart;
            addSlotButton = addSlot;
            if (live) OnEnable();
        }

        /// <summary>The level navigation and the title (Task 17).</summary>
        public void ConfigureLevels(Button previous, Button next, Text titleText, Image chip, Text chipText)
        {
            bool live = isActiveAndEnabled;
            if (live) OnDisable();
            previousButton = previous;
            nextButton = next;
            title = titleText;
            difficultyChip = chip;
            difficultyLabel = chipText;
            if (live) OnEnable();
        }

        public Button SpeedButton => speedButton;
        public Text SpeedLabel => speedLabel;
        public Button RestartButton => restartButton;
        public Button AddSlotButton => addSlotButton;
        public Button PreviousButton => previousButton;
        public Button NextButton => nextButton;
        public Text Title => title;
        public Text DifficultyLabel => difficultyLabel;

        void OnEnable()
        {
            if (speedButton != null) speedButton.onClick.AddListener(OnSpeed);
            if (restartButton != null) restartButton.onClick.AddListener(OnRestart);
            if (addSlotButton != null) addSlotButton.onClick.AddListener(OnAddSlot);
            if (previousButton != null) previousButton.onClick.AddListener(OnPrevious);
            if (nextButton != null) nextButton.onClick.AddListener(OnNext);
            if (driver != null)
            {
                driver.SpeedChanged += ShowSpeed;
                driver.LevelStarted += ShowLevel;
                if (driver.Level != null) ShowLevel(driver);
            }
            ShowSpeed(driver != null ? driver.Speed : 1);
        }

        void OnDisable()
        {
            if (speedButton != null) speedButton.onClick.RemoveListener(OnSpeed);
            if (restartButton != null) restartButton.onClick.RemoveListener(OnRestart);
            if (addSlotButton != null) addSlotButton.onClick.RemoveListener(OnAddSlot);
            if (previousButton != null) previousButton.onClick.RemoveListener(OnPrevious);
            if (nextButton != null) nextButton.onClick.RemoveListener(OnNext);
            if (driver != null)
            {
                driver.SpeedChanged -= ShowSpeed;
                driver.LevelStarted -= ShowLevel;
            }
        }

        void Update()
        {
            if (addSlotButton == null || driver == null || driver.Session == null) return;
            // Spent: used this level, six slots, or won. On the start screen nothing can be pressed yet.
            bool can = driver.CanAddSlot && driver.Screen != FlowScreen.Start;
            if (addSlotButton.interactable != can) addSlotButton.interactable = can;
            if (addSlotButton.targetGraphic != null)
            {
                Color c = driver.CanAddSlot ? SlotGreen : SlotSpent;
                if (addSlotButton.targetGraphic.color != c) addSlotButton.targetGraphic.color = c;
            }
        }

        void OnSpeed()
        {
            if (driver != null) ShowSpeed(driver.CycleSpeed());
        }

        void OnRestart()
        {
            if (driver != null) driver.Restart();
        }

        void OnAddSlot()
        {
            if (driver != null) driver.AddSlot();
        }

        void OnPrevious()
        {
            if (driver != null) driver.PreviousLevel();
        }

        void OnNext()
        {
            if (driver != null) driver.NextLevel();
        }

        void ShowSpeed(int speed)
        {
            if (speedLabel != null) speedLabel.text = speed + "x";
        }

        void ShowLevel(GameSessionDriver d)
        {
            if (title != null) title.text = d.LevelTitle;
            DifficultyMetadata meta = d.Level?.DifficultyMetadata;
            bool known = meta != null;
            if (difficultyChip != null && difficultyChip.gameObject.activeSelf != known) difficultyChip.gameObject.SetActive(known);
            if (!known) return;
            if (difficultyChip != null) difficultyChip.color = DifficultyChipColors.For(meta.Label);
            if (difficultyLabel != null) difficultyLabel.text = DifficultyLabels.ToJson(meta.Label);
        }
    }
}
