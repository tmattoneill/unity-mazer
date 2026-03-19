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

        [Header("Audio Controls")]
        public Button droneModeButton;
        public Text droneModeText;
        public Slider pitchSlider;
        public Slider volumeSlider;
        public Slider wobbleSlider;
        public Text pitchLabel;
        public Text volumeLabel;
        public Text wobbleLabel;
        public Button muteDroneButton;
        public Text muteDroneText;
        public Button muteSfxButton;
        public Text muteSfxText;
        public Button trackButton;
        public Text trackButtonText;

        [Header("Vitals")]
        public Text vitalsSummaryText;
        public Text agentListText;

        MazeGameManager gameManager;
        bool droneMuted;
        bool sfxMuted;
        DroneMode currentDroneMode = DroneMode.Mono;

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

            // Audio controls
            if (pitchSlider)
            {
                pitchSlider.minValue = -12;
                pitchSlider.maxValue = 12;
                pitchSlider.wholeNumbers = true;
                pitchSlider.value = 0;
                pitchSlider.onValueChanged.AddListener(v =>
                {
                    gameManager.AudioEngine.SetDronePitchSemitones(Mathf.RoundToInt(v));
                    UpdateLabels();
                });
            }

            if (volumeSlider)
            {
                volumeSlider.minValue = 0;
                volumeSlider.maxValue = 100;
                volumeSlider.wholeNumbers = true;
                volumeSlider.value = 50;
                volumeSlider.onValueChanged.AddListener(v =>
                {
                    gameManager.AudioEngine.SetDroneVolumePct(v);
                    UpdateLabels();
                });
            }

            if (wobbleSlider)
            {
                wobbleSlider.minValue = 0;
                wobbleSlider.maxValue = 100;
                wobbleSlider.wholeNumbers = true;
                wobbleSlider.value = 30;
                wobbleSlider.onValueChanged.AddListener(v =>
                {
                    gameManager.AudioEngine.SetDroneWobblePct(v);
                    UpdateLabels();
                });
            }

            if (droneModeButton)
            {
                droneModeButton.onClick.AddListener(() =>
                {
                    // Cycle: Mono -> Chord -> Soundtrack -> Mono
                    switch (currentDroneMode)
                    {
                        case DroneMode.Mono: currentDroneMode = DroneMode.Chord; break;
                        case DroneMode.Chord: currentDroneMode = DroneMode.Soundtrack; break;
                        case DroneMode.Soundtrack: currentDroneMode = DroneMode.Mono; break;
                    }
                    gameManager.AudioEngine.SetDroneMode(currentDroneMode);
                    // Reapply volume since gain mapping differs per mode
                    gameManager.AudioEngine.SetDroneVolumePct(volumeSlider.value);
                    UpdateLabels();
                });
            }

            if (trackButton)
            {
                trackButton.onClick.AddListener(() =>
                {
                    gameManager.AudioEngine.CycleTrack();
                    UpdateLabels();
                });
            }

            if (muteDroneButton)
            {
                muteDroneButton.onClick.AddListener(() =>
                {
                    droneMuted = !droneMuted;
                    gameManager.AudioEngine.SetDroneMuted(droneMuted);
                    UpdateLabels();
                });
            }

            if (muteSfxButton)
            {
                muteSfxButton.onClick.AddListener(() =>
                {
                    sfxMuted = !sfxMuted;
                    gameManager.AudioEngine.SetSfxMuted(sfxMuted);
                    UpdateLabels();
                });
            }

            UpdateLabels();
            SetStatus("Ready");
            SetAgentCount(0, 0, 0);
        }

        void UpdateLabels()
        {
            if (mazeSizeLabel) mazeSizeLabel.text = $"Maze Size: {MazeSize}";
            if (targetPathsLabel) targetPathsLabel.text = $"Target Paths: {TargetPaths}";
            if (speedLabel) speedLabel.text = $"Speed: {SpeedMs:0} ms/step";
            if (pitchLabel) pitchLabel.text = $"Pitch: {Mathf.RoundToInt(pitchSlider.value)} st";
            if (volumeLabel) volumeLabel.text = $"Volume: {Mathf.RoundToInt(volumeSlider.value)}%";
            if (wobbleLabel) wobbleLabel.text = $"Wobble: {Mathf.RoundToInt(wobbleSlider.value)}%";
            if (droneModeText)
            {
                switch (currentDroneMode)
                {
                    case DroneMode.Mono: droneModeText.text = "Mono"; break;
                    case DroneMode.Chord: droneModeText.text = "Chord"; break;
                    case DroneMode.Soundtrack: droneModeText.text = "Soundtrack"; break;
                }
            }
            if (trackButtonText && gameManager != null)
            {
                var names = gameManager.AudioEngine.GetTrackNames();
                int idx = gameManager.AudioEngine.CurrentTrackIndex;
                trackButtonText.text = names.Length > 0 ? names[idx] : "No tracks";
            }
            if (muteDroneText) muteDroneText.text = droneMuted ? "Unmute BG" : "Mute BG";
            if (muteSfxText) muteSfxText.text = sfxMuted ? "Unmute SFX" : "Mute SFX";
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

        public void UpdateVitals(MazeSolverEngine solver, float speedMs)
        {
            if (solver == null) return;

            // Summary line
            if (vitalsSummaryText)
            {
                vitalsSummaryText.text =
                    $"<color=#cccccc>{solver.TotalSpawned}</color> TOTAL   " +
                    $"<color=#44ff44>{solver.ActiveCount}</color> ACTIVE   " +
                    $"<color=#ffaa00>{solver.DeadCount}</color> DEAD   " +
                    $"<color=#00ffeb>{solver.SolvedCount}</color> SOLVED";
            }

            // Agent list (most recent first, cap at ~25 visible)
            if (agentListText)
            {
                var sb = new System.Text.StringBuilder();
                int shown = 0;
                // Iterate from highest ID down
                for (int id = solver.TotalSpawned - 1; id >= 0 && shown < 25; id--)
                {
                    if (!solver.Agents.ContainsKey(id)) continue;
                    var agent = solver.Agents[id];
                    shown++;

                    // Color dot based on agent color (use hex)
                    var col = AgentColorUtil.GetHeadColor(agent.Id);
                    string hex = ColorUtility.ToHtmlStringRGB(col);

                    // Status with color
                    string status;
                    switch (agent.Status)
                    {
                        case AgentStatus.Active:
                            status = "<color=#44ff44>active</color> ";
                            break;
                        case AgentStatus.Solved:
                            status = "<color=#00ffeb>solved</color>";
                            break;
                        default:
                            status = "<color=#666666>dead</color>   ";
                            break;
                    }

                    int pathLen = agent.OwnPath.Count;
                    int lifeSteps = (agent.DeathStep >= 0 ? agent.DeathStep : solver.CurrentStep) - agent.SpawnStep;
                    int lifeMs = Mathf.RoundToInt(lifeSteps * speedMs);

                    sb.AppendLine($"<color=#{hex}>\u25cf</color> #{agent.Id,-6} {status}  {pathLen,4}   {lifeMs}ms");
                }
                agentListText.text = sb.ToString();
            }
        }
    }
}
