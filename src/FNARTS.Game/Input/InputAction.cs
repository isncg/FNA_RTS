namespace FNARTS.Game
{
    /// <summary>Logical input actions. Game code queries these, not raw keys.</summary>
    public enum InputAction
    {
        CameraPanUp,
        CameraPanDown,
        CameraPanLeft,
        CameraPanRight,
        CameraZoomIn,
        CameraZoomOut,
        Select,           // Left mouse click
        Command,          // Right mouse click
        ShiftModifier,
        CtrlModifier,
        TogglePause,
        Cancel            // Escape
    }
}
