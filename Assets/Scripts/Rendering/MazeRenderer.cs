using System.Collections.Generic;
using UnityEngine;

namespace MazeSolver
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class MazeRenderer : MonoBehaviour
    {
        Texture2D texture;
        Sprite runtimeSprite;
        Color32[] pixels;
        SpriteRenderer spriteRenderer;
        int gridSize;

        static readonly Color32 WallColor = new Color32(25, 25, 25, 255);
        static readonly Color32 PassageColor = new Color32(42, 42, 42, 255);
        static readonly Color32 StartColor = new Color32(0, 255, 0, 255);
        static readonly Color32 EndColor = new Color32(255, 0, 0, 255);
        static readonly Color32 SolutionColor = new Color32(0, 255, 235, 255);

        public void Initialize(int gridSize, byte[,] grid)
        {
            ReleaseRuntimeAssets();
            this.gridSize = gridSize;
            spriteRenderer = GetComponent<SpriteRenderer>();

            texture = new Texture2D(gridSize, gridSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            pixels = new Color32[gridSize * gridSize];

            // Initial base grid render
            for (int r = 0; r < gridSize; r++)
            {
                for (int c = 0; c < gridSize; c++)
                {
                    // Texture2D y=0 is bottom, so flip vertically
                    int pixelIdx = (gridSize - 1 - r) * gridSize + c;
                    pixels[pixelIdx] = grid[r, c] == 1 ? WallColor : PassageColor;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            runtimeSprite = Sprite.Create(texture,
                new Rect(0, 0, gridSize, gridSize),
                new Vector2(0.5f, 0.5f), 1f);
            spriteRenderer.sprite = runtimeSprite;
        }

        public void RenderFrame(MazeSolverEngine solver, byte[,] grid, int mazeN)
        {
            if (pixels == null) return;

            // 1. Base grid
            for (int r = 0; r < gridSize; r++)
            {
                for (int c = 0; c < gridSize; c++)
                {
                    int pixelIdx = (gridSize - 1 - r) * gridSize + c;
                    pixels[pixelIdx] = grid[r, c] == 1 ? WallColor : PassageColor;
                }
            }

            // 2. If solved, draw solution path only
            if (solver.IsSolved && solver.SolutionPath != null)
            {
                DrawSolutionPath(solver.SolutionPath);
                DrawMarker(0, 0, StartColor);
                DrawMarker(mazeN - 1, mazeN - 1, EndColor);
                texture.SetPixels32(pixels);
                texture.Apply();
                return;
            }

            // 3. Dead agent trails (dim)
            foreach (var kvp in solver.Agents)
            {
                var agent = kvp.Value;
                if (agent.Status != AgentStatus.Dead) continue;
                DrawAgentTrail(agent.OwnPath, agent.Id, 0.1f, false);
            }

            // 4. Active agent trails + heads
            foreach (var kvp in solver.Agents)
            {
                var agent = kvp.Value;
                if (agent.Status != AgentStatus.Active) continue;
                DrawAgentTrail(agent.OwnPath, agent.Id, 1f, true);
            }

            // 5. Start/end markers
            DrawMarker(0, 0, StartColor);
            DrawMarker(mazeN - 1, mazeN - 1, EndColor);

            texture.SetPixels32(pixels);
            texture.Apply();
        }

        void DrawAgentTrail(List<Vector2Int> path, int agentId, float maxAlpha, bool drawHead)
        {
            if (path.Count == 0) return;

            for (int i = 0; i < path.Count; i++)
            {
                int r = path[i].x;
                int c = path[i].y;
                bool isHead = drawHead && i == path.Count - 1;

                if (isHead)
                {
                    Color32 headColor = AgentColorUtil.GetHeadColor(agentId);
                    SetPixelBlended(r * 2, c * 2, headColor);
                }
                else
                {
                    float progress = path.Count > 1 ? (float)i / (path.Count - 1) : 1f;
                    float alpha = (0.25f + progress * 0.75f) * maxAlpha;
                    Color32 tailColor = AgentColorUtil.GetTailColor(agentId, alpha);
                    SetPixelBlended(r * 2, c * 2, tailColor);
                }

                // Fill wall passage between consecutive cells
                if (i < path.Count - 1)
                {
                    int nr = path[i + 1].x;
                    int nc = path[i + 1].y;
                    int wr = r * 2 + (nr - r);
                    int wc = c * 2 + (nc - c);

                    if (isHead)
                    {
                        // Don't draw wall passage for head
                    }
                    else
                    {
                        float progress = path.Count > 1 ? (float)i / (path.Count - 1) : 1f;
                        float alpha = (0.25f + progress * 0.75f) * maxAlpha;
                        Color32 wallColor = AgentColorUtil.GetTailColor(agentId, alpha);
                        SetPixelBlended(wr, wc, wallColor);
                    }
                }
            }
        }

        void DrawSolutionPath(List<Vector2Int> path)
        {
            for (int i = 0; i < path.Count; i++)
            {
                int r = path[i].x;
                int c = path[i].y;
                float progress = path.Count > 1 ? (float)i / (path.Count - 1) : 1f;
                float alpha = 0.5f + progress * 0.5f;

                Color32 color = new Color32(
                    (byte)(SolutionColor.r * alpha),
                    (byte)(SolutionColor.g * alpha),
                    (byte)(SolutionColor.b * alpha),
                    255);
                SetPixelDirect(r * 2, c * 2, color);

                if (i < path.Count - 1)
                {
                    int nr = path[i + 1].x;
                    int nc = path[i + 1].y;
                    int wr = r * 2 + (nr - r);
                    int wc = c * 2 + (nc - c);
                    SetPixelDirect(wr, wc, color);
                }
            }
        }

        void DrawMarker(int cellR, int cellC, Color32 color)
        {
            SetPixelDirect(cellR * 2, cellC * 2, color);
        }

        void SetPixelBlended(int r, int c, Color32 fg)
        {
            if (r < 0 || r >= gridSize || c < 0 || c >= gridSize) return;
            int idx = (gridSize - 1 - r) * gridSize + c;
            Color32 bg = pixels[idx];
            pixels[idx] = BlendOver(bg, fg);
        }

        void SetPixelDirect(int r, int c, Color32 color)
        {
            if (r < 0 || r >= gridSize || c < 0 || c >= gridSize) return;
            int idx = (gridSize - 1 - r) * gridSize + c;
            pixels[idx] = color;
        }

        static Color32 BlendOver(Color32 bg, Color32 fg)
        {
            float a = fg.a / 255f;
            float inv = 1f - a;
            return new Color32(
                (byte)(fg.r * a + bg.r * inv),
                (byte)(fg.g * a + bg.g * inv),
                (byte)(fg.b * a + bg.b * inv),
                255);
        }

        public void Clear()
        {
            if (pixels == null) return;
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(10, 10, 18, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
        }

        void OnDestroy() => ReleaseRuntimeAssets();

        void ReleaseRuntimeAssets()
        {
            if (spriteRenderer && spriteRenderer.sprite == runtimeSprite)
                spriteRenderer.sprite = null;
            DestroyRuntimeObject(runtimeSprite);
            DestroyRuntimeObject(texture);
            runtimeSprite = null;
            texture = null;
            pixels = null;
        }

        static void DestroyRuntimeObject(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
