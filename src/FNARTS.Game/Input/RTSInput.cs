using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FNARTS.Game
{
    /// <summary>
    /// Per-frame input state. Polls FNA input and maps to logical actions.
    /// Call Update() once per frame before querying.
    /// </summary>
    public class RTSInput
    {
        private InputMapping _mapping;

        public RTSInput() { _mapping = new InputMapping(); }

        /// <summary>Load keybindings from a JSON file (replaces defaults).</summary>
        public void LoadBindings(string path)
        {
            _mapping = InputMapping.LoadFromFile(path);
        }

        public Vector2 MouseScreenPos { get; private set; }
        public int ScrollDelta { get; private set; }
        public bool LeftClicked { get; private set; }
        public bool RightClicked { get; private set; }

        private MouseState _prevMouse;
        private int _prevScroll;

        public void Update()
        {
            var mouse = Mouse.GetState();
            MouseScreenPos = new Vector2(mouse.X, mouse.Y);
            ScrollDelta = mouse.ScrollWheelValue - _prevScroll;
            _prevScroll = mouse.ScrollWheelValue;

            LeftClicked = mouse.LeftButton == ButtonState.Pressed &&
                          _prevMouse.LeftButton == ButtonState.Released;
            RightClicked = mouse.RightButton == ButtonState.Pressed &&
                           _prevMouse.RightButton == ButtonState.Released;

            _prevMouse = mouse;
        }

        public bool IsPressed(InputAction action) => _mapping.IsActionPressed(action);
        public bool ShiftHeld => _mapping.IsActionPressed(InputAction.ShiftModifier);
        public bool CtrlHeld => _mapping.IsActionPressed(InputAction.CtrlModifier);
        public bool EscapePressed => _mapping.IsActionPressed(InputAction.Cancel);

        public Vector2 PanDirection => _mapping.GetPanDirection();
    }
}
