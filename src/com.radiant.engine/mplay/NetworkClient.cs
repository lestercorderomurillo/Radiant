using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using com.radiant.engine.runtime;

namespace com.radiant.engine.mplay;

public class NetworkClient
{
    private readonly TcpClient TcpClient;

    private readonly GameServer GameServer;

    private NetworkStream Stream;
    
    private bool IsListening;
    
    public string PlayerId { get; set; }
    
    public NetworkClient(TcpClient tcpClient, GameServer server)
    {
        TcpClient = tcpClient;
        GameServer = server;
        Stream = tcpClient.GetStream();
        PlayerId = string.Empty;
    }
    
    public void Listen()
    {
        IsListening = true;
        Task.Run(() => ProcessMessagesAsync());
    }
    
    public void Disconnect()
    {
        IsListening = false;
        
        try
        {
            Stream?.Close();
            TcpClient?.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error disconnecting client: {ex.Message}");
        }
    }
    
    private async Task ProcessMessagesAsync()
    {
        byte[] buffer = new byte[4096];
        
        try
        {
            while (IsListening && TcpClient.Connected)
            {
                int bytesRead = await Stream.ReadAsync(buffer, 0, buffer.Length);
                
                if (bytesRead == 0)
                {
                    // Client disconnected
                    break;
                }
                
                string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var message = JsonSerializer.Deserialize<NetworkMessage>(json);
                
                if (message != null)
                {
                    GameServer.HandleMessage(this, message);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing client messages: {ex.Message}");
        }
        finally
        {
            GameServer.RemoveClient(this);
            Disconnect();
        }
    }
    
    public void SendMessage(NetworkMessage message)
    {
        if (!TcpClient.Connected) return;
        
        string json = JsonSerializer.Serialize(message);
        byte[] data = Encoding.UTF8.GetBytes(json);
        
        Stream.Write(data, 0, data.Length);
    }
}