# Krakovia Server

**Krakovia** is an experimental MMORPG project built around a custom client-server architecture, with the game client developed in Unity and the authoritative game server implemented in C# / .NET.

[▶️ Watch Krakovia in action](https://youtu.be/10xy_52HHik?t=820)

This repository contains the **Krakovia game server** and the core systems responsible for networking, world simulation, combat, NPC behavior, persistence, quests, inventory, character progression, and other MMORPG mechanics.

> **Project status:** Experimental / Game Jam project
> **Development time:** 30 days
> **Server:** C# / .NET 9
> **Client:** Unity 6 — private repository

---

## Overview

Krakovia was created as a technical experiment to explore what is involved in building an MMORPG-style game with a custom server architecture rather than relying entirely on Unity's built-in multiplayer abstractions.

The server is designed around an **authoritative game-world model**, where the server manages the state and simulation of connected players and other world entities.

The project includes custom TCP and UDP networking, a server-side update loop, world/entity management, spatial interest management, NPC AI, combat, persistence, quests, inventory, equipment, abilities, loot, and character progression.

---

## Architecture

At a high level, Krakovia follows a client-server architecture:

```text
                    ┌──────────────────────┐
                    │     Unity 6 Client   │
                    │      (Private)       │
                    └──────────┬───────────┘
                               │
                         TCP / UDP
                               │
                               ▼
                    ┌──────────────────────┐
                    │   Krakovia Server    │
                    │     C# / .NET 9      │
                    └──────────┬───────────┘
                               │
          ┌────────────────────┼────────────────────┐
          │                    │                    │
          ▼                    ▼                    ▼
       Networking           World                Gameplay
          │                    │                    │
     TCP / UDP          Entities / NPCs       Combat / Stats
     Connections        Spatial Grid          Quests / Items
     Message Dispatch  Interest Management    Abilities / Loot
          │                    │                    │
          └────────────────────┼────────────────────┘
                               │
                               ▼
                    ┌──────────────────────┐
                    │      Persistence     │
                    │       MariaDB        │
                    └──────────────────────┘
```

The server is responsible for maintaining the authoritative state of the game world and coordinating communication with connected clients.

---

## Features

### Networking

Krakovia implements custom networking using both TCP and UDP.

* TCP server for reliable communication
* UDP server for real-time game communication
* Connection lifecycle management
* Client timeout handling
* Message dispatching
* Server-side update/tick processing
* Player connection and disconnection handling
* Client version validation

The UDP server processes the game world through a scheduled update loop, allowing different server-side systems to execute in a predictable order.

---

### World Simulation

The server maintains a persistent representation of the game world.

World systems include:

* Player entities
* NPC entities
* Gatherable resources
* Spawn points
* World management
* Entity lookup
* Spatial partitioning
* Interest management

The project includes a spatial grid and interest management system to determine which entities are relevant to a particular player instead of treating every entity in the world as equally relevant to every client.

This provides a foundation for reducing unnecessary world-state synchronization as the number of entities increases.

---

### Entity System

The server represents different types of world objects through dedicated server-side instances.

Examples include:

* Players
* NPCs
* Gatherable resources

Entities can be tracked and resolved through the world management systems, allowing gameplay and networking systems to interact with the current state of the world.

---

## NPC AI

Krakovia contains a behavior-based NPC system with multiple behavior implementations.

Current behaviors include:

* Ambient passive behavior
* Ambient wandering
* Fleeing
* Aggressive behavior
* Patrolling
* Guard behavior
* Stationary guards
* Boss behavior
* Training dummies

The behavior system allows different NPC types to use different decision-making strategies without putting every behavior into a single monolithic implementation.

---

## Combat

Combat is handled on the server side and includes systems for:

* Character statistics
* Abilities
* Damage
* Status effects
* Active status effects
* Status effect controllers
* Combat state
* Character progression

Status effects are processed as part of the server simulation, allowing effects such as buffs and debuffs to remain synchronized with the authoritative game state.

---

## Character Progression

The server contains systems for managing character progression, including:

* Experience
* Levels
* Stats
* Equipment
* Inventory
* Abilities
* Loot
* Quests

These systems are separated into dedicated modules rather than being implemented entirely inside the player entity.

---

## Inventory & Equipment

Krakovia includes server-side systems for managing player items and equipment.

The server is responsible for maintaining the authoritative state of:

* Inventory items
* Equipped items
* Item definitions
* Loot
* Equipment-related character state

This prevents the client from being the sole authority over persistent item state.

---

## Quests

The server contains a quest system with data-driven quest definitions.

Quest state can be associated with characters and processed by the server alongside the rest of the gameplay systems.

---

## Data-Driven Content

A significant portion of Krakovia's gameplay content is defined outside the C# gameplay logic.

The project currently contains JSON data for systems such as:

```text
abilities.json
classes.json
gatherables.json
gatherable_spawns.json
items.json
loottables.json
npcs.json
quests.json
spawns.json
status_effects.json
vendors.json
```

This approach allows game content to be modified without requiring every gameplay definition to be hardcoded directly into the server logic.

---

## Persistence

Krakovia separates persistence behind database interfaces.

The project includes implementations for:

```text
IAccountDatabase
├── MariaDBAccountDatabase
└── InMemoryAccountDatabase

ICharacterDatabase
├── MariaDBCharacterDatabase
└── InMemoryCharacterDatabase
```

The MariaDB implementations provide persistent storage, while the in-memory implementations can be used when persistent database storage is not required.

This separation keeps the gameplay layer decoupled from the concrete persistence implementation.

---

## Authentication

The server provides account registration and authentication functionality.

Security-related functionality includes:

* Account registration
* Login validation
* Password hashing using BCrypt
* Client version validation
* Server-side authentication state

The server also validates the client version during connection/authentication to prevent incompatible client versions from entering the game world.

---

## Player Lifecycle

Player lifecycle management includes:

```text
Connect
   ↓
Authenticate
   ↓
Enter World
   ↓
Game Simulation
   ↓
Disconnect / Timeout
   ↓
Persist Character
   ↓
Remove From World
```

When a player leaves the server, their state can be persisted before the player is removed from the active world.

---

## Server Update Loop

The server processes the game world through a scheduled update loop.

A simplified representation of the update flow is:

```text
Server Tick
    │
    ├── NPC AI
    │
    ├── Player Lifecycle
    │
    ├── World Management
    │
    ├── Gatherables
    │
    ├── Interest Management
    │
    ├── Status Effects
    │
    └── Network Message Dispatch
```

The server also monitors tick execution time to identify updates that exceed the expected processing interval.

---

## Web Server

Krakovia also includes a lightweight HTTP server used for server status information.

The web server provides a simple way to expose runtime information without interfering with the main game networking loop.

---

## Project Structure

The server is organized into several areas of responsibility:

```text
KrakoviaServer/
│
├── AI/
│   └── Behaviors/
│
├── Combat/
│
├── Database/
│
├── Entities/
│
├── Equipment/
│
├── Experience/
│
├── Inventory/
│
├── Managers/
│
├── Quests/
│
├── Stats/
│
├── World/
│
├── TCPServer.cs
├── UDPServer.cs
├── WebServer.cs
├── NetworkManager.cs
│
├── abilities.json
├── classes.json
├── items.json
├── npcs.json
├── quests.json
├── spawns.json
└── ...
```

The separation is intended to keep networking, world simulation, persistence, AI, and gameplay systems independently maintainable.

---

## Technology Stack

| Technology | Purpose                   |
| ---------- | ------------------------- |
| C#         | Server implementation     |
| .NET 9     | Server runtime            |
| TCP        | Reliable networking       |
| UDP        | Real-time game networking |
| MariaDB    | Persistent data storage   |
| JSON       | Data-driven game content  |
| BCrypt     | Password hashing          |
| Unity 6    | Game client               |

---

## Development

Krakovia was developed as a **30-day game jam / experimental project**.

The short development cycle was intentional: the project was used to explore the architecture and implementation of a multiplayer MMORPG-style game while prioritizing functional systems over production-level polish.

Because of this, the repository should be considered an **engineering prototype**, not a production-ready MMORPG server.

Some systems are intentionally simplified and would require additional work for a production environment, particularly around scalability, security hardening, automated testing, observability, deployment, and infrastructure.

---

## What I Wanted to Explore

The main technical goals behind Krakovia were:

* Building a custom authoritative game server
* Understanding real-time multiplayer networking
* Separating reliable and unreliable network communication
* Simulating a persistent multiplayer world
* Designing modular MMORPG gameplay systems
* Implementing server-side NPC AI
* Managing entity visibility and spatial relevance
* Persisting player state
* Building data-driven game content
* Exploring the challenges of MMORPG architecture within a constrained development period

---

## Client

The Krakovia client is developed using **Unity 6**.

The client repository is currently private, while this repository contains the server-side implementation.

The two components communicate through the custom networking layer implemented by the Krakovia server.

---

## Project Status

Krakovia is currently an **experimental / portfolio project**.

The project demonstrates the implementation of a custom MMORPG-style server architecture, but it is not intended to represent a production-ready MMO infrastructure.

Future development could include:

* Improved scalability
* Automated testing
* Better observability and monitoring
* More robust networking protocols
* Infrastructure and deployment automation
* Additional gameplay systems
* Client-side improvements
* Performance profiling and optimization

---

## License

This project is currently provided for portfolio and educational purposes.

See the repository for the current licensing terms.
