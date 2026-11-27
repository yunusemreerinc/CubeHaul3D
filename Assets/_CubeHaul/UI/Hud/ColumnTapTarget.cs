using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace CubeHaul.UI.Hud
{
    /// <summary>
    /// The touch area over one tray column's front crate (main.ts registers the same box as a button every frame). It
    /// acts on pointer-down, as main.ts does, for the primary button of a mouse, a pen, or any finger: the Input System
    /// UI module reports them all as the same pointer event, so a click and a touch make the same move.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ColumnTapTarget : MonoBehaviour, IPointerDownHandler
    {
        [SerializeField] PlayAreaView view;
        [SerializeField] int column;

        public int Column => column;

        public void Configure(PlayAreaView owner, int col)
        {
            view = owner;
            column = col;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
            // What pressed it, for logs and tests: MouseOrPen or Touch (the move is the same).
            string pointer = eventData is ExtendedPointerEventData e ? e.pointerType.ToString() : "Unknown";
            if (view != null) view.TapColumn(column, pointer);
        }
    }
}
