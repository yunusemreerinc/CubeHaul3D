using CubeHaul.Presentation.Session;
using UnityEngine;
using UnityEngine.UI;

namespace CubeHaul.UI
{
    /// <summary>
    /// The top and bottom bar buttons that act on the game loop (Task 15): the speed button cycles 1x → 2x → 3x → 1x and
    /// shows the speed (main.ts drawSpeedButton), Restart plays the level again, +1 Slot uses the booster and greys out
    /// once it is used or the slots are at six. Listeners are added at run time, so nothing but the references is saved.
    /// </summary>
    public sealed class SessionControls : MonoBehaviour
    {
        [SerializeField] GameSessionDriver driver;
        [SerializeField] Button speedButton;
        [SerializeField] Text speedLabel;
        [SerializeField] Button restartButton;
        [SerializeField] Button addSlotButton;

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

        public Button SpeedButton => speedButton;
        public Text SpeedLabel => speedLabel;
        public Button RestartButton => restartButton;
        public Button AddSlotButton => addSlotButton;

        void OnEnable()
        {
            if (speedButton != null) speedButton.onClick.AddListener(OnSpeed);
            if (restartButton != null) restartButton.onClick.AddListener(OnRestart);
            if (addSlotButton != null) addSlotButton.onClick.AddListener(OnAddSlot);
            if (driver != null) driver.SpeedChanged += ShowSpeed;
            ShowSpeed(driver != null ? driver.Speed : 1);
        }

        void OnDisable()
        {
            if (speedButton != null) speedButton.onClick.RemoveListener(OnSpeed);
            if (restartButton != null) restartButton.onClick.RemoveListener(OnRestart);
            if (addSlotButton != null) addSlotButton.onClick.RemoveListener(OnAddSlot);
            if (driver != null) driver.SpeedChanged -= ShowSpeed;
        }

        void Update()
        {
            if (addSlotButton != null && driver != null && driver.Session != null)
            {
                bool can = driver.Session.CanAddSlot;
                if (addSlotButton.interactable != can) addSlotButton.interactable = can;
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

        void ShowSpeed(int speed)
        {
            if (speedLabel != null) speedLabel.text = speed + "x";
        }
    }
}
