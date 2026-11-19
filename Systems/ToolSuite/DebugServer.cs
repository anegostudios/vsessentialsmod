using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Vintagestory.GameContent;

public class DebugServer
{
    private TcpListener listener;
    private CancellationTokenSource cts;

    public bool IsRunning => listener != null;

    public Action<string> OnError;
    public Action<StreamWriter> OnClientConnected;
    public Action<StreamWriter> OnClientDisconnected;
    public Action<StreamWriter, string> OnReceiveMessage;

    public void Start(string host, int port)
    {
        if (listener != null)
            return;

        var address = System.Net.IPAddress.Parse(host == "localhost" ? "127.0.0.1" : host);
        cts = new CancellationTokenSource();
        listener = new TcpListener(address, port);
        listener.Start();
        _ = AcceptClientsAsync(cts.Token);
    }

    public void Stop()
    {
        if (cts == null || listener == null) return;
        cts.Cancel();
        cts = null;
        listener.Stop();
        listener = null;
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
        }
        catch (OperationCanceledException) {}
        catch (SocketException e)
        {
            OnError?.Invoke(e.Message);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        await using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream))
        await using (var writer = new StreamWriter(stream))
        {
            writer.AutoFlush = true;
            OnClientConnected?.Invoke(writer);

            try
            {
                string line;
                while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync(ct)) != null)
                {
                    OnReceiveMessage?.Invoke(writer, line);
                }
            }
            catch (OperationCanceledException) {}
            catch (IOException e)
            {
                OnError?.Invoke(e.Message);
            }
            finally
            {
                OnClientDisconnected?.Invoke(writer);
            }
        }
    }
}
