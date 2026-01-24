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
    private readonly string ServerName;

    private readonly string ServerIp;

    private readonly int ServerPort;

    private readonly NetworkManager LobbyManager;

    private readonly List<NetworkClient> Clients = new List<NetworkClient>();

    private readonly object Lock = new object();

    private TcpListener TcpListener;

    private bool IsRunning;

    private string ServerId;
    
    public GameServer(string serverName, string serverIp, int serverPort, NetworkManager lobbyManager)
    {
        ServerName = serverName;
        ServerIp = serverIp;
        ServerPort = serverPort;
        LobbyManager = lobbyManager;
    }

    public async Task<bool> RegisterWithDirectoryAsync()
    {
        var serverInfo = new GameServerDescriptor
        {
            Name = ServerName,
            IpAddress = ServerIp,
            Port = ServerPort,
            MaxPlayers = 100,
            PlayerCount = 0,
            Region = "default",
            GameMode = "standard"
        };

        var response = await LobbyManager.RegisterServerAsync(serverInfo);

        if (response.Success)
        {
            ServerId = response.ServerId;
            return true;
        }

        return false;
    }

    public void Start()
    {
        TcpListener = new TcpListener(IPAddress.Any, ServerPort);
        TcpListener.Start();
        IsRunning = true;

        Task.Run(() => AcceptClientsAsync());
    }

    public async Task StopAsync()
    {
        await LobbyManager.UnregisterServerAsync(ServerId);

        IsRunning = false;
        TcpListener?.Stop();

        lock (Lock)
        {
            foreach (var client in Clients)
            {
                client.Disconnect();
            }
            Clients.Clear();
        }
    }

    private async Task AcceptClientsAsync()
    {
        while (IsRunning)
        {
            try
            {
                TcpClient tcpClient = await TcpListener.AcceptTcpClientAsync();
                var clientConnection = new NetworkClient(tcpClient, this);

                lock (Lock)
                {
                    Clients.Add(clientConnection);
                }

                clientConnection.Listen();

                await LobbyManager.PingServerAsync(ServerId, Clients.Count);
            }
            catch when (!IsRunning)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accepting client: {ex.Message}");
            }
        }
    }

    public void RemoveClient(NetworkClient client)
    {
        lock (Lock)
        {
            Clients.Remove(client);

            Task.Run(() => LobbyManager.PingServerAsync(ServerId, Clients.Count));
        }
    }

    public void HandleMessage(NetworkClient client, NetworkMessage message)
    {
        switch (message.Type)
        {
            case "Register":
                var registerData = JsonSerializer.Deserialize<Dictionary<string, string>>(message.Data);
                client.PlayerId = registerData["playerId"];
                SendToClient(client, new NetworkMessage { Type = "Registered", Data = "Success" });
                break;

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
        lock (Lock)
        {
            foreach (var client in Clients)
            {
                if (client != exclude)
                {
                    SendToClient(client, message);
                }
            }
        }
    }
}