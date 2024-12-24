using System.Collections.Concurrent;
using Avalonia.Logging;
using Microsoft.Extensions.Logging;

namespace KJX.ProjectTemplate.Devices.FirmwareProtocol;

using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;


public class AsyncSocketReceiver<T>
{
    private readonly Socket _socket;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _receiveTask;
    private readonly Task _eventDispatchTask;
    private Queue<Response> _responses = new();
    private readonly object _lock = new();
    private AutoResetEvent _responsesAvailable = new(false);
    private ILogger<T> _logger;

    public event EventHandler<Response>? ResponseReceived;

    public AsyncSocketReceiver(Socket socket, ILogger<T> logger)
    {
        _socket = socket;
        _logger = logger;
        _receiveTask = Task.Run(ReceiveLoop, _cancellationTokenSource.Token);
        _eventDispatchTask = Task.Run(EventDispatchLoop, _cancellationTokenSource.Token);
    }
    
    private void EventDispatchLoop()
    {
        try
        {
            while (_responsesAvailable.WaitOne())
            {
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                    return;
                Response[] responses = null;
                lock (_lock)
                {
                    if (_responses.Count > 0)
                    {
                        responses = _responses.ToArray();
                        _responses.Clear();
                    }
                }

                if (responses != null)
                {
                    foreach (var response in responses)
                    {
                        try
                        {
                            ResponseReceived?.Invoke(this, response);
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Error in EventDispatchLoop");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in EventDispatchLoop");
        }
    }

    private void ReceiveLoop()
    {
        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                // Read the length (4 bytes)
                var lengthBuffer = new byte[4];
                int received = 0;
                ReadBytesBlocking(received, lengthBuffer);
                int length = BitConverter.ToInt32(lengthBuffer, 0);

                // Read the serialized protobuf object
                var dataBuffer = new byte[length];
                ReadBytesBlocking(length, dataBuffer);
                
                // Parse the Response object
                var response = Response.Parser.ParseFrom(dataBuffer);
                
                lock (_lock)
                {
                    _responses.Enqueue(response);
                    _responsesAvailable.Set();
                }
            }
        }
        catch (Exception ex)
        {
            if (!_cancellationTokenSource.Token.IsCancellationRequested)
                _logger.LogError(ex, "Error in ReceiveLoop");
        }
    }

    private void ReadBytesBlocking(int bytesToRead, byte[] lengthBuffer)
    {
        int received = 0;
        while (received < bytesToRead)
        {
            int bytesRead = _socket.Receive(lengthBuffer, received, bytesToRead - received, SocketFlags.None);
            if (bytesRead == 0)
            {
                throw new SocketException((int)SocketError.ConnectionReset);
            }
            received += bytesRead;
        }
    }

    private async Task ReceiveAsync(byte[] buffer, int length)
    {
        int received = 0;
        while (received < length)
        {
            int bytesRead = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer, received, length - received), SocketFlags.None);
            if (bytesRead == 0)
            {
                throw new SocketException((int)SocketError.ConnectionReset);
            }
            received += bytesRead;
        }
    }

    public void Stop()
    {
        _cancellationTokenSource.Cancel();
        _responsesAvailable.Set();
        _socket.Close();
        _receiveTask.Wait();
        _eventDispatchTask.Wait();
    }
}