using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using FNARTS.Core;

namespace FNARTS.Game
{
    /// <summary>
    /// Maps hardware keys/mouse buttons to logical InputActions.
    /// Supports JSON-based keybinding configuration.
    /// </summary>
    public class InputMapping
    {
        private readonly Dictionary<InputAction, Keys[]> _keyBindings = new();
        private readonly Dictionary<InputAction, MouseButton[]> _mouseBindings = new();

        public InputMapping()
        {
            // Default bindings
            Bind(InputAction.CameraPanUp, Keys.W, Keys.Up);
            Bind(InputAction.CameraPanDown, Keys.S, Keys.Down);
            Bind(InputAction.CameraPanLeft, Keys.A, Keys.Left);
            Bind(InputAction.CameraPanRight, Keys.D, Keys.Right);
            Bind(InputAction.ShiftModifier, Keys.LeftShift, Keys.RightShift);
            Bind(InputAction.CtrlModifier, Keys.LeftControl, Keys.RightControl);
            Bind(InputAction.TogglePause, Keys.Escape);
            Bind(InputAction.Cancel, Keys.Escape);
            BindMouse(InputAction.Select, MouseButton.Left);
            BindMouse(InputAction.Command, MouseButton.Right);
        }

        public void Bind(InputAction action, params Keys[] keys)
            => _keyBindings[action] = keys;

        public void BindMouse(InputAction action, params MouseButton[] buttons)
            => _mouseBindings[action] = buttons;

        /// <summary>
        /// Load keybindings from a JSON file.  Merges with defaults:
        /// any actions not in the file keep their hardcoded bindings.
        /// Expected format: { "actions": { "cameraPanUp": ["KeyW","ArrowUp"], ... } }
        /// </summary>
        public static InputMapping LoadFromFile(string path)
        {
            var mapping = new InputMapping();

            if (!System.IO.File.Exists(path))
                return mapping;

            try
            {
                var json = System.IO.File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("actions", out var actions))
                {
                    foreach (var prop in actions.EnumerateObject())
                    {
                        // Match camelCase JSON keys to InputAction enum names
                        if (System.Enum.TryParse<InputAction>(prop.Name, ignoreCase: true, out var action))
                        {
                            var keyList = new System.Collections.Generic.List<Keys>();
                            var mouseList = new System.Collections.Generic.List<MouseButton>();
                            foreach (var item in prop.Value.EnumerateArray())
                            {
                                var s = item.GetString();
                                if (s == null) continue;
                                // Mouse buttons
                                if (s == "MouseLeft") mouseList.Add(MouseButton.Left);
                                else if (s == "MouseRight") mouseList.Add(MouseButton.Right);
                                else if (s == "MouseMiddle") mouseList.Add(MouseButton.Middle);
                                // Scroll
                                else if (s == "ScrollUp" || s == "ScrollDown")
                                { /* handled by RTSInput.ScrollDelta */ }
                                // Keyboard keys
                                else if (System.Enum.TryParse<Keys>(s, ignoreCase: true, out var key))
                                    keyList.Add(key);
                            }
                            if (keyList.Count > 0)
                                mapping._keyBindings[action] = keyList.ToArray();
                            if (mouseList.Count > 0)
                                mapping._mouseBindings[action] = mouseList.ToArray();
                        }
                    }
                }
                GameLogger.Info($"Loaded keybindings from {path}");
            }
            catch (System.Exception ex)
            {
                GameLogger.Warn($"Failed to load keybindings {path}: {ex.Message}");
            }

            return mapping;
        }

        public bool IsActionPressed(InputAction action)
        {
            var kb = Keyboard.GetState();
            if (_keyBindings.TryGetValue(action, out var keys))
            {
                foreach (var k in keys)
                    if (kb.IsKeyDown(k)) return true;
            }
            var ms = Mouse.GetState();
            if (_mouseBindings.TryGetValue(action, out var buttons))
            {
                foreach (var b in buttons)
                {
                    bool down = b switch
                    {
                        MouseButton.Left => ms.LeftButton == ButtonState.Pressed,
                        MouseButton.Right => ms.RightButton == ButtonState.Pressed,
                        MouseButton.Middle => ms.MiddleButton == ButtonState.Pressed,
                        _ => false
                    };
                    if (down) return true;
                }
            }
            return false;
        }

        /// <summary>Get camera pan direction vector from bound keys.</summary>
        public Microsoft.Xna.Framework.Vector2 GetPanDirection()
        {
            var dir = Microsoft.Xna.Framework.Vector2.Zero;
            if (IsActionPressed(InputAction.CameraPanUp)) dir.Y -= 1;
            if (IsActionPressed(InputAction.CameraPanDown)) dir.Y += 1;
            if (IsActionPressed(InputAction.CameraPanLeft)) dir.X -= 1;
            if (IsActionPressed(InputAction.CameraPanRight)) dir.X += 1;
            return dir;
        }
    }

    public enum MouseButton { Left, Right, Middle }
}
