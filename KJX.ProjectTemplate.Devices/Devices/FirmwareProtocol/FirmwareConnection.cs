using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace KJX.ProjectTemplate.Devices.FirmwareProtocol;

public class FirmwareConnection(string ipAddress, ushort port) : ISupportsInitialization, INotifyPropertyChanged
{
    public required ILogger<FirmwareConnection> Logger { get; init; }

    public bool IsInitialized
    {
        get => _isInitialized;
        private set => SetField(ref _isInitialized, value);
    }

    private Socket? _socket;
    
    class CallContext
    {
        public UInt32 SequenceNumber { get; set; }
        public Action<Response> OnResponse { get; set; }
    }

    private readonly Dictionary<UInt32, CallContext> _calls = new Dictionary<UInt32, CallContext>();
    private UInt32 _sequenceNumber = 0;
    private readonly object _lock = new object();
    private bool _isInitialized;
    private AsyncSocketReceiver<FirmwareConnection>? _asyncSocketReceiver = null;

    public void Initialize()
    {
        Logger.LogInformation("Connecting to {IpAddress}:{Port}", ipAddress, port);
        IPAddress ip = IPAddress.Parse(ipAddress);
        IPEndPoint endPoint = new IPEndPoint(ip, port);

        _socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        _socket.Connect(endPoint);
        _asyncSocketReceiver = new AsyncSocketReceiver<FirmwareConnection>(_socket, Logger);
        _asyncSocketReceiver.ResponseReceived += (sender, response) =>
        {
            CallContext context;
            lock (_lock)
            {
                if (!_calls.TryGetValue(response.Header.RequestId, out context))
                {
                    Logger.LogError("Received response for unknown request id {RequestId}", response.Header.RequestId);
                    return;
                }
                context = _calls[response.Header.RequestId];
                _calls.Remove(response.Header.RequestId);
            }
            context.OnResponse(response);
        };
        IsInitialized = true;

        Logger.LogInformation("Connected");
    }

    public void Shutdown()
    {
        if (_socket is { Connected: true })
        {
            _asyncSocketReceiver.Stop();
            _socket.Shutdown(SocketShutdown.Both);
            _socket.Close();
        }
        _socket?.Dispose();
        _socket = null;
    }

    public ushort InitializationGroup => 5;

    public void SendCommandGetResponse(Request request, Action<Response> onResponse)
    {
        if (!IsInitialized)
        {
            Logger.LogError("Cannot send command before initialization");
            throw new ApplicationException("Cannot send command before initialization");
        }
        
        using var callCompleted = new ManualResetEvent(false);
        Response response = null;
        var completionHandler = (Response r) =>
        {
            response = r;
            callCompleted.Set();
        };

        lock (_lock)
        {
            var context = new CallContext { SequenceNumber = _sequenceNumber++, OnResponse = onResponse };
            request.Header = new RequestHeader { RequestId = context.SequenceNumber };
            _calls.Add(context.SequenceNumber, context);
        }
        // serialize the request and send it
        using (var stream = new MemoryStream())
        {
            int length = request.CalculateSize();
            stream.Write(BitConverter.GetBytes(length));
            request.WriteTo(stream);
            var requestBytes = stream.ToArray();
            _socket.Send(requestBytes);
        }
        callCompleted.WaitOne();
        onResponse(response);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}