using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AsicFarm.MinerClients.Whatsminer;

public sealed class WhatsminerTcpTransport : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private bool _disposed;

    public WhatsminerTcpTransport(string host, int port = 4433)
    {
        _host = host;
        _port = port;
    }

    public string Host => _host;
    public int Port => _port;
    public bool IsConnected => _tcp?.Connected == true && _stream is not null;

    public async Task ConnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (IsConnected || _disposed) return;

        await _operationLock.WaitAsync(ct);
        try
        {
            if (IsConnected || _disposed) return;

            CloseConnection();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            _tcp = new TcpClient();
            try
            {
                await _tcp.ConnectAsync(_host, _port, cts.Token);
                _stream = _tcp.GetStream();
            }
            catch
            {
                CloseConnection();
                throw;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<JsonDocument> SendCommandAsync(object payload, TimeSpan timeout, CancellationToken ct)
    {
        await _operationLock.WaitAsync(ct);
        try
        {
            if (_stream is null || _disposed)
                throw new InvalidOperationException($"Whatsminer {_host}: Transport not connected.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.ASCII.GetBytes(json);

            var lengthPrefix = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, bytes.Length);

            try
            {
                await _stream.WriteAsync(lengthPrefix, cts.Token);
                await _stream.WriteAsync(bytes, cts.Token);
                await _stream.FlushAsync(cts.Token);

                var responseLengthBytes = new byte[4];
                await ReadExactAsync(responseLengthBytes, cts.Token);
                var responseLength = BinaryPrimitives.ReadInt32LittleEndian(responseLengthBytes);

                if (responseLength <= 0 || responseLength > 1024 * 1024)
                    throw new IOException($"Invalid response length: {responseLength}");

                var responseBytes = new byte[responseLength];
                await ReadExactAsync(responseBytes, cts.Token);

                var responseJson = Encoding.ASCII.GetString(responseBytes);
                return JsonDocument.Parse(responseJson);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                CloseConnection();
                throw new TimeoutException($"Whatsminer {_host}:{_port}: command timeout ({timeout.TotalMilliseconds:F0} ms)");
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException)
            {
                CloseConnection();
                throw;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task ReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await _stream!.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0)
                throw new IOException("Connection closed by miner");
            offset += read;
        }
    }

    private void CloseConnection()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _stream = null;
        _tcp = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Ждем до 2 секунд, чтобы завершились текущие операции, и только потом рвем связь
        if (await _operationLock.WaitAsync(TimeSpan.FromSeconds(2)))
        {
            try
            {
                CloseConnection();
            }
            finally
            {
                _operationLock.Release();
            }
        }
        else
        {
            // Если операция зависла, принудительно закрываем
            CloseConnection();
        }
    }
}