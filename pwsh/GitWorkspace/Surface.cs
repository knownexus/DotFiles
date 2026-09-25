namespace GitWorkspace;

public enum MouseButton { None, Left, Right }

public interface ISurface
{
    int Width { get; }
    int Height { get; }
    Canvas? LastFrame { get; }
    void Draw(Canvas canvas);
    Input? Poll(int milliseconds = 50);
    Input Read();
}

public static class MouseDecoder
{
    // Windows MOUSE_EVENT_RECORD: flags 0 press/release, 1 moved, 2 double click, 4 wheel.
    public static Input? Decode(uint buttons, uint flags, short x, short y, ref uint previous)
    {
        if (flags == 4) return new(X: x, Y: y, Wheel: (short)(buttons >> 16) > 0 ? -3 : 3);
        if (flags == 2) { previous = buttons; return (buttons & 1) != 0 ? new(X: x, Y: y, Button: MouseButton.Left, DoubleClick: true) : null; }
        uint pressed = buttons & ~previous; previous = buttons;
        if ((pressed & 1) != 0) return new(X: x, Y: y, Button: MouseButton.Left);
        if ((pressed & 2) != 0) return new(X: x, Y: y, Button: MouseButton.Right);
        return null;
    }
}
