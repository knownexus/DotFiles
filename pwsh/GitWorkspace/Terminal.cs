using System.Runtime.InteropServices;
using System.Text;

namespace GitWorkspace;

public record Input(ConsoleKey Key = 0, char Character = '\0', bool Control = false, bool Shift = false, int X = -1, int Y = -1, int Wheel = 0)
{
    public bool Mouse => X >= 0;
}

// Small terminal surface with no startup-time package imports. Native Windows input
// provides mouse events in both Windows Terminal and conhost; Console.ReadKey is the
// keyboard fallback elsewhere. All modes are restored before launching an editor.
public sealed class Terminal : IDisposable
{
    readonly bool windows = OperatingSystem.IsWindows();
    IntPtr input, output;
    uint inputMode, outputMode;
    bool native, active;
    uint previousButtons;
    string[]? previousLines;
    int previousWidth;
    readonly Encoding oldEncoding = Console.OutputEncoding;
    public int Width => Math.Clamp(Console.WindowWidth - 1, 1, 300);
    public int Height => Math.Clamp(Console.WindowHeight, 1, 100);
    public Terminal()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
            throw new InvalidOperationException("The workspace UI needs an interactive terminal. Use gws status for redirected output.");
        Console.OutputEncoding = new UTF8Encoding(false);
        if (windows)
        {
            input = GetStdHandle(-10); output = GetStdHandle(-11);
            native = GetConsoleMode(input, out inputMode);
            GetConsoleMode(output, out outputMode);
        }
        Resume();
    }
    public void Resume()
    {
        if (active) return;
        if (native) { SetConsoleMode(input, 0x0080 | 0x0010 | 0x0008); SetConsoleMode(output, outputMode | 0x0004); }
        Console.Write("\u001b[?1049h\u001b[?25l\u001b[2J\u001b[H"); active = true; previousLines = null;
    }
    public void Suspend()
    {
        if (!active) return;
        Console.Write("\u001b[0m\u001b[?25h\u001b[?1049l");
        if (native) { SetConsoleMode(input, inputMode); SetConsoleMode(output, outputMode); }
        active = false;
    }
    public Input? Poll(int milliseconds = 50)
    {
        var until = Environment.TickCount64 + milliseconds;
        do
        {
            if (native)
            {
                if (GetNumberOfConsoleInputEvents(input, out var count) && count > 0)
                {
                    if (!ReadConsoleInputW(input, out var record, 1, out _)) return null;
                    if (record.Type == 1 && record.Down != 0)
                        return new((ConsoleKey)record.Key, record.Character, (record.Control & 0x000C) != 0, (record.Control & 0x0010) != 0);
                    if (record.Type == 2)
                    {
                        if (record.Flags == 4) return new(X: record.X, Y: record.Y, Wheel: (short)(record.Buttons >> 16) > 0 ? -3 : 3);
                        bool click = (record.Buttons & 1) != 0 && (previousButtons & 1) == 0;
                        previousButtons = record.Buttons;
                        if (click) return new(X: record.X, Y: record.Y);
                    }
                }
            }
            else if (Console.KeyAvailable)
            {
                var k = Console.ReadKey(true); return new(k.Key, k.KeyChar, k.Modifiers.HasFlag(ConsoleModifiers.Control), k.Modifiers.HasFlag(ConsoleModifiers.Shift));
            }
            Thread.Sleep(10);
        } while (Environment.TickCount64 < until);
        return null;
    }
    public Input Read()
    {
        Input? result;
        while ((result = Poll()) is null) { }
        return result;
    }
    public void Draw(Canvas canvas)
    {
        var lines = canvas.Ansi().Split("\r\n");
        var update = new StringBuilder();
        if (previousLines?.Length != lines.Length || previousWidth != canvas.Width) { update.Append("\u001b[2J"); previousLines = null; }
        for (int y = 0; y < lines.Length; y++)
            if (previousLines is null || previousLines[y] != lines[y]) update.Append($"\u001b[{y + 1};1H").Append(lines[y]);
        if (update.Length > 0) Console.Write(update.Append("\u001b[0m").ToString());
        previousLines = lines; previousWidth = canvas.Width;
    }
    public void Dispose() { Suspend(); Console.OutputEncoding = oldEncoding; }

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    struct Record
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(4)] public int Down;
        [FieldOffset(10)] public ushort Key;
        [FieldOffset(14)] public char Character;
        [FieldOffset(16)] public uint Control;
        [FieldOffset(4)] public short X;
        [FieldOffset(6)] public short Y;
        [FieldOffset(8)] public uint Buttons;
        [FieldOffset(16)] public uint Flags;
    }
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int which);
    [DllImport("kernel32.dll")] static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll")] static extern bool SetConsoleMode(IntPtr handle, uint mode);
    [DllImport("kernel32.dll")] static extern bool GetNumberOfConsoleInputEvents(IntPtr handle, out uint count);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool ReadConsoleInputW(IntPtr handle, out Record buffer, uint length, out uint read);
}

public enum Ink { Text, Muted, Accent, Good, Warning, Bad, Heading }
public sealed class Canvas(int width, int height)
{
    readonly char[,] chars = new char[height, width];
    readonly Ink[,] inks = new Ink[height, width];
    readonly bool[,] highlights = new bool[height, width];
    public int Width => width;
    public int Height => height;
    public static string Clean(string text) => string.Concat(text.Select(c => char.IsControl(c) ? ' ' : c));
    public void Text(int x, int y, string text, Ink ink = Ink.Text, int? limit = null, bool highlight = false)
    {
        if (y < 0 || y >= height) return;
        int end = Math.Min(width, x + (limit ?? width));
        foreach (char c in Clean(text)) { if (x >= end) break; if (x >= 0) { chars[y, x] = c; inks[y, x] = ink; highlights[y, x] = highlight; } x++; }
    }
    public void Line(int y, int start = 0, int? length = null) => Text(start, y, new string('─', Math.Max(0, Math.Min(length ?? width, width - start))), Ink.Muted);
    public void Fill(int x, int y, int length, bool highlight = true) => Text(x, y, new string(' ', Math.Max(0, length)), Ink.Text, length, highlight);
    public string Plain()
    {
        var b = new StringBuilder();
        for (int y = 0; y < height; y++) { for (int x = 0; x < width; x++) b.Append(chars[y, x] == '\0' ? ' ' : chars[y, x]); b.AppendLine(); }
        return b.ToString();
    }
    public string Ansi()
    {
        var b = new StringBuilder(); Ink? previous = null; bool? highlight = null;
        for (int y = 0; y < height; y++)
        {
            previous = null; highlight = null;
            for (int x = 0; x < width; x++)
            {
                if (inks[y, x] != previous || highlights[y, x] != highlight)
                {
                    previous = inks[y, x]; highlight = highlights[y, x];
                    string colour = previous switch { Ink.Muted => "38;5;245", Ink.Accent => "38;5;117", Ink.Good => "38;5;114", Ink.Warning => "38;5;221", Ink.Bad => "38;5;210", Ink.Heading => "1;38;5;153", _ => "38;5;252" };
                    b.Append("\u001b[0;").Append(colour).Append(highlight == true ? ";48;5;237m" : "m");
                }
                b.Append(chars[y, x] == '\0' ? ' ' : chars[y, x]);
            }
            if (y != height - 1) b.Append("\r\n");
        }
        return b.Append("\u001b[0m").ToString();
    }
}
