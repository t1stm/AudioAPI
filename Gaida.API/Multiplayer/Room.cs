using System.Net.WebSockets;
using System.Text.Json.Serialization;
using Gaida.API.Multiplayer.Handlers;

namespace Gaida.API.Multiplayer;

public class Room
{
    protected readonly ManagerService ManagerService;
    [JsonIgnore] protected readonly VirtualPlayer Player;
    [JsonIgnore] protected readonly MessageQueue Queue;

    [JsonIgnore] protected readonly UserStore Store;

    public Room(Guid guid, ManagerService managerService)
    {
        RoomID = guid;
        ManagerService = managerService;
        RoomName = guid.ToString();

        Store = new UserStore();
        Queue = new MessageQueue(Store);
        Player = new VirtualPlayer(Queue);
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

    /// <summary>
    ///     Raised once the last member is gone, so an abandoned room does not sit in the list — and
    ///     hold its queue and its clock — for the life of the process.
    /// </summary>
    /// <remarks>
    ///     ponytail: fires the moment the room empties, so a solo member whose connection blips
    ///     loses the room rather than reconnecting into it. Give the room a retain window and this
    ///     becomes "empty since when": stamp the time here and let the manager sweep the rooms that
    ///     have been empty longer than they are allowed to be.
    /// </remarks>
    [JsonIgnore] public Action? OnEmptied { get; init; }

    public ValueTask<User> GetOrAddUser(string id, WebSocket webSocket, string? initialUsername)
    {
        // the join callback closes over the username, so building it on every message — which
        // is what the unconditional call did — allocated a closure and a delegate per frame
        if (Store.GetUser(id) is { } present) return new ValueTask<User>(present);

        return Store.GetOrAddUser(id, webSocket, user =>
        {
            user.Username = initialUsername;
            return Player.Joined(user);
        });
    }

    public async Task RemoveUser(string id)
    {
        var user = Store.GetUser(id);
        // a socket can close without ever having joined — nothing left, so
        // nothing to announce and no barrier that could have changed
        if (user is null) return;

        await Store.RemoveUser(id);
        await Queue.Send($"chat System %% User '{user.ChatUsername}' left from the session.");
        await Player.UserLeft(id);

        // last one out: announced to the room first, because after this the room is gone
        if (Store.Count == 0) OnEmptied?.Invoke();
    }

    public Task HandleUserMessage(User user, string message)
    {
        return HandleUserMessage(user, message.AsMemory());
    }

    /// <summary>
    /// Takes the frame as memory over the reader's buffer: splitting a command used to cut two
    /// strings out of every inbound message, and this path runs once per keystroke-rate action
    /// from every member of every room.
    /// </summary>
    public Task HandleUserMessage(User user, ReadOnlyMemory<char> message)
    {
        var splitIndex = message.Span.IndexOf(' ');

        return splitIndex != -1
            ? HandleParameterMessages(message[..splitIndex], message[splitIndex..], user)
            : HandleParameterlessMessages(message, user);
    }

    // deliberately not async: the switch dispatches on a span, and every arm is already a Task,
    // so returning them directly skips a state machine per message
    protected Task HandleParameterMessages(ReadOnlyMemory<char> name, ReadOnlyMemory<char> value, User user)
    {
        switch (name.Span)
        {
            case "add":
                return Enqueue(value.ToString());

            case "setnext":
                return int.TryParse(value.Span, out var nextIndex) ? Player.SetNext(nextIndex) : Task.CompletedTask;

            case "skipto":
                return int.TryParse(value.Span, out var skipIndex) ? Player.SkipTo(skipIndex) : Task.CompletedTask;

            case "seek":
                return double.TryParse(value.Span, out var seekSeconds)
                    ? Player.SeekTo(seekSeconds)
                    : Task.CompletedTask;

            case "remove":
                return int.TryParse(value.Span, out var removeIndex) ? Player.Remove(removeIndex) : Task.CompletedTask;

            case "chat":
                return Queue.Send($"chat {user.ChatUsername} %% {value}");

            case "updateroom":
                return HandleUpdateRoom(value, user);

            default:
                return Task.CompletedTask;
        }
    }

    protected async Task Enqueue(string id)
    {
        var result = await ManagerService.Manager.SearchID(id);
        if (result is null) return;

        await Player.Enqueue(result);
    }

    protected Task HandleUpdateRoom(ReadOnlyMemory<char> value, User user)
    {
        var action = value.Span.Trim();
        var splitIndex = action.IndexOf(' ');
        if (splitIndex == -1 || splitIndex + 1 >= value.Length) return Task.CompletedTask;

        var parameterValue = action[splitIndex..];

        switch (action[..splitIndex])
        {
            case "name":
                RoomName = parameterValue.ToString();
                OnInfoModified?.Invoke();

                return user.SendAsync($"room name {RoomName}");

            case "description":
                RoomDescription = parameterValue.ToString();
                OnInfoModified?.Invoke();

                return user.SendAsync($"room description {RoomDescription}");

            default:
                return Task.CompletedTask;
        }
    }

    protected Task HandleParameterlessMessages(ReadOnlyMemory<char> name, User user)
    {
        switch (name.Span)
        {
            case "end":
                return Player.SetFinished(user.ID);

            case "next":
                return Player.Next();

            case "previous":
                return Player.Previous();

            case "playpause":
                return Player.TogglePlaying();

            case "stop":
                return Player.Stop();

            case "shuffle":
                return Player.Shuffle();

            case "loaded":
                return Player.SetLoaded(user.ID);

            case "sync":
                return SyncTo(user);

            default:
                return Task.CompletedTask;
        }
    }

    protected Task SyncTo(User user)
    {
        return Player.SyncTo(user);
    }
}
