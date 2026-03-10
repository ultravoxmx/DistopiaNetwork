# Distopia Network — Distributed Podcast Protocol

A fully decentralized podcast network implemented in C# .NET 9.

## Architecture

```
Browser 1/2          Browser 3/4/N
    ↕                     ↕
 Server A   ←──sync──→  Server B
    ↕    ╲             ╱    ↕
    ↕      ╲         ╱      ↕
 Client1  Client1-BCK  Client2  Client3
 [MP3s]                [MP3]    [MP3s]
```

## Projects

| Project | Type | Description |
|---------|------|-------------|
| `DistopiaNetwork.Shared` | Class Library | Models, Crypto, DTOs shared by all |
| `DistopiaNetwork.Server` | ASP.NET Core Web API | Peer server node |
| `DistopiaNetwork.PublisherClient` | Console App | Podcast creator tool |
| `DistopiaNetwork.BrowserClient` | Console App | Listener/streaming client |

## Key Features

- ✅ **RSA-2048 digital signatures** on all metadata
- ✅ **SHA-256 file integrity** verification
- ✅ **Automatic metadata synchronization** between peer servers
- ✅ **Tiered MP3 caching** with 1–7 day TTL
- ✅ **Full streaming cascade**: local cache → peer server → publisher client
- ✅ **Publisher backup clients** (shared key pair, only one active)
- ✅ **Background cleanup** of expired cache files
- ✅ **Swagger UI** on the server for API exploration

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### Run Two Servers (Simulated Network)

**Terminal 1 — Server A (port 5000)**
```bash
cd DistopiaNetwork.Server
# Edit appsettings.json: ServerId=server-a, Port=5000, PeerServers=["http://localhost:5001"]
dotnet run
```

**Terminal 2 — Server B (port 5001)**
```bash
cd DistopiaNetwork.Server
# Copy appsettings and change: ServerId=server-b, Port=5001, PeerServers=["http://localhost:5000"]
dotnet run --urls http://localhost:5001
```

### Publish a Podcast

```bash
cd DistopiaNetwork.PublisherClient
dotnet run
# Commands: p → publish, q → quit
```

### Listen as Browser

```bash
cd DistopiaNetwork.BrowserClient
dotnet run
# Commands: l → list catalog, s → stream/download, q → quit
```

## API Reference

Base URL: `http://localhost:5000`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/podcasts` | List all podcasts |
| GET | `/podcasts/{id}` | Get podcast by ID |
| GET | `/podcasts/since/{timestamp}` | Sync: get new podcasts since Unix timestamp |
| POST | `/podcast/publish` | Publish metadata (publisher → server) |
| POST | `/podcast/{id}/upload` | Upload MP3 (publisher → server) |
| GET | `/podcast/{id}/stream` | Stream MP3 (browser → server) |
| GET | `/internal/file/{hash}` | Inter-server file transfer |
| GET | `/status` | Server status info |
| GET | `/swagger` | Interactive API docs |

## Security

- Publisher signs metadata with RSA private key
- Servers verify signature on receipt; rejected if invalid
- File integrity verified via SHA-256 on upload and inter-server transfer
- Servers cannot alter podcast content (signatures prevent tampering)

## Spec Sections Implemented

| Section | Feature | Status |
|---------|---------|--------|
| 2 | System entities (Servers, Publishers, Browsers) | ✅ |
| 3 | Metadata structure | ✅ |
| 4 | Digital signatures | ✅ |
| 5 | Server catalog synchronization | ✅ |
| 6–7 | MP3 cache model + policy (1–7 day TTL) | ✅ |
| 8–10 | Full streaming workflow cascade | ✅ |
| 11 | Cache countdown reset on access | ✅ |
| 12 | Publisher backup clients | ✅ |
| 13 | Network scalability | ✅ |
| 14 | Security properties | ✅ |
| 17 | Sequence diagrams (see spec) | ✅ |
