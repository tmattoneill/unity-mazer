using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MazeSolver
{
    public class SolveTreeRenderer : MonoBehaviour
    {
        public RawImage targetImage;

        Texture2D texture;
        Color32[] pixels;
        int texWidth = 512;
        int texHeight = 512;

        static readonly Color32 BgColor = new Color32(12, 12, 20, 255);
        static readonly Color32 LineColor = new Color32(239, 239, 239, 255);
        static readonly Color32 SolvedColor = new Color32(0, 255, 235, 255);
        static readonly Color32 DeadDimColor = new Color32(40, 40, 50, 255);

        // Cached tree structure
        List<int> roots = new List<int>();
        Dictionary<int, List<int>> children = new Dictionary<int, List<int>>();
        readonly Stack<List<int>> childListPool = new Stack<List<int>>();
        Dictionary<int, float> subtreeWeights = new Dictionary<int, float>();
        Dictionary<int, Vector2> nodePositions = new Dictionary<int, Vector2>();

        public void Initialize(int width, int height)
        {
            if (texture && texWidth == width && texHeight == height)
            {
                Clear();
                return;
            }
            DestroyRuntimeTexture();
            texWidth = width;
            texHeight = height;
            texture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            pixels = new Color32[texWidth * texHeight];

            if (targetImage)
                targetImage.texture = texture;

            Clear();
        }

        public void Clear()
        {
            if (pixels == null) return;
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = BgColor;
            texture.SetPixels32(pixels);
            texture.Apply();
        }

        public void RenderTree(MazeSolverEngine solver)
        {
            if (pixels == null || solver == null || solver.Agents.Count == 0) return;

            // Clear
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = BgColor;

            // Build tree structure
            roots.Clear();
            foreach (var childList in children.Values)
            {
                childList.Clear();
                childListPool.Push(childList);
            }
            children.Clear();
            subtreeWeights.Clear();
            nodePositions.Clear();

            foreach (var kvp in solver.Agents)
            {
                int id = kvp.Key;
                var agent = kvp.Value;
                if (agent.ParentId < 0)
                {
                    roots.Add(id);
                }
                else
                {
                    if (!children.ContainsKey(agent.ParentId))
                        children[agent.ParentId] = childListPool.Count > 0 ? childListPool.Pop() : new List<int>();
                    children[agent.ParentId].Add(id);
                }
            }

            // Calculate subtree weights (leaf count)
            foreach (var root in roots)
                CalcSubtreeWeight(root);

            // Find max depth for vertical scaling
            int maxDepth = 0;
            foreach (var root in roots)
                maxDepth = Mathf.Max(maxDepth, GetMaxDepth(root, 0));

            // Layout nodes
            float margin = 8f;
            float usableWidth = texWidth - margin * 2;
            float usableHeight = texHeight - margin * 2;
            float xOffset = margin;

            float totalRootWeight = 0;
            foreach (var root in roots)
                totalRootWeight += subtreeWeights[root];

            foreach (var root in roots)
            {
                float w = (subtreeWeights[root] / totalRootWeight) * usableWidth;
                LayoutNode(root, xOffset, w, 0, maxDepth, margin, usableHeight);
                xOffset += w;
            }

            // Draw edges first (behind nodes)
            foreach (var kvp in children)
            {
                int parentId = kvp.Key;
                if (!nodePositions.ContainsKey(parentId)) continue;
                var parentPos = nodePositions[parentId];

                foreach (int childId in kvp.Value)
                {
                    if (!nodePositions.ContainsKey(childId)) continue;
                    var childPos = nodePositions[childId];

                    // Draw L-shaped connector: vertical down from parent, horizontal to child
                    float midY = (parentPos.y + childPos.y) * 0.5f;
                    DrawLine(parentPos.x, parentPos.y, parentPos.x, midY, LineColor);
                    DrawLine(parentPos.x, midY, childPos.x, midY, LineColor);
                    DrawLine(childPos.x, midY, childPos.x, childPos.y, LineColor);
                }
            }

            // Draw nodes
            foreach (var kvp in solver.Agents)
            {
                int id = kvp.Key;
                var agent = kvp.Value;
                if (!nodePositions.ContainsKey(id)) continue;
                var pos = nodePositions[id];

                Color32 color;
                int radius;

                if (agent.Status == AgentStatus.Solved)
                {
                    color = SolvedColor;
                    radius = 4;
                }
                else if (agent.Status == AgentStatus.Active)
                {
                    color = AgentColorUtil.GetHeadColor(id);
                    radius = 3;
                }
                else // Dead
                {
                    color = AgentColorUtil.GetTailColor(id, 0.3f);
                    // Blend with dim background
                    color = new Color32(
                        (byte)((color.r + DeadDimColor.r) / 2),
                        (byte)((color.g + DeadDimColor.g) / 2),
                        (byte)((color.b + DeadDimColor.b) / 2),
                        255);
                    radius = 2;
                }

                DrawCircle((int)pos.x, (int)pos.y, radius, color);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
        }

        void CalcSubtreeWeight(int nodeId)
        {
            if (!children.ContainsKey(nodeId) || children[nodeId].Count == 0)
            {
                subtreeWeights[nodeId] = 1f;
                return;
            }

            float total = 0;
            foreach (int child in children[nodeId])
            {
                CalcSubtreeWeight(child);
                total += subtreeWeights[child];
            }
            subtreeWeights[nodeId] = total;
        }

        int GetMaxDepth(int nodeId, int depth)
        {
            int max = depth;
            if (children.ContainsKey(nodeId))
            {
                foreach (int child in children[nodeId])
                    max = Mathf.Max(max, GetMaxDepth(child, depth + 1));
            }
            return max;
        }

        void LayoutNode(int nodeId, float xStart, float width, int depth, int maxDepth, float yMargin, float usableHeight)
        {
            float y = maxDepth > 0
                ? yMargin + ((float)depth / maxDepth) * usableHeight
                : yMargin + usableHeight * 0.5f;
            float x = xStart + width * 0.5f;

            // Flip y so root is at top
            nodePositions[nodeId] = new Vector2(x, texHeight - y);

            if (!children.ContainsKey(nodeId)) return;

            var childList = children[nodeId];
            float childXOffset = xStart;
            float parentWeight = subtreeWeights[nodeId];

            foreach (int child in childList)
            {
                float childWidth = (subtreeWeights[child] / parentWeight) * width;
                LayoutNode(child, childXOffset, childWidth, depth + 1, maxDepth, yMargin, usableHeight);
                childXOffset += childWidth;
            }
        }

        void DrawCircle(int cx, int cy, int radius, Color32 color)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy <= radius * radius)
                        SetPixel(cx + dx, cy + dy, color);
                }
            }
        }

        void DrawLine(float x0f, float y0f, float x1f, float y1f, Color32 color)
        {
            int x0 = (int)x0f, y0 = (int)y0f, x1 = (int)x1f, y1 = (int)y1f;
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                SetPixel(x0, y0, color);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        void SetPixel(int x, int y, Color32 color)
        {
            if (x < 0 || x >= texWidth || y < 0 || y >= texHeight) return;
            pixels[y * texWidth + x] = color;
        }

        void OnDestroy()
        {
            if (targetImage && targetImage.texture == texture) targetImage.texture = null;
            DestroyRuntimeTexture();
        }

        void DestroyRuntimeTexture()
        {
            if (!texture) return;
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
            texture = null;
            pixels = null;
        }
    }
}
