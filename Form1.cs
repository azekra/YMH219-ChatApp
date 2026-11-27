using System.Drawing;
using System.Net;
using System.Windows.Forms;
using YMH219_ChatApp.Networking;

namespace YMH219_ChatApp
{
    public partial class Form1 : Form
    {
        private static readonly Color PageColor = Color.FromArgb(244, 247, 251);
        private static readonly Color InkColor = Color.FromArgb(31, 41, 55);
        private static readonly Color MutedColor = Color.FromArgb(107, 114, 128);
        private static readonly Color AccentColor = Color.FromArgb(37, 99, 235);
        private static readonly Color BorderColor = Color.FromArgb(226, 232, 240);

        private readonly ChatServer _server = new();
        private readonly ChatClient _client = new();
        private TextBox _serverAddressTextBox = null!;
        private TextBox _serverPortTextBox = null!;
        private Button _startServerButton = null!;
        private Button _stopServerButton = null!;
        private Label _serverStatusLabel = null!;
        private Label _clientCountLabel = null!;
        private ListBox _clientsListBox = null!;
        private TextBox _hostTextBox = null!;
        private TextBox _clientPortTextBox = null!;
        private TextBox _userNameTextBox = null!;
        private Button _connectButton = null!;
        private Button _disconnectButton = null!;
        private Label _clientStatusLabel = null!;
        private RichTextBox _transcript = null!;
        private TextBox _messageTextBox = null!;
        private Button _sendButton = null!;
        private Label _footerStatusLabel = null!;
        private bool _shutdownStarted;
        private bool _allowClose;

        public Form1()
        {
            InitializeComponent();
            BuildDashboard();
            WireEvents();
            FormClosing += HandleFormClosing;
        }

