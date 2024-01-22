using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;

namespace Evolits.ServerLib;

public delegate void IncomingPacketEvent(IPEndPoint client, Memory<byte> packet);

public delegate void ConnectEvent(IPEndPoint client);

public delegate void DisconnectEvent(IPEndPoint client, bool disconnectedByServer);

/// <summary>
/// Container for the WebSocket handle and CancellationTokenSource for a connection.
/// </summary>
/// <param name="webSocketHandle"></param>
public class Connection(WebSocket webSocketHandle)
{
    public readonly WebSocket WebSocketHandle = webSocketHandle;
    public readonly CancellationTokenSource ConnectionCancellationSource = new();
}

/// <summary>
/// Abstraction for WebSockets.
/// This may only be used once, create another instance to restart a listener.
/// Must be disposed after use.
/// </summary>
public class WebSocketServer : IDisposable
{
    public const string SubProtocol = "evolits-v2";
    
    public int Port { get; private set; }

    private readonly ConcurrentDictionary<IPEndPoint, Connection?> _connections = [];

    private readonly CancellationTokenSource _globalCancelSource = new();

    /// <summary>
    /// Event for receiving a packet from a connected client.
    /// </summary>
    public event IncomingPacketEvent? OnConnectionReceive;

    /// <summary>
    /// Event for a client connecting.
    /// </summary>
    public event ConnectEvent? OnConnect;

    /// <summary>
    /// Event for a client disconnecting or being disconnected.
    /// Does not get called when server is stopping.
    /// </summary>
    public event DisconnectEvent? OnDisconnect;
    
    public WebSocketServer SetPort(int port)
    {
        Port = port;
        return this;
    }

    /// <summary>
    /// Starts the WebSocket server.
    /// SetPort must be called first.
    /// Returns false if the server has been stopped. WebSocketServer instances cannot be restarted.
    /// </summary>
    /// <returns></returns>
    public bool Start()
    {
        //Start listener task.
        if (_globalCancelSource.IsCancellationRequested) return false;
        _ = ListenerTask(Port, _globalCancelSource.Token);
        return true;
    }

    /// <summary>
    /// Stops the WebSocket server.
    /// </summary>
    public void Stop()
    {
        _globalCancelSource.Cancel();
    }

    /// <summary>
    /// Sends a binary packet to a client.
    /// </summary>
    /// <param name="client"></param>
    /// <param name="packet"></param>
    /// <returns></returns>
    public bool Send(IPEndPoint client, Memory<byte> packet)
    {
        if (!_connections.TryGetValue(client, out Connection? connection)) return false;
        if (connection == null || connection.WebSocketHandle.State != WebSocketState.Open || connection.ConnectionCancellationSource.IsCancellationRequested) return false;
#pragma warning disable CA2012
        _ = connection.WebSocketHandle.SendAsync(packet, WebSocketMessageType.Binary, WebSocketMessageFlags.None,
#pragma warning restore CA2012
            connection.ConnectionCancellationSource.Token);
        return true;
    }
    
    /// <summary>
    /// Disconnects a certain client.
    /// Returns false if the client isn't already connected
    /// </summary>
    /// <param name="client"></param>
    /// <returns></returns>
    public bool Disconnect(IPEndPoint client)
    {
        if (!_connections.TryGetValue(client, out Connection? value)) return false;
        value?.ConnectionCancellationSource.Cancel();
        return true;
    }
    
    //Listens on port and starts up connection handling when connections are received.
    private async Task ListenerTask(int port, CancellationToken token)
    {
        var httpListener = new HttpListener();
        httpListener.Prefixes.Add("http://*:" + port + "/");
        httpListener.Start();
        
        //Connection accepting loop.
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context = await httpListener.GetContextAsync();
            if (!context.Request.IsWebSocketRequest) continue;
            HttpListenerWebSocketContext webSocketContext = await context.AcceptWebSocketAsync(SubProtocol);
            WebSocket webSocketHandle = webSocketContext.WebSocket;
            
            //Remove existing connection from endpoint if it exists.
            IPEndPoint remoteEndPoint = context.Request.RemoteEndPoint;
            if (_connections.ContainsKey(remoteEndPoint))
                _connections.Remove(remoteEndPoint, out _);
            
            //Start connection task for the connection.
            var connection = new Connection(webSocketHandle);
            _connections.TryAdd(remoteEndPoint, connection);
            _ = ConnectionTask(remoteEndPoint, webSocketHandle, connection.ConnectionCancellationSource.Token, connection.ConnectionCancellationSource, _globalCancelSource.Token);
        }
    }
    
    //Handle connection including packet receiving and disconnecting.
    private async Task ConnectionTask(IPEndPoint clientEndPoint, WebSocket webSocketHandle, CancellationToken cancellationToken, CancellationTokenSource cancellationTokenSource, CancellationToken globalCancellationToken)
    {
        OnConnect?.Invoke(clientEndPoint);
        
        //Receive packets.
        while (!cancellationToken.IsCancellationRequested && !globalCancellationToken.IsCancellationRequested && webSocketHandle.State == WebSocketState.Open)
        {
            Memory<byte> buffer = new();
            await webSocketHandle.ReceiveAsync(buffer, cancellationToken);
            OnConnectionReceive?.Invoke(clientEndPoint, buffer);
        }

        //Only call disconnect event if it isn't because the server is stopping.
        if (!globalCancellationToken.IsCancellationRequested)
        {
            OnDisconnect?.Invoke(clientEndPoint, cancellationToken.IsCancellationRequested);
        }

        //More precise messages are expected to be sent normally before disconnecting the client.
        string description = globalCancellationToken.IsCancellationRequested
            ? "Server closing."
            : "Server disconnected client.";

        await webSocketHandle.CloseAsync(WebSocketCloseStatus.NormalClosure, description, CancellationToken.None);

        //Dispose resources.
        webSocketHandle.Dispose();
        cancellationTokenSource.Dispose();
        _connections.Remove(clientEndPoint, out _);
    }

    public void Dispose()
    {
        _globalCancelSource.Dispose();
    }
}