using GitWorkspace;
using System.Collections.Generic;
using System.Linq;

// Deterministic surface for dialog tests: Read() replays scripted input, Poll() never
// consumes it (Busy() polls while work runs), and every drawn frame is kept as text.
sealed class ScriptedSurface(int width, int height, params Input[] keys) : ISurface
{
    readonly Queue<Input> queue = new(keys);
    public int Width => width;
    public int Height => height;
    public Canvas? LastFrame { get; private set; }
    public List<string> Frames { get; } = [];
    public void SetBackdrop(Canvas canvas) => LastFrame = canvas;
    public void Draw(Canvas canvas) { LastFrame = canvas; Frames.Add(canvas.Plain()); }
    public Input? Poll(int milliseconds = 50) { Thread.Sleep(1); return null; }
    public Input Read() => queue.Count > 0 ? queue.Dequeue() : throw new InvalidOperationException("Scripted input exhausted");
}
