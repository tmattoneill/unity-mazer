using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;

namespace MazeSolver.Editor
{
    public static class CreateMazeSolverScene
    {
        [MenuItem("Tools/Create Maze Solver Scene")]
        static void Create()
        {
            // Create a fresh scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Camera ---
            var cameraGO = new GameObject("Main Camera");
            var cam = cameraGO.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 30f;
            cam.backgroundColor = new Color32(10, 10, 18, 255);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.transform.position = new Vector3(24, 24, -10);
            cameraGO.AddComponent<AudioListener>();
            cameraGO.tag = "MainCamera";

            // --- Maze Display ---
            var displayGO = new GameObject("MazeDisplay");
            displayGO.AddComponent<SpriteRenderer>();
            var renderer = displayGO.AddComponent<MazeRenderer>();

            // --- Maze Manager ---
            var managerGO = new GameObject("MazeManager");
            var manager = managerGO.AddComponent<MazeGameManager>();
            var audio = managerGO.AddComponent<AudioSource>();
            var audioEngine = managerGO.AddComponent<MazeAudioEngine>();

            // --- UI Canvas ---
            var canvasGO = new GameObject("UICanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            // EventSystem is required for UI input (clicks, drags)
            var eventSystemGO = new GameObject("EventSystem");
            eventSystemGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            var uiController = canvasGO.AddComponent<MazeUIController>();

            // --- Panel background ---
            var panelGO = CreateUIElement("Panel", canvasGO.transform);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0, 0);
            panelRect.anchorMax = new Vector2(0, 1);
            panelRect.pivot = new Vector2(0, 0.5f);
            panelRect.sizeDelta = new Vector2(260, 0);
            panelRect.anchoredPosition = Vector2.zero;
            var panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0.05f, 0.05f, 0.08f, 0.85f);

            // --- Vertical layout group for controls (stretches to fill panel) ---
            var layoutGO = CreateUIElement("Controls", panelGO.transform);
            var layoutRect = layoutGO.GetComponent<RectTransform>();
            layoutRect.anchorMin = Vector2.zero;
            layoutRect.anchorMax = Vector2.one;
            layoutRect.offsetMin = new Vector2(10, 10);
            layoutRect.offsetMax = new Vector2(-10, -10);
            var vlg = layoutGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;

            // Title
            var titleText = CreateText("Maze Solver", layoutGO.transform, 18, TextAnchor.MiddleCenter, Color.white);
            titleText.GetComponent<LayoutElement>().preferredHeight = 28;

            // Status
            var statusText = CreateText("Ready", layoutGO.transform, 13, TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.8f));
            statusText.GetComponent<LayoutElement>().preferredHeight = 20;
            uiController.statusText = statusText.GetComponent<Text>();

            // Agent count
            var agentText = CreateText("Agents: 0 active / 0 dead / 0 total", layoutGO.transform, 10, TextAnchor.MiddleCenter, new Color(0.5f, 0.5f, 0.6f));
            agentText.GetComponent<LayoutElement>().preferredHeight = 16;
            uiController.agentCountText = agentText.GetComponent<Text>();

