# YMH219-ChatApp

A Windows Forms TCP client-server chat application for the YMH219 assignment. An app instance can host the server and connect as a client. Other instances on the same computer or local network can connect with a username and exchange timestamped messages.

## Features

- Start and stop a TCP server from the app.
- Connect to a server using its IP address, port, and username.
- Send and receive messages with server-generated timestamps.
- View connected usernames and connection times on the server.
- Display connection and network errors without closing the application.
- Support multiple connected clients.

## Technologies

- C# and .NET 10 for Windows (`net10.0-windows`).
- Windows Forms for the user interface.
- `TcpListener` and `TcpClient` for TCP communication.
- `NetworkStream`, `StreamReader`, and `StreamWriter` for newline-delimited UTF-8 JSON messages.
- `async`/`await` and cancellation for network operations and shutdown.

No additional NuGet packages are required.

## Requirements

- Windows with the .NET 10 SDK to build, or the .NET 10 Desktop Runtime to run a published build.
- For LAN use, both computers must be connected to the same reachable network.

## Run locally

1. Open `YMH219-ChatApp.slnx` in Visual Studio 2026, or open a PowerShell terminal in the project directory.
2. Build and run the `YMH219-ChatApp` project.
3. In the **Server** card, keep bind address `0.0.0.0` and port `5000`, then select **Start server**.
4. In the **Connect** card, keep server address `127.0.0.1`, port `5000`, enter a username, and select **Connect**.
5. Open a second app instance and connect it to `127.0.0.1:5000` with a different username to test messaging between two clients.

To run from PowerShell:

```powershell
dotnet run --project .\YMH219-ChatApp.csproj
```

## Run over a local network

1. Start the server on the computer that will host the chat. The bind address should be `0.0.0.0` so the listener accepts connections on its network interfaces.
2. If Windows Firewall prompts, allow the application on **Private networks**. If needed, create an inbound firewall rule for the chosen TCP port.
3. Find the host computer's private IPv4 address (for example, with `ipconfig`).
4. On each other computer, run the app and enter that private IPv4 address and the server's port in the **Connect** card. Choose a unique username and connect.
5. Keep the host app running and its server started while clients are chatting.

The default port is `5000`. If it is already in use, choose another available port and use the same value on the clients.

## Screenshots for submission

Capture the actual running application in Windows and add both images to `docs/screenshots/`:

- `docs/screenshots/server-running.png` — server started with at least one connected client visible.
- `docs/screenshots/chat-messages.png` — two different usernames with timestamped messages visible.

After adding the images, include them in this section using Markdown image links. The screenshots must come from the running app; they are not generated mockups.

## Project layout

- `Form1.cs` — dashboard layout and UI event handling.
- `Networking/ChatServer.cs` — asynchronous TCP server and connected-client management.
- `Networking/ChatClient.cs` — asynchronous TCP client.
- `Networking/ChatProtocol.cs` — shared JSON wire message definitions.

## Notes

This project is intended for a local-network course assignment. It does not provide encryption, account authentication, or protection against untrusted network traffic. Do not expose it directly to the public internet.