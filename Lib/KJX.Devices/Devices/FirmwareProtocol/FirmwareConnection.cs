using System.Net;
using System.Net.Sockets;
using Google.Protobuf;
using KJX.Devices.Logic;
using Microsoft.Extensions.Logging;

namespace KJX.Devices.FirmwareProtocol;

public class FirmwareConnection(string ipAddress, ushort port) : DeviceBase
{
    class CallContext
    {
        public UInt32 SequenceNumber { get; set; }
        public Action<Response> OnResponse { get; set; }
    }
    public required ILogger<FirmwareConnection> Logger { get; init; }
    public new ushort InitializationGroup => 5;

    private Socket _socket;
    private readonly Dictionary<UInt32, CallContext> _calls = new Dictionary<UInt32, CallContext>();
    private UInt32 _sequenceNumber = 0;
    private readonly object _lock = new object();
    private AsyncSocketReceiver<FirmwareConnection> _asyncSocketReceiver = null;

    protected override void DoInitialize()
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

        Logger.LogInformation("Connected");
    }

    protected override void DoShutdown()
    {
        if (_socket is { Connected: true })
        {
            _asyncSocketReceiver?.Stop();
            // the async reciever closes the socket
        }

        _socket?.Dispose();
        _socket = null;
    }
    
    /// <summary>
    /// A simple wrapper that sends a request and waits for a Nack/Ack response
    /// </summary>
    public void SendRequest(NodeId recipient, Request request)
    {
        Response response = null;
        SendCommandGetResponse(recipient, request, (r) => { response = r; });
        if (response is { Nak: not null })
        {
            Logger.LogError("Failed to send request: {ErrorCode}", response.Nak.ErrorCode);
            throw new ApplicationException("Failed to send request");
        }

        if (response == null || response.Ack is null)
        {
            Logger.LogError("Failed to send request: no response");
            throw new ApplicationException("Failed to send request");
        }
    }
    
    public Response GetResponse(NodeId recipient, Request request)
    {
        Response response = null;
        SendCommandGetResponse(recipient, request, (r) => { response = r; });
        return response;
    }

    public void SendCommandGetResponse(NodeId recipient, Request request, Action<Response> onResponse)
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
            var context = new CallContext { SequenceNumber = _sequenceNumber++, OnResponse = completionHandler };
            request.Header = new RequestHeader
                { RequestId = context.SequenceNumber, SourceNodeId = NodeId.Pc, TargetNodeId = recipient };
            _calls.Add(context.SequenceNumber, context);
        }

        // serialize the request and send it
        using (var stream = new MemoryStream())
        {
            int length = request.CalculateSize();
            if (length > UInt16.MaxValue)
            {
                Logger.LogError("Request too large: {Length}", length);
                throw new ApplicationException("Request too large");
            }
            UInt16 shortLength = (UInt16)length;
            stream.Write(BitConverter.GetBytes(shortLength));
            request.WriteTo(stream);
            var requestBytes = stream.ToArray();
            _socket.Send(requestBytes);
        }

        callCompleted.WaitOne();
        onResponse(response);
    }
    
    public FirmwareVersions GetFirmwareVersions()
    {
        FirmwareVersions firmwareVersions = null;
        SendCommandGetResponse(
            NodeId.Main, 
            new Request { GetFirmwareVersions = new GetFirmwareVersions() },
        (response) => { firmwareVersions = response.FirmwareVersions; });
        return firmwareVersions;
    }
}