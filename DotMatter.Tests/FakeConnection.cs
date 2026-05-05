using DotMatter.Core;

namespace DotMatter.Tests;

internal class FakeConnection : IConnection
{
    public event EventHandler ConnectionClosed = delegate { };

    public Task<byte[]> ReadAsync(CancellationToken token)
    {
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task SendAsync(byte[] message)
    {
        return Task.CompletedTask;
    }

    public void Close()
    {
        // Do nothing
    }

    public IConnection OpenConnection()
    {
        return new FakeConnection();
    }
}
