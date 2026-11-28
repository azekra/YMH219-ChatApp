using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace YMH219_ChatApp.Networking;

public sealed class ChatServer : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, ClientSession> _sessions = new();
    private readonly List<Task> _clientTasks = [];
    private readonly object _clientTasksLock = new();
    private readonly object _registrationLock = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Task? _acceptTask;

    public event EventHandler<ChatMessageEventArgs>? MessageReceived;
    public event EventHandler<ClientConnectionEventArgs>? ClientConnected;
    public event EventHandler<ClientConnectionEventArgs>? ClientDisconnected;
    public event EventHandler<ChatServerErrorEventArgs>? ErrorOccurred;

    public IReadOnlyCollection<ChatClientInfo> ConnectedClients
    {
        get
        {
            lock (_registrationLock)
            {
                return _sessions.Values
                    .Where(session => session.UserName is not null)
                    .Select(session => new ChatClientInfo(session.Id, session.UserName!, session.ConnectedAt))
                    .OrderBy(client => client.UserName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public async Task StartAsync(IPAddress? address = null, int port = 5000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, IPEndPoint.MinPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_listener is not null)
            {
                throw new InvalidOperationException("The chat server is already running.");
            }

            var listener = new TcpListener(address ?? IPAddress.Any, port);
            listener.Start();
            _listener = listener;
            _cancellation = new CancellationTokenSource();
            _acceptTask = AcceptClientsAsync(listener, _cancellation.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_listener is null)
            {
                return;
            }

            var listener = _listener;
            var cancellation = _cancellation!;
            var acceptTask = _acceptTask!;
            cancellation.Cancel();
            listener.Stop();

            foreach (var session in _sessions.Values)
            {
                session.Close();
            }

            await acceptTask.ConfigureAwait(false);

            Task[] clientTasks;
            lock (_clientTasksLock)
            {
                clientTasks = _clientTasks.ToArray();
            }

            await Task.WhenAll(clientTasks).ConfigureAwait(false);
            _listener = null;
            _cancellation = null;
            _acceptTask = null;
            cancellation.Dispose();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task AcceptClientsAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                var session = new ClientSession(tcpClient);
                _sessions[session.Id] = session;

                var task = HandleClientAsync(session, cancellationToken);
                lock (_clientTasksLock)
                {
                    _clientTasks.Add(task);
                }

                _ = task.ContinueWith(
                    completed =>
                    {
                        _ = completed.Exception;
                        lock (_clientTasksLock)
                        {
                            _clientTasks.Remove(completed);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                RaiseError(exception);
            }
        }
    }

    private async Task HandleClientAsync(ClientSession session, CancellationToken cancellationToken)
    {
        var registered = false;
        var announced = false;
        try
        {
            var joinLine = await session.Reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            var join = joinLine is null ? null : ChatProtocol.Deserialize(joinLine);
            var userName = join?.Type == ChatProtocol.Join ? join.UserName?.Trim() : null;
            if (string.IsNullOrWhiteSpace(userName) || userName.Length > 32)
            {
                await session.SendAsync(new ChatWireMessage(ChatProtocol.Error, Text: "A username of 1 to 32 characters is required."), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!TryRegisterSession(session, userName))
            {
                await session.SendAsync(new ChatWireMessage(ChatProtocol.Error, Text: "That username is already connected."), cancellationToken).ConfigureAwait(false);
                return;
            }

            registered = true;
            await session.SendAsync(new ChatWireMessage(ChatProtocol.Joined, UserName: userName, Timestamp: session.ConnectedAt), cancellationToken).ConfigureAwait(false);
            RaiseClientConnected(session.ToClientInfo());
            announced = true;

            var joined = new ChatMessageEventArgs(userName, $"{userName} joined the chat.", DateTimeOffset.UtcNow, true);
            await BroadcastAsync(new ChatWireMessage(ChatProtocol.System, joined.UserName, joined.Text, joined.Timestamp), cancellationToken).ConfigureAwait(false);
            RaiseMessage(joined);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await session.Reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                var message = ChatProtocol.Deserialize(line);
                if (message?.Type != ChatProtocol.Message || string.IsNullOrWhiteSpace(message.Text))
                {
                    continue;
                }

                var text = message.Text.Trim();
                if (text.Length > 4000)
                {
                    await session.SendAsync(new ChatWireMessage(ChatProtocol.Error, Text: "Messages cannot exceed 4000 characters."), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var chatMessage = new ChatMessageEventArgs(userName, text, DateTimeOffset.UtcNow, false);
                await BroadcastAsync(new ChatWireMessage(ChatProtocol.Chat, userName, text, chatMessage.Timestamp), cancellationToken).ConfigureAwait(false);
                RaiseMessage(chatMessage);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            RaiseError(exception);
        }
        finally
        {
            lock (_registrationLock)
            {
                _sessions.TryRemove(session.Id, out _);
            }

            session.Close();
            if (registered && announced)
            {
                RaiseClientDisconnected(session.ToClientInfo());
                var left = new ChatMessageEventArgs(session.UserName!, $"{session.UserName} left the chat.", DateTimeOffset.UtcNow, true);
                await BroadcastAsync(new ChatWireMessage(ChatProtocol.System, left.UserName, left.Text, left.Timestamp), CancellationToken.None).ConfigureAwait(false);
                RaiseMessage(left);
            }
        }
    }

    private bool TryRegisterSession(ClientSession session, string userName)
    {
        lock (_registrationLock)
        {
            if (_sessions.Values.Any(existing =>
                    !ReferenceEquals(existing, session) &&
                    string.Equals(existing.UserName, userName, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            session.UserName = userName;
            session.ConnectedAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    private async Task BroadcastAsync(ChatWireMessage message, CancellationToken cancellationToken)
    {
        var sends = _sessions.Values
            .Where(session => session.UserName is not null)
            .Select(session => session.SendSafelyAsync(message, cancellationToken));
        await Task.WhenAll(sends).ConfigureAwait(false);
    }

    private void RaiseMessage(ChatMessageEventArgs message)
    {
        try { MessageReceived?.Invoke(this, message); } catch { }
    }

    private void RaiseClientConnected(ChatClientInfo client)
    {
        try { ClientConnected?.Invoke(this, new ClientConnectionEventArgs(client)); } catch { }
    }

    private void RaiseClientDisconnected(ChatClientInfo client)
    {
        try { ClientDisconnected?.Invoke(this, new ClientConnectionEventArgs(client)); } catch { }
    }

    private void RaiseError(Exception exception)
    {
        try { ErrorOccurred?.Invoke(this, new ChatServerErrorEventArgs(exception)); } catch { }
    }

    private sealed class ClientSession(TcpClient tcpClient)
    {
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private readonly NetworkStream _stream = tcpClient.GetStream();
        private readonly StreamReader _reader = new(tcpClient.GetStream(), new UTF8Encoding(false), leaveOpen: true);
        private readonly StreamWriter _writer = new(tcpClient.GetStream(), new UTF8Encoding(false), leaveOpen: true);

        public Guid Id { get; } = Guid.NewGuid();
        public string? UserName { get; set; }
        public DateTimeOffset ConnectedAt { get; set; }
        public StreamReader Reader => _reader;

        public async Task SendAsync(ChatWireMessage message, CancellationToken cancellationToken)
        {
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _writer.WriteLineAsync(ChatProtocol.Serialize(message).AsMemory(), cancellationToken).ConfigureAwait(false);
                await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }
        }

        public async Task SendSafelyAsync(ChatWireMessage message, CancellationToken cancellationToken)
        {
            try { await SendAsync(message, cancellationToken).ConfigureAwait(false); }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }

        public ChatClientInfo ToClientInfo() => new(Id, UserName!, ConnectedAt);

        public void Close()
        {
            tcpClient.Dispose();
            _reader.Dispose();
            _writer.Dispose();
            _stream.Dispose();
        }
    }
}

public sealed record ChatClientInfo(Guid Id, string UserName, DateTimeOffset ConnectedAt);

public sealed class ChatMessageEventArgs(string userName, string text, DateTimeOffset timestamp, bool isSystem) : EventArgs
{
    public string UserName { get; } = userName;
    public string Text { get; } = text;
    public DateTimeOffset Timestamp { get; } = timestamp;
    public bool IsSystem { get; } = isSystem;
}

public sealed class ClientConnectionEventArgs(ChatClientInfo client) : EventArgs
{
    public ChatClientInfo Client { get; } = client;
}

public sealed class ChatServerErrorEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}