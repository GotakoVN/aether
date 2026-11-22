# aether-tui — User Guide

> Terminal UI client for Aether AI · Athanor Fire theme 🔥

---

## Requirements

- The Aether backend is running (port 5099 by default)
- A Linux terminal with `TERM=xterm-256color`, or kitty/alacritty
- `~/.aether/config.json` (optional — see the Config section)

---

## Quick start

### Option 1 — Automatic script (recommended)

```bash
# Kill any old server → start a new server → launch the TUI
cd ~/repo/aether
./clients/aether-tui/tui.sh
```

The script will:
1. Kill any process that holds port 5099
2. Start the Aether backend in the background
3. Wait for the backend to be ready (up to 10 s)
4. Launch `aether-tui`
5. Kill the backend automatically when the TUI exits

```bash
# Build the binary before running (first time, or after changing code)
./clients/aether-tui/tui.sh --build

# Connect to a different group
./clients/aether-tui/tui.sh --group general

# Connect to a remote backend
./clients/aether-tui/tui.sh --url ws://192.168.1.10:5099/ws
```

### Option 2 — Manual

```bash
# Terminal 1: start the backend
cd ~/repo/aether
./run.sh

# Terminal 2: launch the TUI
./clients/aether-tui/target/release/aether-tui
```

---

## CLI arguments

```
aether-tui [OPTIONS]

Options:
  --url <URL>      WebSocket URL override
                   Default: ws://localhost:5099/ws
  --group <NAME>   Agent group to connect to
                   Default: maria
  --help           Show help
```

---

## Interface

```
┌─ Aether · Maria ─────────────────────── deepseek-r1 · Think:high ─┐
│                                                                     │
│  21:20  You                                                         │
│  ╰─ explain Ratatui                                                 │
│                                                                     │
│  21:21  Maria                                                       │
│  ╰─ Ratatui is a Rust TUI framework...▋                             │
│                                                                     │
│─────────────────────────────────────────────────────────────────────│
│ › _                                                                 │
└─ ● Maria · Normal ──────── [F2] Models [F1] Help [Ctrl+Q] Quit ────┘
```

**Colors (Athanor Fire theme):**
- 🟦 User name — `#5BC8F5` ice blue
- 🟠 Agent name — `#FFB347` amber
- 🔶 Streaming cursor `▋` — `#FF6B00` fire orange
- 🟢 Connected dot `●` — `#44FF88` green
- ⚫ Background — `#0D0D0D` near-black

---

## Keyboard shortcuts

### Normal mode

| Key | Action |
|-----|--------|
| `Enter` | Send the message |
| `Ctrl+Q` | Quit (the terminal is restored) |
| `Ctrl+L` | Clear the screen (does not delete server history) |
| `Esc` | Enter Scroll mode |
| `F1` or `?` | Show the help popup |
| `F2` or `Ctrl+M` | Open the model picker |
| `Backspace` | Delete a character |

### Scroll mode (press `Esc` to enter)

| Key | Action |
|-----|--------|
| `k` / `↑` | Scroll up (older messages) |
| `j` / `↓` | Scroll down (newer messages) |
| `PgUp` | Scroll up 10 lines |
| `PgDn` | Scroll down 10 lines |
| `G` | Jump to the bottom (latest) |
| `gg` | Jump to the top (oldest) |
| Mouse wheel | Scroll up/down 3 lines |
| `Esc` | Leave Scroll mode → Normal |
| Any character | Leave Scroll mode and type that character |

### Model picker (`F2`)

| Key | Action |
|-----|--------|
| `j` / `↓` | Next model |
| `k` / `↑` | Previous model |
| `Enter` | Select the model |
| `Esc` | Close the picker |

---

## Slash commands

### LOCAL (handled in the TUI, not sent to the server)

| Command | Action |
|---------|--------|
| `/clear` | Clear the screen |
| `/help` | Show the help popup |
| `/quit` or `/q` | Quit |

### FORWARDED (sent to the Aether backend)

| Command | Action |
|---------|--------|
| `/model <name>` | Change the model (e.g. `/model google/gemini-2.5-flash`) |
| `/models` | List available models |
| `/think <low\|medium\|high>` | Set the thinking effort |
| `/new` | Create a new session |
| `/reset` | Reset the session |
| `/context` | Show the current context |
| `/compact` | Compact the conversation history |
| `/tools` | List available tools |

---

## Config

The TUI resolves the WebSocket URL in this order:

1. **CLI flag** `--url ws://...` — overrides everything
2. **Env var** `AETHER_WS_URL=ws://host:port/ws`
3. **Config file** `~/.aether/config.json`:
   ```json
   {
     "agents": {
       "maria": {
         "workspace": "/path/to/maria/workspace"
       }
     }
   }
   ```
   → reads `{workspace}/.aether.json` → takes `websocket.port`
4. **Fallback** `ws://localhost:5099/ws`

---

## Build from source

```bash
cd ~/repo/aether/clients/aether-tui

# Debug build (faster, for development)
cargo build

# Release build (optimized, for real use)
cargo build --release

# Binary output:
# clients/aether-tui/target/release/aether-tui  (~2.9MB)
```

---

## Troubleshooting

**The TUI cannot connect:**
```bash
# Check whether the backend is running
fuser 5099/tcp

# Watch the backend log
tail -f /tmp/aether-server.log

# Kill the old backend and restart
fuser -k 5099/tcp && ./run.sh
```

**The screen is garbled after an abrupt Ctrl+C:**
```bash
reset
```

**Streaming does not show — only the complete message appears:**
The Aether backend may be running in non-streaming mode. Check `appsettings.json` to see whether streaming is enabled.

**The model picker shows "Loading models…":**
The backend needs the Phase 3 WS handlers (already implemented). Restart the backend to load the new code.
