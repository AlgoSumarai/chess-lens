namespace ChessLens.Worker;

public sealed class WorkerActivity
{
    private long main = DateTimeOffset.UtcNow.Ticks;
    private long interactive = DateTimeOffset.UtcNow.Ticks;
    public DateTimeOffset Main => new(Interlocked.Read(ref main), TimeSpan.Zero);
    public DateTimeOffset Interactive => new(Interlocked.Read(ref interactive), TimeSpan.Zero);
    public void TouchMain() => Interlocked.Exchange(ref main, DateTimeOffset.UtcNow.Ticks);
    public void TouchInteractive() => Interlocked.Exchange(ref interactive, DateTimeOffset.UtcNow.Ticks);
}
