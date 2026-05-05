using DotMatter.Core;

namespace DotMatter.Tests;

[TestFixture]
public class ThreadSafetyTests
{
    [Test]
    public void GlobalCounter_ParallelIncrement_NoDuplicates()
    {
        // Run 10,000 increments across multiple threads and ensure all values are unique
        const int count = 10_000;
        var results = new uint[count];

        Parallel.For(0, count, i =>
        {
            results[i] = GlobalCounter.Counter;
        });

        var distinct = results.Distinct().Count();
        Assert.That(distinct, Is.EqualTo(count), "GlobalCounter produced duplicate values under contention");
    }

    [Test]
    public void GlobalCounter_SequentialIncrement_Monotonic()
    {
        var prev = GlobalCounter.Counter;
        for (int i = 0; i < 100; i++)
        {
            var next = GlobalCounter.Counter;
            Assert.That(next, Is.GreaterThan(prev));
            prev = next;
        }
    }
}
