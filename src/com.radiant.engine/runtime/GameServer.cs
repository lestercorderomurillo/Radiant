using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using com.radiant.engine.mplay;

namespace com.radiant.engine.runtime;

public class GameServer
{
    private readonly string _serverName;

    private readonly string _serverIp;

    private readonly int _serverPort;

    private readonly NetworkManager _lobbyManager;

    private readonly List<NetworkClient> _clients = new List<NetworkClient>();

    private readonly object _lock = new object();
    
    private TcpListener _tcpListener;

    private bool _isRunning;

    private string _serverId;
    
    public GameServer(string serverName, string serverIp, int serverPort, NetworkManager lobbyManager)
    {
        _serverName = serverName;
        _serverIp = serverIp;
        _serverPort = serverPort;
        _lobbyManager = lobbyManager;
    }
    
    public async Task<bool> RegisterWithDirectoryAsync()
    {
        var serverInfo = new GameServerDescriptor
        {
            Name = _serverName,
            IpAddress = _serverIp,
            Port = _serverPort,
            MaxPlayers = 100,
            PlayerCount = 0,
            Region = "default",
            GameMode = "standard"
        };
        
        var response = await _lobbyManager.RegisterServerAsync(serverInfo);
        
        if (response.Success)
        {
            _serverId = response.ServerId;
            return true;
        }
        
        return false;
    }
    
    public void Start()
    {
        _tcpListener = new TcpListener(IPAddress.Any, _serverPort);
        _tcpListener.Start();
        _isRunning = true;
        
        Task.Run(() => AcceptClientsAsync());
    }
    
    public async Task StopAsync()
    {
        await _lobbyManager.UnregisterServerAsync(_serverId);
        
        _isRunning = false;
        _tcpListener?.Stop();
        
        lock (_lock)
        {
            foreach (var client in _clients)
            {
                client.Disconnect();
            }
            _clients.Clear();
        }
    }
    
    private async Task AcceptClientsAsync()
    {
        while (_isRunning)
        {
            try
            {
                TcpClient tcpClient = await _tcpListener.AcceptTcpClientAsync();
                var clientConnection = new NetworkClient(tcpClient, this);
                
                lock (_lock)
                {
                    _clients.Add(clientConnection);
                }
                
                clientConnection.Listen();
                
                // Update player count
                await _lobbyManager.PingServerAsync(_serverId, _clients.Count);
            }
            catch when (!_isRunning)
            {
                // Ignore exceptions during shutdown
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accepting client: {ex.Message}");
            }
        }
    }
    
    public void RemoveClient(NetworkClient client)
    {
        lock (_lock)
        {
            _clients.Remove(client);
            
            // Update player count
            Task.Run(() => _lobbyManager.PingServerAsync(_serverId, _clients.Count));
        }
    }
    
    public void HandleMessage(NetworkClient client, NetworkMessage message)
    {
        // Process received messages
        switch (message.Type)
        {
            case "Register":
                // Register client
                var registerData = JsonSerializer.Deserialize<Dictionary<string, string>>(message.Data);
                client.PlayerId = registerData["playerId"];
                SendToClient(client, new NetworkMessage { Type = "Registered", Data = "Success" });
                break;
                
            // Add more message types as needed
            
            default:
                Console.WriteLine($"Unknown message type: {message.Type}");
                break;
        }
    }
    
    public void SendToClient(NetworkClient client, NetworkMessage message)
    {
        try
        {
            client.SendMessage(message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending message to client: {ex.Message}");
            RemoveClient(client);
        }
    }
    
    public void BroadcastMessage(NetworkMessage message, NetworkClient exclude = null)
    {
        lock (_lock)
        {
            foreach (var client in _clients)
            {
                if (client != exclude)
                {
                    SendToClient(client, message);
                }
            }
        }
    }
}