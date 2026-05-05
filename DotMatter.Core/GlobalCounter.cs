namespace DotMatter.Core;

class GlobalCounter
{
    private static uint _counter;

    public static uint Counter => Interlocked.Increment(ref _counter);
}
