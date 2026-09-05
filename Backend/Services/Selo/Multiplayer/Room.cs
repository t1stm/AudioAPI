using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Selo.Multiplayer.Handlers;

namespace Selo.Multiplayer;

public class Room
{
    protected readonly HttpClient Gaida;
    [JsonIgnore] protected readonly VirtualPlayer Player;
    [JsonIgnore] protected readonly MessageQueue Queue;

    [JsonIgnore] protected readonly UserStore Store;

    public Room(Guid guid, HttpClient gaida)
    {
        RoomID = guid;
        Gaida = gaida;
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
    [JsonIgnore]
    public Action? OnEmptied { get; init; }

    /// <summary>
    ///     Everything the admin panel shows for this room. Deliberately reads <see cref="User.Username" />
    ///     rather than <see cref="User.ChatUsername" />: the latter assigns an anonymous name as a side
    ///     effect, and a monitoring read must not change what the room calls people.
    /// </summary>
    public async Task<object> Snapshot()
    {
        return new
        {
            roomId = RoomID,
            name = RoomName,
            description = RoomDescription,
            users = Store.Users.Values.Select(user => new
            {
                id = user.ID,
                username = user.Username,
                socket = user.WebSocket.State.ToString()
            }).ToList(),
            player = await Player.Snapshot()
        };
    }

    /// <summary>
    ///     Removes a member and closes their socket. The close is the point: <see cref="RemoveUser" />
    ///     alone drops them from the store, and <see cref="GetOrAddUser" /> puts them straight back on
    ///     their next frame — a kick that only lasts until the kicked person presses play is not a kick.
    /// </summary>
    public async Task<bool> Kick(string id)
    {
        var user = Store.GetUser(id);
        if (user is null) return false;

        await RemoveUser(id);

        try
        {
            // CloseOutputAsync, never CloseAsync: CloseAsync sends the close frame and then waits for
            // the peer's acknowledgement, and that acknowledgement is read by this member's own
            // receive loop, which is already sitting in ReadWholeMessageAsync. The wait therefore
            // never completes and the operator's request hangs until it times out — the kick itself
            // works, so the only symptom is a 502 on an action that succeeded. Sending the frame is
            // all that is wanted here; the receive loop sees the peer's reply and tears the
            // connection down through its own finally.
            if (user.WebSocket.State == WebSocketState.Open)
                await user.WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,
                    "Removed by an administrator", CancellationToken.None);
        }
        catch (WebSocketException)
        {
            // Already gone. They are out of the store either way, which is what was asked for.
        }
        catch (ObjectDisposedException)
        {
            // The receive loop's `using` got there first. Same outcome.
        }

        return true;
    }

    /// <summary>Everyone currently in the room, for a close that has to remove them all.</summary>
    public IReadOnlyCollection<string> UserIds => [.. Store.Users.Keys];

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
        // a socket can close without ever having joined, so let's just return in that case
        if (user is null) return;

        await Store.RemoveUser(id);
        await Queue.Send($"chat System %% User '{user.ChatUsername}' left from the session.");
        await Player.UserLeft(id);

        if (Store.Count == 0)
            OnEmptied?.Invoke();
    }

    public Task HandleUserMessage(User user, string message)
    {
        return HandleUserMessage(user, message.AsMemory());
    }

    /// <summary>
    ///     Takes the frame as memory over the reader's buffer: splitting a command used to cut two
    ///     strings out of every inbound message, and this path runs once per keystroke-rate action
    ///     from every member of every room.
    /// </summary>
    public Task HandleUserMessage(User user, ReadOnlyMemory<char> message)
    {
        var splitIndex = message.Span.IndexOf(' ');

        return splitIndex != -1
            ? HandleParameterMessages(message[..splitIndex], message[splitIndex..], user)
            : HandleParameterlessMessages(message, user);
    }

    protected Task HandleParameterMessages(ReadOnlyMemory<char> name, ReadOnlyMemory<char> value, User user)
    {
        return name.Span switch
        {
            "add" => Enqueue(value.ToString()),
            "setnext" when int.TryParse(value.Span, out var nextIndex) => Player.SetNext(nextIndex),
            "skipto" when int.TryParse(value.Span, out var skipIndex) => Player.SkipTo(skipIndex),
            "seek" when double.TryParse(value.Span, out var seekSeconds) => Player.SeekTo(seekSeconds),
            "remove" when int.TryParse(value.Span, out var removeIndex) => Player.Remove(removeIndex),
            "chat" => Queue.Send($"chat {user.ChatUsername} %% {value}"),
            "updateroom" => HandleUpdateRoom(value, user),
            _ => Task.CompletedTask
        };
    }

    protected async Task Enqueue(string id)
    {
        SearchResultDto[]? results;
        try
        {
            results = await Gaida.GetFromJsonAsync<SearchResultDto[]>(
                $"/Audio/Search?query={Uri.EscapeDataString(id)}");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            return;
        }

        var result = results?.FirstOrDefault();
        if (result is null) return;

        await Player.Enqueue(result.ToTrack());
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
        return name.Span switch
        {
            "end" => Player.SetFinished(user.ID),
            "next" => Player.Next(),
            "previous" => Player.Previous(),
            "playpause" => Player.TogglePlaying(),
            "stop" => Player.Stop(),
            "shuffle" => Player.Shuffle(),
            "loaded" => Player.SetLoaded(user.ID),
            "sync" => SyncTo(user),
            _ => Task.CompletedTask
        };
    }

    protected Task SyncTo(User user)
    {
        return Player.SyncTo(user);
    }
}