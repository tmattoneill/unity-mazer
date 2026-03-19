using UnityEngine;
using UnityEngine.UI;

namespace MazeSolver
{
    public class MazeUIController : MonoBehaviour
    {
        [Header("Sliders")]
        public Slider mazeSizeSlider;
        public Slider targetPathsSlider;
        public Slider speedSlider;

        [Header("Buttons")]
        public Button generateButton;
        public Button pauseButton;
        public Button resetButton;
        public Button quitButton;

        [Header("Labels")]
        public Text mazeSizeLabel;
        public Text targetPathsLabel;
        public Text speedLabel;
        public Text statusText;
        public Text agentCountText;
        public Text pauseButtonText;

        MazeGameManager gameManager;

        public int MazeSize => Mathf.RoundToInt(mazeSizeSlider.value);
        public int TargetPaths => Mathf.RoundToInt(targetPathsSlider.value);
        public float SpeedMs => speedSlider.value;

        public void Initialize(MazeGameManager manager)
        {
            gameManager = manager;

            mazeSizeSlider.minValue = 5;
            mazeSizeSlider.maxValue = 100;
            mazeSizeSlider.wholeNumbers = true;
            mazeSizeSlider.value = 25;
            mazeSizeSlider.onValueChanged.AddListener(v => UpdateLabels());

            targetPathsSlider.minValue = 1;
            targetPathsSlider.maxValue = 5;
            targetPathsSlider.wholeNumbers = true;
            targetPathsSlider.value = 3;
            targetPathsSlider.onValueChanged.AddListener(v => UpdateLabels());

            speedSlider.minValue = 1;
            speedSlider.maxValue = 200;
            speedSlider.wholeNumbers = true;
            speedSlider.value = 25;
            speedSlider.onValueChanged.AddListener(v => UpdateLabels());

            generateButton.onClick.AddListener(() => gameManager.GenerateAndSolve());
            pauseButton.onClick.AddListener(() => gameManager.TogglePause());
            resetButton.onClick.AddListener(() => gameManager.Reset());
            if (quitButton) quitButton.onClick.AddListener(() => Application.Quit());

            UpdateLabels();
            SetStatus("Ready");
            SetAgentCount(0, 0, 0);
        }

        void UpdateLabels()
        {
            if (mazeSizeLabel) mazeSizeLabel.text = $"Maze Size: {MazeSize}";
            if (targetPathsLabel) targetPathsLabel.text = $"Target Paths: {TargetPaths}";
            if (speedLabel) speedLabel.text = $"Speed: {SpeedMs:0} ms/step";
        }

        public void SetStatus(string status)
        {
            if (statusText) statusText.text = status;
        }

        public void SetAgentCount(int active, int dead, int total)
        {
            if (agentCountText) agentCountText.text = $"Agents: {active} active / {dead} dead / {total} total";
        }

        public void SetSolving(bool solving)
        {
            mazeSizeSlider.interactable = !solving;
            targetPathsSlider.interactable = !solving;
        }

        public void SetPauseText(bool isPaused)
        {
            if (pauseButtonText) pauseButtonText.text = isPaused ? "Resume" : "Pause";
        }
    }
}
