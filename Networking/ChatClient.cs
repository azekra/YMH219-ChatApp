using System.Net;
using System.Net.Sockets;
using System.Text;

namespace YMH219_ChatApp.Networking;

public sealed class ChatClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private ConnectionSession? _session;

    public event EventHandler<ChatMessageEventArgs>? MessageReceived;
    public event EventHandler<ChatClientConnectionEventArgs>? ConnectionStateChanged;
    public event EventHandler<ChatClientErrorEventArgs>? ErrorOccurred;

    public bool IsConnected => _session is { IsConnected: true };

    public async Task ConnectAsync(string host, int port, string userName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, IPEndPoint.MinPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);

        userName = userName.Trim();
        if (userName.Length is < 1 or > 32)
        {
            throw new ArgumentException("A username must be between 1 and 32 characters.", nameof(userName));
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
            {
                throw new InvalidOperationException("The chat client is already connected.");
            }

            if (_session is not null)
            {
                var previousSession = _session;
                _session = null;
                await CloseSessionAsync(previousSession).ConfigureAwait(false);
            }

            var tcpClient = new TcpClient { NoDelay = true };
            ConnectionSession? session = null;
            try
            {
                await tcpClient.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
                session = new ConnectionSession(tcpClient);
                _session = session;
                await session.SendAsync(new ChatWireMessage(ChatProtocol.Join, UserName: userName), cancellationToken).ConfigureAwait(false);
                session.MarkConnected();
                RaiseConnectionStateChanged(true);
                session.ReadTask = ReadMessagesAsync(session);
            }
            catch
            {
                _session = null;
                if (session is null)
                {
                    tcpClient.Dispose();
                }
                else
                {
                    session.Stop();
                    await session.DisposeAsync().ConfigureAwait(false);
                }

                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task SendMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        text = text.Trim();
        if (text.Length > 4000)
        {
            throw new ArgumentException("Messages cannot exceed 4000 characters.", nameof(text));
        }

        var session = _session;
        if (session is null || !session.IsConnected)
        {
            throw new InvalidOperationException("The chat client is not connected.");
        }

        await session.SendAsync(new ChatWireMessage(ChatProtocol.Message, Text: text), cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var session = _session;
            _session = null;
            if (session is null)
            {
                return;
            }

            if (session.MarkDisconnected())
            {
                RaiseConnectionStateChanged(false);
            }

            session.Stop();
            await CloseSessionAsync(session).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    private async Task ReadMessagesAsync(ConnectionSession session)
    {
        try
        {
            while (!session.CancellationToken.IsCancellationRequested)
            {
                var line = await session.Reader.ReadLineAsync(session.CancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                var message = ChatProtocol.Deserialize(line);
                if (message is null)
                {
                    continue;
                }

                if (message.Type is ChatProtocol.Chat or ChatProtocol.System)
                {
                    RaiseMessage(new ChatMessageEventArgs(
                        message.UserName ?? string.Empty,
                        message.Text ?? string.Empty,
                        message.Timestamp ?? DateTimeOffset.UtcNow,
                        message.Type == ChatProtocol.System));
                }
                else if (message.Type == ChatProtocol.Error)
                {
                    RaiseError(new InvalidOperationException(message.Text ?? "The server reported an error."));
                }
            }
        }
        catch (OperationCanceledException) when (session.CancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException exception)
        {
            RaiseError(exception);
        }
        catch (SocketException exception)
        {
            RaiseError(exception);
        }
        catch (ObjectDisposedException) when (session.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RaiseError(exception);
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _session, null, session), session) && session.MarkDisconnected())
            {
                RaiseConnectionStateChanged(false);
            }

            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task CloseSessionAsync(ConnectionSession session)
    {
        session.Stop();
        if (session.ReadTask is not null)
        {
            await session.ReadTask.ConfigureAwait(false);
        }
        else
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void RaiseMessage(ChatMessageEventArgs message)
    {
        try { MessageReceived?.Invoke(this, message); } catch { }
    }

    private void RaiseConnectionStateChanged(bool isConnected)
    {
        try { ConnectionStateChanged?.Invoke(this, new ChatClientConnectionEventArgs(isConnected)); } catch { }
    }

    private void RaiseError(Exception exception)
    {
        try { ErrorOccurred?.Invoke(this, new ChatClientErrorEventArgs(exception)); } catch { }
    }

    private sealed class ConnectionSession(TcpClient tcpClient) : IAsyncDisposable
    {
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private readonly NetworkStream _stream = tcpClient.GetStream();
        private readonly StreamWriter _writer = new(tcpClient.GetStream(), new UTF8Encoding(false), leaveOpen: true);
        private readonly CancellationTokenSource _cancellation = new();
        private int _connected;
        private int _stopped;
        private int _disposed;

        public StreamReader Reader { get; } = new(tcpClient.GetStream(), new UTF8Encoding(false), leaveOpen: true);
        public CancellationToken CancellationToken => _cancellation.Token;
        public Task? ReadTask { get; set; }
        public bool IsConnected => Volatile.Read(ref _connected) == 1;

        public void MarkConnected() => Interlocked.Exchange(ref _connected, 1);

        public bool MarkDisconnected() => Interlocked.Exchange(ref _connected, 0) == 1;

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

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 0)
            {
                _cancellation.Cancel();
                tcpClient.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Stop();
            await _sendGate.WaitAsync().ConfigureAwait(false);
            try
            {
                Reader.Dispose();
                _writer.Dispose();
                _stream.Dispose();
                _cancellation.Dispose();
            }
            finally
            {
                _sendGate.Release();
            }
        }
    }
}

public sealed class ChatClientConnectionEventArgs(bool isConnected) : EventArgs
{
    public bool IsConnected { get; } = isConnected;
}

public sealed class ChatClientErrorEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}