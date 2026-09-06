using UnityEngine;
using UnityEngine.EventSystems;

namespace MazeSolver
{
    // Attach to any UI element to give it a hover tooltip. Works on non-raycastable
    // children (labels) too, since PointerEnter/Exit only need an Image/Graphic on this
    // object or a raycast target somewhere in its hierarchy.
    public sealed class UITooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string Text;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!string.IsNullOrEmpty(Text)) UITooltip.Show(Text, eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData) => UITooltip.Hide();

        void OnDisable() => UITooltip.Hide();
    }
}