        private void BuildDashboard()
        {
            Text = "YMH219 Chat | TCP Client-Server";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1120, 700);
            ClientSize = new Size(1380, 850);
            BackColor = PageColor;
            Font = new Font("Segoe UI", 9F);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = PageColor,
                Padding = new Padding(18, 14, 18, 12),
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildWorkspace(), 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);
            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = PageColor };
            panel.Controls.Add(new Label
            {
                Text = "YMH219 Chat",
                AutoSize = true,
                Font = new Font("Segoe UI", 21F, FontStyle.Bold),
                ForeColor = InkColor,
                Location = new Point(4, 2)
            });
            panel.Controls.Add(new Label
            {
                Text = "TCP client-server messaging over your local network",
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = MutedColor,
                Location = new Point(7, 42)
            });
            return panel;
        }

        private Control BuildWorkspace()
        {
            var workspace = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = PageColor,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty
            };
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            workspace.Controls.Add(BuildServerCard(), 0, 0);
            workspace.Controls.Add(BuildChatCard(), 1, 0);
            workspace.Controls.Add(BuildConnectionCard(), 2, 0);
            return workspace;
        }

        private Control BuildServerCard()
        {
            var layout = CreateCardLayout();
            AddCardRow(layout, CreateCardTitle("Server", "Start a server for local or LAN clients."), 72);
            _serverAddressTextBox = CreateTextBox("0.0.0.0");
            AddCardRow(layout, CreateField("Bind address", _serverAddressTextBox), 62);
            _serverPortTextBox = CreateTextBox("5000");
            AddCardRow(layout, CreateField("Port", _serverPortTextBox), 62);

            _startServerButton = CreateButton("Start server", AccentColor, Color.White);
            _stopServerButton = CreateButton("Stop", Color.FromArgb(229, 231, 235), InkColor);
            _stopServerButton.Enabled = false;
            AddCardRow(layout, CreateButtonRow(_startServerButton, _stopServerButton), 44);
            _serverStatusLabel = CreateStatusLabel("Server stopped");
            AddCardRow(layout, _serverStatusLabel, 34);

            var clientsHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            clientsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            clientsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            clientsHeader.Controls.Add(CreateSectionLabel("Connected clients"), 0, 0);
            _clientCountLabel = new Label { Text = "0", AutoSize = true, ForeColor = MutedColor, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
            clientsHeader.Controls.Add(_clientCountLabel, 1, 0);
            AddCardRow(layout, clientsHeader, 32);

            _clientsListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(249, 250, 251),
                ForeColor = InkColor
            };
            AddCardRow(layout, _clientsListBox, 100, SizeType.Percent);
            AddCardRow(layout, CreateHint("Clients appear here after connecting with a username."), 48);
            return CreateCard(layout);
        }

        private Control BuildChatCard()
        {
            var layout = CreateCardLayout();
            AddCardRow(layout, CreateCardTitle("Chat", "Messages include server-generated timestamps."), 72);
            _transcript = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                DetectUrls = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(249, 250, 252),
                ForeColor = InkColor,
                Font = new Font("Segoe UI", 9.5F),
                HideSelection = false,
                WordWrap = true
            };
            AddCardRow(layout, _transcript, 100, SizeType.Percent);

            var composer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 10, 0, 0) };
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            _messageTextBox = CreateTextBox(string.Empty);
            _messageTextBox.PlaceholderText = "Write a message...";
            _messageTextBox.Enabled = false;
            _sendButton = CreateButton("Send", AccentColor, Color.White);
            _sendButton.Enabled = false;
            composer.Controls.Add(_messageTextBox, 0, 0);
            composer.Controls.Add(_sendButton, 1, 0);
            AddCardRow(layout, composer, 50);
            return CreateCard(layout);
        }

        private Control BuildConnectionCard()
        {
            var layout = CreateCardLayout();
            AddCardRow(layout, CreateCardTitle("Connect", "Join the server using its address and port."), 72);
            _hostTextBox = CreateTextBox("127.0.0.1");
            AddCardRow(layout, CreateField("Server address", _hostTextBox), 62);
            _clientPortTextBox = CreateTextBox("5000");
            AddCardRow(layout, CreateField("Server port", _clientPortTextBox), 62);
            _userNameTextBox = CreateTextBox(string.Empty);
            _userNameTextBox.PlaceholderText = "Choose a display name";
            AddCardRow(layout, CreateField("Username", _userNameTextBox), 62);

            _connectButton = CreateButton("Connect", AccentColor, Color.White);
            _disconnectButton = CreateButton("Disconnect", Color.FromArgb(229, 231, 235), InkColor);
            _disconnectButton.Enabled = false;
            AddCardRow(layout, CreateButtonRow(_connectButton, _disconnectButton), 44);
            _clientStatusLabel = CreateStatusLabel("Not connected");
            AddCardRow(layout, _clientStatusLabel, 34);
            AddCardRow(layout, CreateHint("For another computer, enter the server computer's private LAN IPv4 address. Allow the app through Windows Firewall on private networks."), 112);
            AddCardRow(layout, CreateHint("Quick test: start the server here and connect to 127.0.0.1. Open a second app instance to test another username."), 112);
            return CreateCard(layout);
        }

        private Control BuildFooter()
        {
            _footerStatusLabel = new Label
            {
                Text = "Ready",
                Dock = DockStyle.Fill,
                ForeColor = MutedColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };
            return _footerStatusLabel;
        }

        private static Panel CreateCard(Control content)
        {
            var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(16), Margin = new Padding(7), BorderStyle = BorderStyle.FixedSingle };
            content.Dock = DockStyle.Fill;
            card.Controls.Add(content);
            return card;
        }

        private static TableLayoutPanel CreateCardLayout() => new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ColumnCount = 1,
            RowCount = 0,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        private static void AddCardRow(TableLayoutPanel layout, Control control, float height, SizeType sizeType = SizeType.Absolute)
        {
            var row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(sizeType, height));
            control.Margin = new Padding(0, 0, 0, 5);
            layout.Controls.Add(control, 0, row);
        }

        private static Control CreateCardTitle(string title, string description)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            panel.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 14F, FontStyle.Bold), ForeColor = InkColor, Location = new Point(0, 0) });
            panel.Controls.Add(new Label { Text = description, AutoSize = false, Height = 36, Dock = DockStyle.Bottom, Font = new Font("Segoe UI", 8.5F), ForeColor = MutedColor });
            return panel;
        }

        private static Control CreateField(string title, Control input)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(CreateSectionLabel(title), 0, 0);
            layout.Controls.Add(input, 0, 1);
            return layout;
        }

        private static Label CreateSectionLabel(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = InkColor,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        private static Label CreateHint(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = MutedColor,
            Font = new Font("Segoe UI", 8.25F),
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(0, 4, 0, 0)
        };

        private static TextBox CreateTextBox(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5F),
            Margin = Padding.Empty
        };

        private static Button CreateButton(string text, Color backColor, Color foreColor) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            BackColor = backColor,
            ForeColor = foreColor,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Margin = new Padding(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            FlatAppearance = { BorderColor = BorderColor, BorderSize = 1 }
        };

        private static Control CreateButtonRow(params Button[] buttons)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = buttons.Length, RowCount = 1, Margin = Padding.Empty };
            for (var index = 0; index < buttons.Length; index++)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / buttons.Length));
                row.Controls.Add(buttons[index], index, 0);
            }

            return row;
        }

        private static Label CreateStatusLabel(string text) => new()
        {
            Text = $"●  {text}",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        private void WireEvents()
        {
            _startServerButton.Click += async (_, _) => await StartServerAsync();
            _stopServerButton.Click += async (_, _) => await StopServerAsync();
            _connectButton.Click += async (_, _) => await ConnectClientAsync();
            _disconnectButton.Click += async (_, _) => await DisconnectClientAsync();
            _sendButton.Click += async (_, _) => await SendMessageAsync();
            _messageTextBox.KeyDown += async (_, eventArgs) =>
            {
                if (eventArgs.KeyCode == Keys.Enter)
                {
                    eventArgs.SuppressKeyPress = true;
                    await SendMessageAsync();
                }
            };

            _server.ClientConnected += (_, _) => RunOnUiThread(RefreshConnectedClients);
            _server.ClientDisconnected += (_, _) => RunOnUiThread(RefreshConnectedClients);
            _server.ErrorOccurred += (_, eventArgs) => RunOnUiThread(() => SetFooterStatus($"Server: {eventArgs.Exception.Message}", true));
            _client.MessageReceived += (_, eventArgs) => RunOnUiThread(() => AppendMessage(eventArgs));
            _client.ConnectionStateChanged += (_, eventArgs) => RunOnUiThread(() => SetClientConnectedState(eventArgs.IsConnected));
            _client.ErrorOccurred += (_, eventArgs) => RunOnUiThread(() => SetFooterStatus($"Connection: {eventArgs.Exception.Message}", true));
        }

        private async Task StartServerAsync()
        {
            if (!TryReadPort(_serverPortTextBox.Text, out var port) || !IPAddress.TryParse(_serverAddressTextBox.Text.Trim(), out var address))
            {
                SetFooterStatus("Enter a valid server bind IP address and port (1–65535).", true);
                return;
            }

            _startServerButton.Enabled = false;
            _serverStatusLabel.Text = "●  Starting server...";
            _serverStatusLabel.ForeColor = AccentColor;
            SetFooterStatus("Starting TCP server...");
            try
            {
                await _server.StartAsync(address, port);
                _stopServerButton.Enabled = true;
                _serverStatusLabel.Text = $"●  Listening on {address}:{port}";
                _serverStatusLabel.ForeColor = Color.FromArgb(22, 163, 74);
                SetFooterStatus($"Server is listening on {address}:{port}. Connect this or another app instance.");
            }
            catch (Exception exception)
            {
                _startServerButton.Enabled = true;
                _serverStatusLabel.Text = "●  Server stopped";
                _serverStatusLabel.ForeColor = Color.FromArgb(148, 163, 184);
                SetFooterStatus($"Could not start server: {exception.Message}", true);
                MessageBox.Show(this, exception.Message, "Server could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task StopServerAsync()
        {
            _stopServerButton.Enabled = false;
            SetFooterStatus("Stopping server...");
            try
            {
                await _server.StopAsync();
                _serverStatusLabel.Text = "●  Server stopped";
                _serverStatusLabel.ForeColor = Color.FromArgb(148, 163, 184);
                _startServerButton.Enabled = true;
                RefreshConnectedClients();
                SetFooterStatus("Server stopped.");
            }
            catch (Exception exception)
            {
                _stopServerButton.Enabled = true;
                SetFooterStatus($"Could not stop server: {exception.Message}", true);
                MessageBox.Show(this, exception.Message, "Server error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task ConnectClientAsync()
        {
            if (!TryReadPort(_clientPortTextBox.Text, out var port))
            {
                SetFooterStatus("Enter a valid server port (1–65535).", true);
                return;
            }

            var host = _hostTextBox.Text.Trim();
            var userName = _userNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(userName))
            {
                SetFooterStatus("Enter the server address and a username before connecting.", true);
                return;
            }

            _connectButton.Enabled = false;
            _clientStatusLabel.Text = "●  Connecting...";
            _clientStatusLabel.ForeColor = AccentColor;
            SetFooterStatus($"Connecting to {host}:{port}...");
            try
            {
                await _client.ConnectAsync(host, port, userName);
                SetFooterStatus($"Connected to {host}:{port} as {userName}.");
            }
            catch (Exception exception)
            {
                _connectButton.Enabled = true;
                _clientStatusLabel.Text = "●  Not connected";
                _clientStatusLabel.ForeColor = Color.FromArgb(148, 163, 184);
                SetFooterStatus($"Connection failed: {exception.Message}", true);
                MessageBox.Show(this, exception.Message, "Connection failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task DisconnectClientAsync()
        {
            _disconnectButton.Enabled = false;
            SetFooterStatus("Disconnecting...");
            try
            {
                await _client.DisconnectAsync();
                SetClientConnectedState(false);
                SetFooterStatus("Disconnected from server.");
            }
            catch (Exception exception)
            {
                SetFooterStatus($"Could not disconnect cleanly: {exception.Message}", true);
            }
        }

        private async Task SendMessageAsync()
        {
            var text = _messageTextBox.Text.Trim();
            if (text.Length == 0)
            {
                return;
            }

            try
            {
                await _client.SendMessageAsync(text);
                _messageTextBox.Clear();
                _messageTextBox.Focus();
                SetFooterStatus("Message sent.");
            }
            catch (Exception exception)
            {
                SetFooterStatus($"Could not send message: {exception.Message}", true);
            }
        }

        private void SetClientConnectedState(bool connected)
        {
            _connectButton.Enabled = !connected;
            _disconnectButton.Enabled = connected;
            _sendButton.Enabled = connected;
            _messageTextBox.Enabled = connected;
            _hostTextBox.Enabled = !connected;
            _clientPortTextBox.Enabled = !connected;
            _userNameTextBox.Enabled = !connected;
            _clientStatusLabel.Text = connected ? "●  Connected" : "●  Not connected";
            _clientStatusLabel.ForeColor = connected ? Color.FromArgb(22, 163, 74) : Color.FromArgb(148, 163, 184);
            if (!connected)
            {
                _messageTextBox.Clear();
            }
        }

        private void RefreshConnectedClients()
        {
            var clients = _server.ConnectedClients;
            _clientsListBox.BeginUpdate();
            _clientsListBox.Items.Clear();
            foreach (var client in clients)
            {
                _clientsListBox.Items.Add($"{client.UserName}  ·  since {client.ConnectedAt.ToLocalTime():HH:mm:ss}");
            }

            _clientsListBox.EndUpdate();
            _clientCountLabel.Text = clients.Count.ToString();
        }

        private void AppendMessage(ChatMessageEventArgs message)
        {
            var timestamp = message.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            var text = message.IsSystem
                ? $"[{timestamp}]  {message.Text}"
                : $"[{timestamp}]  {message.UserName}: {message.Text}";
            var color = message.IsSystem ? MutedColor : InkColor;
            _transcript.SelectionStart = _transcript.TextLength;
            _transcript.SelectionLength = 0;
            _transcript.SelectionColor = color;
            _transcript.AppendText(text + Environment.NewLine);
            _transcript.SelectionColor = _transcript.ForeColor;
            _transcript.ScrollToCaret();
        }

        private void SetFooterStatus(string text, bool isError = false)
        {
            _footerStatusLabel.Text = text;
            _footerStatusLabel.ForeColor = isError ? Color.FromArgb(185, 28, 28) : MutedColor;
        }

        private void RunOnUiThread(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }

            if (InvokeRequired)
            {
                try { BeginInvoke(action); } catch (InvalidOperationException) { }
                return;
            }

            action();
        }

        private static bool TryReadPort(string text, out int port) =>
            int.TryParse(text.Trim(), out port) && port is >= 1 and <= 65535;

        private async void HandleFormClosing(object? sender, FormClosingEventArgs eventArgs)
        {
            if (_allowClose)
            {
                return;
            }

            eventArgs.Cancel = true;
            if (_shutdownStarted)
            {
                return;
            }

            _shutdownStarted = true;
            Enabled = false;
            Exception? shutdownError = null;
            try
            {
                await _client.DisposeAsync();
            }
            catch (Exception exception)
            {
                shutdownError = exception;
            }

            try
            {
                await _server.DisposeAsync();
            }
            catch (Exception exception)
            {
                shutdownError ??= exception;
            }

            if (shutdownError is not null)
            {
                MessageBox.Show(this, shutdownError.Message, "Shutdown warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            _allowClose = true;
            Close();
        }
    }
}
