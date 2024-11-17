using System.Net.WebSockets;
using System.Text.Json.Serialization;
using Result.Objects;

namespace WebApplication3.Multiplayer;

public class Room
{
    [JsonInclude, JsonPropertyName("roomID")]
    public Guid RoomID { get; init; }
    [JsonInclude, JsonPropertyName("name")]
    public string RoomName { get; set; }
    [JsonInclude, JsonPropertyName("description")]
    public string RoomDescription { get; set; } = "";
    [JsonIgnore] 
    public Action? OnInfoModified { get; init; }
    
    [JsonIgnore]
    protected readonly UserStore Store;
    [JsonIgnore]
    protected readonly MessageQueue Queue;
    [JsonIgnore]
    protected readonly VirtualPlayer Player;
    [JsonIgnore]
    protected readonly System.Timers.Timer Timer;

    public Room(Guid guid)
    {
        RoomID = guid;
        RoomName = guid.ToString();
        
        Store = new UserStore();
        Queue = new MessageQueue(Store);
        Player = new VirtualPlayer(Queue);
        
        Timer = new System.Timers.Timer
        {
            Enabled = true,
            Interval = 133
        };

        Timer.Elapsed += Timer_Tick;
    }

    protected async void Timer_Tick(object? sender, EventArgs e)
    { 
        await Queue.Update();
    }
    
    public async Task<User> GetOrAddUser(string id, WebSocket web_socket)
    {
       return await Store.GetOrAddUser(id, web_socket, user => Player.Joined(user));
    }
    
    public async Task RemoveUser(string id)
    {
        var user = await Store.GetUser(id);
        await Store.RemoveUser(id);
        await Queue.Add($"chat System %% User \'{user.ChatUsername}\' left from the session.");
        await Player.HandleLoaded();
        await Player.HandleFinished();
    }

    public async Task HandleUserMessage(User user, string message)
    {
        var split_index = message.IndexOf(' ');
        if (split_index != -1)
        {
            await HandleParameterMessages(message[..split_index], message[split_index..], user);
            return;
        }
        
        await HandleParameterlessMessages(message, user);
    }

    protected async Task HandleParameterMessages(string name, string value, User user)
    {
        switch (name)
        {
            case "add":
                var result = await Globals.AudioManager.SearchID(value);
                if (result == Status.Error) return;
                
                await Player.Enqueue(result.GetOK());
                break;
            
            case "setnext":
                if (!int.TryParse(value, out var next_index)) return;
                await Player.SetNext(next_index);
                break;
            
            case "skipto":
                if (!int.TryParse(value, out var skip_index)) return;
                await Player.SkipTo(skip_index);
                break;
            
            case "seek":
                if (!double.TryParse(value, out var seek_seconds)) return;
                await Player.SeekTo(seek_seconds);
                break;
            
            case "remove":
                if (!int.TryParse(value, out var remove_index)) return;
                await Player.Remove(remove_index);
                break;
            
            case "chat":
                await Queue.Add($"chat {user.ChatUsername} %% {value}");
                break;
            
            case "updateroom":
                await HandleUpdateRoom(value, user);
                break;
        }
    }

    protected async Task HandleUpdateRoom(string value, User user)
    {
        var action = value.Trim();
        var split_index = action.IndexOf(' ');
        if (split_index == -1 || split_index + 1 >= value.Length) return;
                
        var parameter_key = action[..split_index];
        var parameter_value = action[split_index..];

        switch (parameter_key)
        {
            case "name":
                RoomName = parameter_value;
                OnInfoModified?.Invoke();

                await user.SendMessageAsync($"room name {RoomName}");
                break;
                    
            case "description":
                RoomDescription = parameter_value;
                OnInfoModified?.Invoke();
                
                await user.SendMessageAsync($"room description {RoomDescription}");
                break;
        }
    } 
    
    protected async Task HandleParameterlessMessages(string name, User user)
    {
        switch (name)
        {
            case "end":
                await Player.SetFinished(user);
                return;
                
            case "next":
                await Player.Next();
                return;
                
            case "previous":
                await Player.Previous();
                return;
                
            case "playpause":
                await Player.TogglePlaying();
                return;
                
            case "stop":
                await Player.Stop();
                return;
                
            case "shuffle":
                await Player.Shuffle();
                return;
            
            case "loaded":
                await Player.SetLoaded(user);
                break;
            
            case "sync":
                var time = await Player.GetCurrentTime();
                await user.SendMessageAsync($"sync {time}");
                return;
        }
    }
}