            // Maze Size slider
            var mazeSizeLabel = CreateText("Maze Size: 25", layoutGO.transform, 11, TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.9f));
            mazeSizeLabel.GetComponent<LayoutElement>().preferredHeight = 16;
            uiController.mazeSizeLabel = mazeSizeLabel.GetComponent<Text>();
            var mazeSizeSlider = CreateSlider(layoutGO.transform);
            uiController.mazeSizeSlider = mazeSizeSlider;

            // Target Paths slider
            var pathsLabel = CreateText("Target Paths: 3", layoutGO.transform, 11, TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.9f));
            pathsLabel.GetComponent<LayoutElement>().preferredHeight = 16;
            uiController.targetPathsLabel = pathsLabel.GetComponent<Text>();
            var pathsSlider = CreateSlider(layoutGO.transform);
            uiController.targetPathsSlider = pathsSlider;

            // Speed slider
            var speedLabel = CreateText("Speed: 25 ms/step", layoutGO.transform, 11, TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.9f));
            speedLabel.GetComponent<LayoutElement>().preferredHeight = 16;
            uiController.speedLabel = speedLabel.GetComponent<Text>();
            var speedSlider = CreateSlider(layoutGO.transform);
            uiController.speedSlider = speedSlider;

            CreateSpacer(layoutGO.transform, 2);

            // Generate & Solve button
            var genBtn = CreateButton("Generate & Solve", layoutGO.transform, new Color(0.1f, 0.6f, 0.3f));
            uiController.generateButton = genBtn;

            // Pause / Reset row
            var pauseBtn = CreateButton("Pause", layoutGO.transform, new Color(0.6f, 0.5f, 0.1f));
            uiController.pauseButton = pauseBtn;
            uiController.pauseButtonText = pauseBtn.GetComponentInChildren<Text>();

            var resetBtn = CreateButton("Reset", layoutGO.transform, new Color(0.6f, 0.15f, 0.15f));
            uiController.resetButton = resetBtn;

            CreateSpacer(layoutGO.transform, 2);

            // --- Audio Settings ---
            var audioTitle = CreateText("Audio", layoutGO.transform, 13, TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.8f));
            audioTitle.GetComponent<LayoutElement>().preferredHeight = 18;

            // Drone mode toggle button
            var droneModeBtn = CreateButton("Mono", layoutGO.transform, new Color(0.25f, 0.3f, 0.45f));
            uiController.droneModeButton = droneModeBtn;
            uiController.droneModeText = droneModeBtn.GetComponentInChildren<Text>();

            // Track selector button
            var trackBtn = CreateButton("Select Track", layoutGO.transform, new Color(0.2f, 0.25f, 0.4f));
            uiController.trackButton = trackBtn;
            uiController.trackButtonText = trackBtn.GetComponentInChildren<Text>();

            // Pitch slider
            var pitchLbl = CreateText("Pitch: 0 st", layoutGO.transform, 10, TextAnchor.MiddleLeft, new Color(0.7f, 0.7f, 0.8f));
            pitchLbl.GetComponent<LayoutElement>().preferredHeight = 14;
            uiController.pitchLabel = pitchLbl.GetComponent<Text>();
            var pitchSld = CreateSlider(layoutGO.transform);
            uiController.pitchSlider = pitchSld;

            // Volume slider
            var volLbl = CreateText("Volume: 50%", layoutGO.transform, 10, TextAnchor.MiddleLeft, new Color(0.7f, 0.7f, 0.8f));
            volLbl.GetComponent<LayoutElement>().preferredHeight = 14;
            uiController.volumeLabel = volLbl.GetComponent<Text>();
            var volSld = CreateSlider(layoutGO.transform);
            uiController.volumeSlider = volSld;

            // Wobble slider
            var wobLbl = CreateText("Wobble: 30%", layoutGO.transform, 10, TextAnchor.MiddleLeft, new Color(0.7f, 0.7f, 0.8f));
            wobLbl.GetComponent<LayoutElement>().preferredHeight = 14;
            uiController.wobbleLabel = wobLbl.GetComponent<Text>();
            var wobSld = CreateSlider(layoutGO.transform);
            uiController.wobbleSlider = wobSld;

            // Mute buttons
            var muteDroneBtn = CreateButton("Mute BG", layoutGO.transform, new Color(0.3f, 0.3f, 0.35f));
            uiController.muteDroneButton = muteDroneBtn;
            uiController.muteDroneText = muteDroneBtn.GetComponentInChildren<Text>();

            var muteSfxBtn = CreateButton("Mute SFX", layoutGO.transform, new Color(0.3f, 0.3f, 0.35f));
            uiController.muteSfxButton = muteSfxBtn;
            uiController.muteSfxText = muteSfxBtn.GetComponentInChildren<Text>();

            CreateSpacer(layoutGO.transform, 4);

            // Quit button
            var quitBtn = CreateButton("Quit", layoutGO.transform, new Color(0.4f, 0.4f, 0.45f));
            uiController.quitButton = quitBtn;

            // --- Right Panel (Solve Tree + Vitals) ---
            var rightPanelGO = CreateUIElement("RightPanel", canvasGO.transform);
            var rightPanelRect = rightPanelGO.GetComponent<RectTransform>();
            rightPanelRect.anchorMin = new Vector2(1, 0);
            rightPanelRect.anchorMax = new Vector2(1, 1);
            rightPanelRect.pivot = new Vector2(1, 0.5f);
            rightPanelRect.sizeDelta = new Vector2(550, 0);
            rightPanelRect.anchoredPosition = Vector2.zero;
            var rightPanelImg = rightPanelGO.AddComponent<Image>();
            rightPanelImg.color = new Color(0.04f, 0.04f, 0.07f, 0.9f);

            // --- Solve Tree (top 55%) ---
            var treeTitleGO = CreateUIElement("TreeTitle", rightPanelGO.transform);
            var treeTitleRect = treeTitleGO.GetComponent<RectTransform>();
            treeTitleRect.anchorMin = new Vector2(0, 1);
            treeTitleRect.anchorMax = new Vector2(1, 1);
            treeTitleRect.pivot = new Vector2(0.5f, 1);
            treeTitleRect.anchoredPosition = new Vector2(0, -2);
            treeTitleRect.sizeDelta = new Vector2(0, 16);
            var treeTitleText = treeTitleGO.AddComponent<Text>();
            treeTitleText.text = "Solve Tree";
            treeTitleText.fontSize = 11;
            treeTitleText.alignment = TextAnchor.MiddleCenter;
            treeTitleText.color = new Color(0.6f, 0.6f, 0.7f);
            treeTitleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var treeImageGO = CreateUIElement("TreeImage", rightPanelGO.transform);
            var treeImageRect = treeImageGO.GetComponent<RectTransform>();
            treeImageRect.anchorMin = new Vector2(0, 0.45f);
            treeImageRect.anchorMax = new Vector2(1, 1);
            treeImageRect.offsetMin = new Vector2(4, 0);
            treeImageRect.offsetMax = new Vector2(-4, -20);
            var treeRawImage = treeImageGO.AddComponent<RawImage>();
            treeRawImage.color = Color.white;

            var treeRenderer = managerGO.AddComponent<SolveTreeRenderer>();
            treeRenderer.targetImage = treeRawImage;

            // --- Agent Vitals (bottom 45%) ---
            var vitalsTitle = CreateUIElement("VitalsTitle", rightPanelGO.transform);
            var vitalsTitleRect = vitalsTitle.GetComponent<RectTransform>();
            vitalsTitleRect.anchorMin = new Vector2(0, 0.43f);
            vitalsTitleRect.anchorMax = new Vector2(1, 0.45f);
            vitalsTitleRect.offsetMin = new Vector2(8, 0);
            vitalsTitleRect.offsetMax = new Vector2(-8, 0);
            var vitalsTitleText = vitalsTitle.AddComponent<Text>();
            vitalsTitleText.text = "AGENTS";
            vitalsTitleText.fontSize = 11;
            vitalsTitleText.fontStyle = FontStyle.Bold;
            vitalsTitleText.alignment = TextAnchor.MiddleLeft;
            vitalsTitleText.color = new Color(0.7f, 0.5f, 0.9f);
            vitalsTitleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Summary line (TOTAL / ACTIVE / DEAD / SOLVED)
            var summaryGO = CreateUIElement("VitalsSummary", rightPanelGO.transform);
            var summaryRect = summaryGO.GetComponent<RectTransform>();
            summaryRect.anchorMin = new Vector2(0, 0.38f);
            summaryRect.anchorMax = new Vector2(1, 0.43f);
            summaryRect.offsetMin = new Vector2(8, 0);
            summaryRect.offsetMax = new Vector2(-8, 0);
            var summaryText = summaryGO.AddComponent<Text>();
            summaryText.text = "";
            summaryText.fontSize = 10;
            summaryText.alignment = TextAnchor.MiddleLeft;
            summaryText.color = new Color(0.8f, 0.8f, 0.85f);
            summaryText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            summaryText.supportRichText = true;

            // Agent list (scrollable text)
            // Header
            var headerGO = CreateUIElement("VitalsHeader", rightPanelGO.transform);
            var headerRect = headerGO.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 0.35f);
            headerRect.anchorMax = new Vector2(1, 0.38f);
            headerRect.offsetMin = new Vector2(8, 0);
            headerRect.offsetMax = new Vector2(-8, 0);
            var headerText = headerGO.AddComponent<Text>();
            headerText.text = "  ID         STATUS    PATHS  LIFETIME";
            headerText.fontSize = 9;
            headerText.alignment = TextAnchor.MiddleLeft;
            headerText.color = new Color(0.5f, 0.5f, 0.55f);
            headerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Agent list text area
            var agentListGO = CreateUIElement("AgentList", rightPanelGO.transform);
            var agentListRect = agentListGO.GetComponent<RectTransform>();
            agentListRect.anchorMin = new Vector2(0, 0);
            agentListRect.anchorMax = new Vector2(1, 0.35f);
            agentListRect.offsetMin = new Vector2(8, 4);
            agentListRect.offsetMax = new Vector2(-8, 0);
            var agentListText = agentListGO.AddComponent<Text>();
            agentListText.text = "";
            agentListText.fontSize = 9;
            agentListText.alignment = TextAnchor.UpperLeft;
            agentListText.color = Color.white;
            agentListText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            agentListText.supportRichText = true;
            agentListText.verticalOverflow = VerticalWrapMode.Truncate;

            // Wire vitals text references to UI controller
            uiController.vitalsSummaryText = summaryText;
            uiController.agentListText = agentListText;

            // --- Wire references ---
            manager.mazeRenderer = renderer;
            manager.uiController = uiController;
            manager.audioEngine = audioEngine;
            manager.solveTreeRenderer = treeRenderer;

            // Save scene
            var scenePath = "Assets/Scenes/MazeSolver.unity";
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);

            Debug.Log("Maze Solver scene created and saved to " + scenePath);
        }

        static GameObject CreateUIElement(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static GameObject CreateText(string content, Transform parent, int fontSize, TextAnchor alignment, Color color)
        {
            var go = CreateUIElement(content.Replace(" ", ""), parent);
            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.AddComponent<LayoutElement>();
            return go;
        }

        static Slider CreateSlider(Transform parent)
        {
            var sliderGO = CreateUIElement("Slider", parent);
            var le = sliderGO.AddComponent<LayoutElement>();
            le.preferredHeight = 16;

            var slider = sliderGO.AddComponent<Slider>();

            // Background
            var bgGO = CreateUIElement("Background", sliderGO.transform);
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.color = new Color(0.2f, 0.2f, 0.25f);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.25f);
            bgRect.anchorMax = new Vector2(1, 0.75f);
            bgRect.sizeDelta = Vector2.zero;

            // Fill area
            var fillAreaGO = CreateUIElement("FillArea", sliderGO.transform);
            var fillAreaRect = fillAreaGO.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.sizeDelta = Vector2.zero;

            var fillGO = CreateUIElement("Fill", fillAreaGO.transform);
            var fillImg = fillGO.AddComponent<Image>();
            fillImg.color = new Color(0.3f, 0.6f, 0.9f);
            var fillRect = fillGO.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = Vector2.zero;

            // Handle slide area
            var handleAreaGO = CreateUIElement("HandleSlideArea", sliderGO.transform);
            var handleAreaRect = handleAreaGO.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.sizeDelta = Vector2.zero;

            var handleGO = CreateUIElement("Handle", handleAreaGO.transform);
            var handleImg = handleGO.AddComponent<Image>();
            handleImg.color = Color.white;
            var handleRect = handleGO.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(14, 0);
            handleRect.anchorMin = new Vector2(0, 0);
            handleRect.anchorMax = new Vector2(0, 1);

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;

            return slider;
        }

        static Button CreateButton(string label, Transform parent, Color bgColor)
        {
            var btnGO = CreateUIElement(label.Replace(" ", "").Replace("&", ""), parent);
            var le = btnGO.AddComponent<LayoutElement>();
            le.preferredHeight = 28;

            var btnImg = btnGO.AddComponent<Image>();
            btnImg.color = bgColor;

            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = btnImg;

            var textGO = CreateUIElement("Text", btnGO.transform);
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.fontSize = 14;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            return btn;
        }

        static void CreateSpacer(Transform parent, float height)
        {
            var spacer = CreateUIElement("Spacer", parent);
            var le = spacer.AddComponent<LayoutElement>();
            le.preferredHeight = height;
        }
    }
}
