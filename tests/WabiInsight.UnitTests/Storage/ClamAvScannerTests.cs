using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WabiInsight.Infrastructure.Storage;

namespace WabiInsight.UnitTests.Storage;

/// <summary>ClamAV INSTREAM client against a fake clamd: clean, infected, unreachable (fail closed) and fail-open.</summary>
public sealed class ClamAvScannerTests
{
    private static ClamAvAttachmentScanner Scanner(int port, bool failOpen = false)
        => new(Options.Create(new AttachmentScanningOptions { Provider = "ClamAv", Host = "127.0.0.1", Port = port, TimeoutSeconds = 5, FailOpen = failOpen }), NullLogger<ClamAvAttachmentScanner>.Instance);

    /// <summary>Accepts one INSTREAM session, returns the streamed bytes and answers with the given reply.</summary>
    private static async Task<(int Port, Task<byte[]> Received)> FakeClamdAsync(string reply)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var command = new byte[10];
            await stream.ReadExactlyAsync(command);
            Assert.Equal("zINSTREAM\0", Encoding.ASCII.GetString(command));
            using var payload = new MemoryStream();
            var prefix = new byte[4];
            while (true)
            {
                await stream.ReadExactlyAsync(prefix);
                var length = BinaryPrimitives.ReadInt32BigEndian(prefix);
                if (length == 0)
                {
                    break;
                }

                var chunk = new byte[length];
                await stream.ReadExactlyAsync(chunk);
                payload.Write(chunk);
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes(reply));
            listener.Stop();
            return payload.ToArray();
        });
        return (port, received);
    }

    [Fact]
    public async Task CleanFile_IsAccepted_AndStreamedInFull()
    {
        var (port, received) = await FakeClamdAsync("stream: OK\0");
        var bytes = Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray();
        var result = await Scanner(port).ScanAsync("report.pdf", new MemoryStream(bytes));
        Assert.True(result.IsClean);
        Assert.Equal(bytes, await received);
    }

    [Fact]
    public async Task InfectedFile_IsRejected_WithTheSignature()
    {
        var (port, _) = await FakeClamdAsync("stream: Eicar-Test-Signature FOUND\0");
        var result = await Scanner(port).ScanAsync("payload.zip", new MemoryStream([1, 2, 3]));
        Assert.False(result.IsClean);
        Assert.Equal("infected", result.Reason);
    }

    [Fact]
    public async Task UnreachableDaemon_FailsClosed_UnlessFailOpen()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var closed = await Scanner(port).ScanAsync("a.txt", new MemoryStream([1]));
        Assert.False(closed.IsClean);
        Assert.Equal("scanner_unavailable", closed.Reason);

        var open = await Scanner(port, failOpen: true).ScanAsync("a.txt", new MemoryStream([1]));
        Assert.True(open.IsClean);
    }

    [Fact]
    public void UnexpectedReply_IsAnError_NotAPass()
    {
        var scanner = Scanner(1);
        Assert.Equal("scanner_error", scanner.Interpret("x", "stream: Something ERROR\0").Reason);
        Assert.True(scanner.Interpret("x", "stream: OK\0").IsClean);
    }
}
