using System.Net.WebSockets;
using System.Text.Json.Serialization;
using Gaida.API.Multiplayer.Handlers;
using Result.Objects;
using Timer = System.Timers.Timer;

namespace Gaida.API.Multiplayer;

public class Room
{
    protected readonly ManagerService ManagerService;
    [JsonIgnore] protected readonly VirtualPlayer Player;
    [JsonIgnore] protected readonly MessageQueue Queue;

    [JsonIgnore] protected readonly UserStore Store;
    [JsonIgnore] protected readonly Timer Timer;

    public Room(Guid guid, ManagerService managerService)
    {
        RoomID = guid;
        ManagerService = managerService;
        RoomName = guid.ToString();

        Store = new UserStore();
        Queue = new MessageQueue(Store);
        Player = new VirtualPlayer(Queue);

        Timer = new Timer
        {
            Enabled = true,
            Interval = 133
        };

        Timer.Elapsed += Timer_Tick;
    }

    [JsonInclude]
    [JsonPropertyName("roomID")]
    public Guid RoomID { get; init; }

    [JsonInclude]
    [JsonPropertyName("name")]
    public string RoomName { get; set; }

    [JsonInclude]
    [JsonPropertyName("description")]
    public string RoomDescription { get; set; } = "";

    [JsonIgnore] public Action? OnInfoModified { get; init; }

    protected async void Timer_Tick(object? sender, EventArgs e)
    {
        await Queue.Update();
    }

    public async Task<User> GetOrAddUser(string id, WebSocket webSocket, string? initialUsername)
    {
        return await Store.GetOrAddUser(id, webSocket, user =>
        {
            user.Username = initialUsername;
            return Player.Joined(user);
        });
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
        var splitIndex = message.IndexOf(' ');
        if (splitIndex != -1)
        {
            await HandleParameterMessages(message[..splitIndex], message[splitIndex..], user);
            return;
        }

        await HandleParameterlessMessages(message, user);
    }

    protected async Task HandleParameterMessages(string name, string value, User user)
    {
        switch (name)
        {
            case "add":
                var result = await ManagerService.Manager.SearchID(value);
                if (result == Status.Error) return;

                await Player.Enqueue(result.GetOk());
                break;

            case "setnext":
                if (!int.TryParse(value, out var nextIndex)) return;
                await Player.SetNext(nextIndex);
                break;

            case "skipto":
                if (!int.TryParse(value, out var skipIndex)) return;
                await Player.SkipTo(skipIndex);
                break;

            case "seek":
                if (!double.TryParse(value, out var seekSeconds)) return;
                await Player.SeekTo(seekSeconds);
                break;

            case "remove":
                if (!int.TryParse(value, out var removeIndex)) return;
                await Player.Remove(removeIndex);
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
        var splitIndex = action.IndexOf(' ');
        if (splitIndex == -1 || splitIndex + 1 >= value.Length) return;

        var parameterKey = action[..splitIndex];
        var parameterValue = action[splitIndex..];

        switch (parameterKey)
        {
            case "name":
                RoomName = parameterValue;
                OnInfoModified?.Invoke();

                await user.SendMessageAsync($"room name {RoomName}");
                break;

            case "description":
                RoomDescription = parameterValue;
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