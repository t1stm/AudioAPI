using System.Net.WebSockets;
using Result.Objects;

namespace WebApplication3.Multiplayer;

public class Room
{
    public Guid RoomID { get; init; }
    public string RoomName { get; set; }
    public string RoomDescription { get; set; } = "";

    protected readonly UserStore Store;
    protected readonly MessageQueue Queue;
    protected readonly VirtualPlayer Player;

    public Room(Guid guid)
    {
        RoomID = guid;
        RoomName = guid.ToString();
        
        Store = new UserStore();
        Queue = new MessageQueue(Store);
        Player = new VirtualPlayer(Queue);
    }
    
    public async Task<User> GetOrAddUser(string id, WebSocket web_socket)
    {
       return await Store.GetOrAddUser(id, web_socket);
    }
    
    public async Task RemoveUser(string id)
    {
        var user = await Store.GetUser(id);
        await Store.RemoveUser(id);
        await Queue.Add($"chat User \'{user.ChatUsername}\' was removed from the room.");
    }

    public async Task HandleUserMessage(User user, string message)
    {
        var split_index = message.IndexOf(' ');
        if (split_index != -1)
        {
            await HandleParameterMessages(message[..split_index], message[split_index..], user);
            return;
        }
        
        await HandleParameterlessMessages(message[..split_index], user);
    }

    protected async Task HandleParameterMessages(string name, string value, User user)
    {
        switch (name)
        {
            case "add":
                var result = await Globals.AudioManager.SearchID(value);
                if (result == Status.Error) return;
                
                Player.Items.Add(result.GetOK());
                break;
            
            case "seek":
                if (!double.TryParse(value, out var seek_seconds)) return;
                await Queue.Add($"seek {seek_seconds}");
                break;
            
            case "chat":
                await Queue.Add($"chat [{user.ChatUsername}]: {value}");
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
        }
    }
}