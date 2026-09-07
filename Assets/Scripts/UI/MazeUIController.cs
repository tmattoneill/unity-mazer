using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace MazeSolver
{
    public class MazeUIController : MonoBehaviour
    {
        public Slider mazeSizeSlider, targetPathsSlider, speedSlider;
        public Button generateButton, pauseButton, resetButton, quitButton;
        public Text mazeSizeLabel, targetPathsLabel, speedLabel, statusText, agentCountText, pauseButtonText;
        // These legacy serialized names preserve saved-scene references: drone means music,
        // pitch means tempo, wobble means energy, and SFX means solver-event accents.
        public Button droneModeButton, muteDroneButton, muteSfxButton, trackButton;
        public Text droneModeText, muteDroneText, muteSfxText, trackButtonText;
        public Slider pitchSlider, volumeSlider, wobbleSlider, densitySlider, hallSlider, accentVolumeSlider;
        public Text pitchLabel, volumeLabel, wobbleLabel, densityLabel, hallLabel, accentVolumeLabel;
        public Text vitalsSummaryText, agentListText, welcomeText;
        public Slider variationSlider;
        public Text variationLabel, seedModeText, recordingStatusText, currentSeedText;
        public Button seedModeButton, saveWavButton, showFolderButton;
        public InputField seedInput;
        public Dropdown tonicDropdown, tonalityDropdown;
        public Dropdown styleDropdown;
        public GameObject solvePanel;
        public Button solvePanelButton;
        public Text solvePanelButtonText;
        string lastRecordingStatus;
        float lastSpeedMs = -1;
        MazeGameManager gameManager;
        MusicSettings music = MusicSettings.Default;
        readonly StringBuilder vitalsBuilder = new StringBuilder(2048);
        Slider[] orchestraControls;
        const string HexDigits = "0123456789ABCDEF";
        // A saved scene without the panel reference behaves as before: panel shown.
        public bool SolvePanelVisible => !solvePanel || solvePanel.activeSelf;
        // Pixels the solve panel occupies on the right; the camera fit keeps the maze clear of it.
        public float SolvePanelWidth => SolvePanelVisible ? 550f : 0f;
        public int MazeSize => Mathf.RoundToInt(mazeSizeSlider.value);
        public int TargetPaths => Mathf.RoundToInt(targetPathsSlider.value);
        public float SpeedMs => speedSlider.value;

        public void Initialize(MazeGameManager manager)
        {
            gameManager = manager;
            Configure(mazeSizeSlider, 5, 100, 25);
            Configure(targetPathsSlider, 1, 5, 3);
            Configure(speedSlider, 1, 200, 25);
            Configure(pitchSlider, 92, 132, 112);
            Configure(volumeSlider, 0, 100, 65);
            Configure(wobbleSlider, 0, 100, 60);
            Configure(densitySlider, 0, 100, 65);
            Configure(hallSlider, 0, 100, 28);
            Configure(accentVolumeSlider, 0, 100, 60);
            Configure(variationSlider, 0, 100, 35);
            orchestraControls = new[] { pitchSlider, wobbleSlider, densitySlider, hallSlider, accentVolumeSlider, variationSlider };
            if (styleDropdown)
            {
                styleDropdown.ClearOptions();
                styleDropdown.AddOptions(new System.Collections.Generic.List<string> { "Cinematic", "Ambient", "EDM", "Classical" });
                styleDropdown.SetValueWithoutNotify(0);
                styleDropdown.onValueChanged.AddListener(v => { music.Style = (MusicStyle)v; ApplyMusic(); });
            }
            if (tonicDropdown)
            {
                tonicDropdown.ClearOptions();
                tonicDropdown.AddOptions(new System.Collections.Generic.List<string>(MusicalKey.Names));
                tonicDropdown.SetValueWithoutNotify(2);
                tonicDropdown.onValueChanged.AddListener(v => { music.Tonic = v; ApplyMusic(); });
            }
            if (tonalityDropdown)
            {
                tonalityDropdown.ClearOptions();
                tonalityDropdown.AddOptions(new System.Collections.Generic.List<string> { "Minor", "Major" });
                tonalityDropdown.SetValueWithoutNotify(0);
                tonalityDropdown.onValueChanged.AddListener(v => { music.Tonality = (Tonality)v; ApplyMusic(); });
            }
            if (seedModeButton) seedModeButton.onClick.AddListener(() =>
            {
                gameManager.AudioEngine.NewSeedEachRun = !gameManager.AudioEngine.NewSeedEachRun;
                RefreshSeed();
            });
            if (seedInput) seedInput.onEndEdit.AddListener(value =>
            {
                if (int.TryParse(value, out int seed) && seed > 0) gameManager.AudioEngine.SelectedSeed = seed;
                RefreshSeed();
            });
            if (saveWavButton) saveWavButton.onClick.AddListener(() => gameManager.AudioEngine.SaveRecording());
            if (showFolderButton) showFolderButton.onClick.AddListener(() => gameManager.AudioEngine.ShowRecordingsFolder());
            RefreshSeed();
            generateButton.onClick.AddListener(() => gameManager.GenerateAndSolve());
            pauseButton.onClick.AddListener(() => gameManager.TogglePause());
            resetButton.onClick.AddListener(() => gameManager.ResetMaze());
            if (quitButton) quitButton.onClick.AddListener(() => Application.Quit());
            if (droneModeButton) droneModeButton.onClick.AddListener(() =>
            {
                music.Mode = music.Mode == MusicMode.Orchestra ? MusicMode.Soundtrack : MusicMode.Orchestra;
                ApplyMusic();
            });
            if (muteDroneButton) muteDroneButton.onClick.AddListener(() => { music.MusicMuted = !music.MusicMuted; ApplyMusic(); });
            if (muteSfxButton) muteSfxButton.onClick.AddListener(() => { music.AccentsMuted = !music.AccentsMuted; ApplyMusic(); });
            if (trackButton) trackButton.onClick.AddListener(() => { gameManager.AudioEngine.CycleTrack(); UpdateLabels(); });
            if (solvePanelButton) solvePanelButton.onClick.AddListener(ToggleSolvePanel);
            UpdateSolvePanelButton();
            ApplyMusic();
            SetStatus(gameManager.AudioEngine.OrchestraAvailable ? "Ready" : "Orchestra unavailable: " + gameManager.AudioEngine.Diagnostic);
            SetAgentCount(0, 0, 0);
        }
        void Configure(Slider slider, int min, int max, int value)
        {
            if (!slider) return;
            slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = true; slider.value = value;
            slider.onValueChanged.AddListener(v => ApplyMusic());
        }
        void ApplyMusic()
        {
            if (lastSpeedMs != SpeedMs)
            {
                lastSpeedMs = SpeedMs;
                gameManager.AudioEngine.Conductor.OnRequestedDelayChanged(SpeedMs / 1000f);
            }
            if (variationSlider) music.Variation = variationSlider.value / 100f;
            if (pitchSlider) music.Tempo = Mathf.RoundToInt(pitchSlider.value);
            if (volumeSlider) music.Volume = volumeSlider.value / 100f;
            if (wobbleSlider) music.Energy = wobbleSlider.value / 100f;
            if (densitySlider) music.Density = densitySlider.value / 100f;
            if (hallSlider) music.Hall = hallSlider.value / 100f;
            if (accentVolumeSlider) music.AccentVolume = accentVolumeSlider.value / 100f;
            gameManager.AudioEngine.ApplySettings(music);
            music = gameManager.AudioEngine.Settings;
            UpdateLabels();
        }
        void UpdateLabels()
        {
            if (mazeSizeLabel) mazeSizeLabel.text = $"Maze Size: {MazeSize}";
            if (targetPathsLabel) targetPathsLabel.text = $"Target Paths: {TargetPaths}";
            if (speedLabel) speedLabel.text = $"Speed: {SpeedMs:0} ms/step";
            if (variationLabel) variationLabel.text = $"Variation: {music.Variation:P0} (next phrase)";
            if (pitchLabel) pitchLabel.text = $"Tempo: {music.Tempo} BPM";
            if (volumeLabel) volumeLabel.text = $"Music: {music.Volume:P0}";
            if (wobbleLabel) wobbleLabel.text = $"Energy: {music.Energy:P0}";
            if (densityLabel) densityLabel.text = $"Density: {music.Density:P0}";
            if (hallLabel) hallLabel.text = $"Hall: {music.Hall:P0}";
            if (accentVolumeLabel) accentVolumeLabel.text = $"Accents: {music.AccentVolume:P0}";
            if (droneModeText) droneModeText.text = music.Mode == MusicMode.Orchestra ? "Orchestra (live)" : "Soundtrack (recorded)";
            if (droneModeButton) droneModeButton.interactable = gameManager.AudioEngine.OrchestraAvailable;
            if (muteDroneText) muteDroneText.text = music.MusicMuted ? "Unmute Music" : "Mute Music";
            if (muteSfxText) muteSfxText.text = music.AccentsMuted ? "Unmute Accents" : "Mute Accents";
            bool recorded = music.Mode == MusicMode.Soundtrack;
            if (trackButton) trackButton.gameObject.SetActive(recorded);
            if (trackButtonText)
            {
                var names = gameManager.AudioEngine.GetTrackNames();
                trackButtonText.text = names.Length > 0 ? names[gameManager.AudioEngine.CurrentTrackIndex] : "No tracks";
            }
            foreach (var control in orchestraControls)
                if (control) control.interactable = !recorded;
            if (tonicDropdown) tonicDropdown.interactable = !recorded;
            if (tonalityDropdown) tonalityDropdown.interactable = !recorded;
            if (styleDropdown) styleDropdown.interactable = !recorded;
            if (seedModeButton) seedModeButton.interactable = !recorded;
            RefreshSeed();
        }

        void ToggleSolvePanel()
        {
            if (!solvePanel) return;
            solvePanel.SetActive(!solvePanel.activeSelf);
            UpdateSolvePanelButton();
            gameManager.RefreshSolvePanel();
        }

        void UpdateSolvePanelButton()
        {
            if (solvePanelButtonText) solvePanelButtonText.text = SolvePanelVisible ? "Hide Solve Tree ▶" : "◀ Solve Tree";
        }

        public void RefreshSeed()
        {
            if (gameManager == null) return;
            var engine = gameManager.AudioEngine;
            if (currentSeedText) currentSeedText.text = engine.CurrentSeed == 0 ? "No performance started yet" : "Current seed: " + engine.CurrentSeed;
            if (seedInput)
            {
                seedInput.SetTextWithoutNotify(engine.SelectedSeed.ToString());
                seedInput.interactable = !engine.NewSeedEachRun && music.Mode == MusicMode.Orchestra;
            }
            if (seedModeText) seedModeText.text = engine.NewSeedEachRun ? "New seed each run: ON" : "New seed each run: OFF";
        }

        void Update()
        {
            if (gameManager == null) return;
            var engine = gameManager.AudioEngine;
            if (saveWavButton) saveWavButton.interactable = engine.CanSaveRecording;
            if (recordingStatusText && lastRecordingStatus != engine.RecordingStatus)
            {
                lastRecordingStatus = engine.RecordingStatus;
                recordingStatusText.text = lastRecordingStatus;
            }
        }

        public void SetWelcomeVisible(bool visible)
        {
            if (welcomeText) welcomeText.gameObject.SetActive(visible);
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
                vitalsBuilder.Clear();
                vitalsBuilder.Append("<color=#cccccc>").Append(solver.TotalSpawned).Append("</color> TOTAL   ")
                    .Append("<color=#44ff44>").Append(solver.ActiveCount).Append("</color> ACTIVE   ")
                    .Append("<color=#ffaa00>").Append(solver.DeadCount).Append("</color> DEAD   ")
                    .Append("<color=#00ffeb>").Append(solver.SolvedCount).Append("</color> SOLVED");
                vitalsSummaryText.text = vitalsBuilder.ToString();
            }

            // Agent list (most recent first, cap at ~25 visible)
            if (agentListText)
            {
                vitalsBuilder.Clear();
                int shown = 0;
                // Iterate from highest ID down
                for (int id = solver.TotalSpawned - 1; id >= 0 && shown < 25; id--)
                {
                    if (!solver.Agents.ContainsKey(id)) continue;
                    var agent = solver.Agents[id];
                    shown++;

                    // Color dot based on agent color (use hex)
                    var col = AgentColorUtil.GetHeadColor(agent.Id);
                    int pathLen = agent.OwnPath.Count;
                    int lifeSteps = (agent.DeathStep >= 0 ? agent.DeathStep : solver.CurrentStep) - agent.SpawnStep;
                    // Observed step timing beats the requested delay, which is only a floor.
                    float observed = gameManager.AudioEngine.Conductor.ObservedStepSeconds;
                    int lifeMs = Mathf.RoundToInt(lifeSteps * (observed > 0 ? observed * 1000f : speedMs));

                    vitalsBuilder.Append("<color=#");
                    AppendHex(vitalsBuilder, col.r); AppendHex(vitalsBuilder, col.g); AppendHex(vitalsBuilder, col.b);
                    vitalsBuilder.Append(">\u25cf</color> #").Append(agent.Id).Append("  ");
                    switch (agent.Status)
                    {
                        case AgentStatus.Active: vitalsBuilder.Append("<color=#44ff44>active</color> "); break;
                        case AgentStatus.Solved: vitalsBuilder.Append("<color=#00ffeb>solved</color>"); break;
                        default: vitalsBuilder.Append("<color=#666666>dead</color>   "); break;
                    }
                    vitalsBuilder.Append("  ").Append(pathLen).Append("   ").Append(lifeMs).AppendLine("ms");
                }
                agentListText.text = vitalsBuilder.ToString();
            }
        }

        static void AppendHex(StringBuilder builder, byte value)
        {
            builder.Append(HexDigits[value >> 4]);
            builder.Append(HexDigits[value & 15]);
        }
    }
}
