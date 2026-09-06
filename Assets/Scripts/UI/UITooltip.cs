using UnityEngine;
using UnityEngine.UI;

namespace MazeSolver
{
    // A single floating tooltip box shared by every UITooltipTarget. Lazily builds its
    // own UI the first time something asks it to show, so it needs no scene wiring.
    public sealed class UITooltip : MonoBehaviour
    {
        static UITooltip instance;
        RectTransform box;
        Text label;
        Canvas canvas;

        public static void Show(string text, Vector2 screenPosition)
        {
            Get().ShowInternal(text, screenPosition);
        }

        public static void Hide()
        {
            if (instance) instance.HideInternal();
        }

        static UITooltip Get()
        {
            if (instance) return instance;
            var canvasGO = GameObject.Find("UICanvas");
            var parentCanvas = canvasGO ? canvasGO.GetComponent<Canvas>() : null;
            var go = new GameObject("Tooltip");
            if (parentCanvas) go.transform.SetParent(parentCanvas.transform, false);
            instance = go.AddComponent<UITooltip>();
            instance.canvas = parentCanvas;
            instance.Build();
            return instance;
        }

        void Build()
        {
            box = gameObject.AddComponent<RectTransform>();
            box.pivot = new Vector2(0, 1);
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.12f, 0.96f);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(transform, false);
            label = textGO.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 11;
            label.color = new Color(0.92f, 0.92f, 0.96f);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            var textRect = label.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8, 6);
            textRect.offsetMax = new Vector2(-8, -6);

            gameObject.SetActive(false);
        }

        void ShowInternal(string text, Vector2 screenPosition)
        {
            const float width = 240f;
            box.sizeDelta = new Vector2(width, 999f); // set width first so wrapping is correct below
            label.text = text;
            float height = label.preferredHeight + 12f;
            box.sizeDelta = new Vector2(width, height);

            // Keep the box on-screen: flip above the cursor and clamp horizontally.
            float x = screenPosition.x + 16f;
            float y = screenPosition.y;
            if (x + width > Screen.width) x = screenPosition.x - width - 16f;
            box.position = new Vector3(x, y, 0);
            gameObject.SetActive(true);
        }

        void HideInternal() => gameObject.SetActive(false);
    }
}
