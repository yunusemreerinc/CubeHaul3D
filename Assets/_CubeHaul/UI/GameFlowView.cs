using CubeHaul.Gameplay.Flow;
using CubeHaul.Presentation.Session;
using UnityEngine;
using UnityEngine.UI;

namespace CubeHaul.UI
{
    /// <summary>
    /// The screens over the game (Task 17, docs/GAME_FLOW.md; main.ts drawOverlay and panel): a dim layer that takes
    /// every touch so nothing underneath can be played, and a card.
    /// <list type="bullet">
    /// <item><b>Start:</b> the level's title, Play.</item>
    /// <item><b>Won</b> ("Picture complete!"): Next level, or Back to level 1 after the last.</item>
    /// <item><b>Stuck</b> ("Out of moves"): +1 Slot while the booster is left (free), Retry.</item>
    /// </list>
    /// Which one shows is <see cref="GameSessionDriver.Screen"/>: the won and stuck panels open only once every truck
    /// is home (GameFlow). Listeners are added at run time; the composer saves only the objects and references.
    /// </summary>
    public sealed class GameFlowView : MonoBehaviour
    {
        [SerializeField] GameSessionDriver driver;
        [SerializeField] GameObject blocker;
        [SerializeField] GameObject startPanel, wonPanel, stuckPanel;
        [SerializeField] Text startTitle;
        [SerializeField] Button playButton, nextButton, addSlotButton, retryButton;
        [SerializeField] Text nextLabel;

        public GameObject Blocker => blocker;
        public GameObject StartPanel => startPanel;
        public GameObject WonPanel => wonPanel;
        public GameObject StuckPanel => stuckPanel;
        public Button PlayButton => playButton;
        public Button NextButton => nextButton;
        public Button AddSlotButton => addSlotButton;
        public Button RetryButton => retryButton;
        public Text NextLabel => nextLabel;
        public Text StartTitle => startTitle;

        public void Configure(GameSessionDriver gameDriver, GameObject dim, GameObject start, GameObject won, GameObject stuck,
            Text startTitleText, Button play, Button next, Text nextText, Button addSlot, Button retry)
        {
            bool live = isActiveAndEnabled;
            if (live) OnDisable();
            driver = gameDriver;
            blocker = dim;
            startPanel = start;
            wonPanel = won;
            stuckPanel = stuck;
            startTitle = startTitleText;
            playButton = play;
            nextButton = next;
            nextLabel = nextText;
            addSlotButton = addSlot;
            retryButton = retry;
            if (live) OnEnable();
            Refresh();
        }

        void OnEnable()
        {
            if (playButton != null) playButton.onClick.AddListener(OnPlay);
            if (nextButton != null) nextButton.onClick.AddListener(OnNext);
            if (addSlotButton != null) addSlotButton.onClick.AddListener(OnAddSlot);
            if (retryButton != null) retryButton.onClick.AddListener(OnRetry);
            Refresh();
        }

        void OnDisable()
        {
            if (playButton != null) playButton.onClick.RemoveListener(OnPlay);
            if (nextButton != null) nextButton.onClick.RemoveListener(OnNext);
            if (addSlotButton != null) addSlotButton.onClick.RemoveListener(OnAddSlot);
            if (retryButton != null) retryButton.onClick.RemoveListener(OnRetry);
        }

        void LateUpdate() => Refresh();

        /// <summary>Shows the panel for the driver's screen now.</summary>
        public void Refresh()
        {
            FlowScreen screen = driver != null && driver.Session != null ? driver.Screen : FlowScreen.Playing;
            Show(startPanel, screen == FlowScreen.Start);
            Show(wonPanel, screen == FlowScreen.Won);
            Show(stuckPanel, screen == FlowScreen.Stuck);
            Show(blocker, screen != FlowScreen.Playing);
            if (screen == FlowScreen.Start && startTitle != null) startTitle.text = driver.LevelTitle;
            if (screen == FlowScreen.Won && nextLabel != null) nextLabel.text = driver.IsLastLevel ? "Back to level 1" : "Next level";
            if (screen == FlowScreen.Stuck) LayOutStuckButtons(driver.CanAddSlot);
        }

        /// <summary>main.ts panel: two buttons 130 wide, 16 apart; one alone is 200 wide.</summary>
        void LayOutStuckButtons(bool canSlot)
        {
            if (addSlotButton == null || retryButton == null) return;
            Show(addSlotButton.gameObject, canSlot);
            var slot = (RectTransform)addSlotButton.transform;
            var retry = (RectTransform)retryButton.transform;
            if (canSlot)
            {
                slot.sizeDelta = new Vector2(130, slot.sizeDelta.y);
                retry.sizeDelta = new Vector2(130, retry.sizeDelta.y);
                slot.anchoredPosition = new Vector2(-73, slot.anchoredPosition.y);
                retry.anchoredPosition = new Vector2(73, retry.anchoredPosition.y);
            }
            else
            {
                retry.sizeDelta = new Vector2(200, retry.sizeDelta.y);
                retry.anchoredPosition = new Vector2(0, retry.anchoredPosition.y);
            }
        }

        static void Show(GameObject go, bool visible)
        {
            if (go != null && go.activeSelf != visible) go.SetActive(visible);
        }

        void OnPlay()
        {
            if (driver != null) driver.StartPlaying();
            Refresh();
        }

        void OnNext()
        {
            if (driver != null) driver.NextLevel();
            Refresh();
        }

        void OnAddSlot()
        {
            if (driver != null) driver.AddSlot();
        }

        void OnRetry()
        {
            if (driver != null) driver.Retry();
            Refresh();
        }
    }
}
