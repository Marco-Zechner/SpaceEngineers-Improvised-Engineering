https://steamcommunity.com/sharedfiles/filedetails/?id=2891367014

# A Fully Networked Grid Pickup / Holding / Manipulation System

You can:

- Grab grids (default up to **20×20×20 m**)
- Carry / drag
- Rotate them (via vanilla keybinds)
- Throw ~~warheads~~ at your friends
- Carry a small self-built gatling gun around (video)
- Align to camera or a reference grid
- Stores per-grid settings automatically (lost after disconnect)

Use `/IME` or `/IME help` in chat for commands.

---

## Multiplayer

Works in:

- Singleplayer
- Client-hosted worlds
- Dedicated servers

Clients move grids correctly.  
All forces and rotation go through a **server-validated system**.

---

# Controls

| Action              | Key                               | Description                                     |
|---------------------|-----------------------------------|-------------------------------------------------|
| Grab / Drop         | R (Reload binding)                | Pick up or release grids                        |
| Rotation Mode       | LMB (Primary tool action)         | Toggle or hold rotation (configurable)          |
| Change Grab Point   | RMB (Secondary tool action)       | Hit ↔ Center of Mass                            |
| Throw               | LMB hold + RMB                    | Throw forward                                   |
| Distance            | Scrollwheel (hardcoded)           | Move grid closer / farther                     |
| Reference Grid      | MMB (hardcoded)                   | Set / clear reference grid                     |
| Toggle Alignment    | Shift (hardcoded)                 | Camera ↔ Grid alignment, disabled while holding W (sprinting) |
| Cycle Facing        | Alt (hardcoded)                   | Change “towards” face                           |
| Cycle Up Face       | Ctrl (hardcoded)                  | Change “up” face                                |

---

# In-game Configuration

Options that can be changed in chat:

## In-game settings

- `/IME modes` – show / hide notifications  
- `/IME rotation` – toggle / hold rotation  
- `/IME keyboard2` – enable CTRL + WASDQE rotation  
- `/IME grabTool` – grab with tool equipped  
- `/IME holdUi` – keep holding during menus / chat  
- `/IME offset` – toggle close-grid offset  
- `/IME lockUse` – interaction while holding  

## Other

- `/IME news` – show last update message  
- `/IME supporters` – list supporters  

## Dev

- `/IME debug` – toggle debug info  
- `/IME state` – show config values  
- `/IME reset` – clear per-grid states  

Most commands accept `on / true` or `off / false`  
(if nothing is provided, they toggle).

There are more settings (e.g. max size, lift force, etc.), but those can **only** be changed via `config.xml` in the world folder:
`%appdata%\spaceengineers\saves\yoursteamid64\yourworldname\Storage\Improvised_Experimentation_mz_00956\config.xml`
