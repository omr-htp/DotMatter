using System.Net.Sockets;
using System.Threading.Channels;

namespace DotMatter.Core;

internal class UdpServer : IDisposable
{
    private UdpClient? _udpClient;
    private readonly Task _readingTask;
    private readonly Channel<byte[]> _receivedDataChannel = Channel.CreateBounded<byte[]>(5);
    private readonly CancellationTokenSource _cancellationTokenSource;

    public UdpServer()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _udpClient = new UdpClient(0);

        _readingTask = Task.Factory.StartNew(
            () => ReadAvailableDataAsync(_cancellationTokenSource.Token),
            TaskCreationOptions.LongRunning).Unwrap();
    }

    public void Close()
    {
        _cancellationTokenSource.Cancel();
        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;

        try
        {
            _readingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }
    }

    public void Dispose()
    {
        Close();
        _cancellationTokenSource.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ReadAvailableDataAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = _udpClient;
                if (client == null)
                {
                    break;
                }

                var receiveResult = await client.ReceiveAsync(ct);
                await _receivedDataChannel.Writer.WriteAsync([.. receiveResult.Buffer], ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                MatterLog.Warn("UdpServer receive error: {0}", ex.Message);
            }
        }
    }

    public async Task<byte[]> ReadAsync()
    {
        return await _receivedDataChannel.Reader.ReadAsync();
    }

    public async Task SendAsync(byte[] bytes)
    {
        await _udpClient!.SendAsync(bytes, _cancellationTokenSource.Token);
    }
}
