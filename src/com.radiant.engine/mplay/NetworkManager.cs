using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace com.radiant.engine.mplay;

public class NetworkManager
{
    private readonly HttpClient HttpClient;

    private readonly string BaseUrl;
    
    public NetworkManager(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        HttpClient = new HttpClient();
        HttpClient.Timeout = TimeSpan.FromSeconds(10);
    }
    
    public async Task<List<GameServerDescriptor>> GetServerListAsync()
    {
        try
        {
            var response = await HttpClient.GetAsync($"{BaseUrl}/servers/");
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<List<GameServerDescriptor>>();
            }
            
            return new List<GameServerDescriptor>();
        }
        catch
        {
            return new List<GameServerDescriptor>();
        }
    }
    
    public async Task<ServerRegisterResponse> RegisterServerAsync(GameServerDescriptor serverInfo)
    {
        try
        {
            var response = await HttpClient.PostAsJsonAsync($"{BaseUrl}/servers/", serverInfo);
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ServerRegisterResponse>();
            }
            
            return new ServerRegisterResponse { Success = false };
        }
        catch
        {
            return new ServerRegisterResponse { Success = false };
        }
    }
    
    public async Task<bool> UnregisterServerAsync(string serverId)
    {
        try
        {
            var response = await HttpClient.DeleteAsync($"{BaseUrl}/servers/{serverId}/");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
    
    public async Task<bool> PingServerAsync(string serverId, int playerCount)
    {
        try
        {
            var pingData = new { player_count = playerCount };
            var response = await HttpClient.PatchAsJsonAsync($"{BaseUrl}/servers/{serverId}/", pingData);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

public class GameServerDescriptor
{
    [JsonPropertyName("id")]
    public string Id { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; }
    
    [JsonPropertyName("ip_address")]
    public string IpAddress { get; set; }
    
    [JsonPropertyName("port")]
    public int Port { get; set; }
    
    [JsonPropertyName("max_players")]
    public int MaxPlayers { get; set; }
    
    [JsonPropertyName("player_count")]
    public int PlayerCount { get; set; }
    
    [JsonPropertyName("region")]
    public string Region { get; set; }
    
    [JsonPropertyName("game_mode")]
    public string GameMode { get; set; }
}

public class ServerRegisterResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("server_id")]
    public string ServerId { get; set; }
    
    [JsonPropertyName("error")]
    public string Error { get; set; }
}
