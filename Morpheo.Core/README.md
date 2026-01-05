# Morpheo Core Engine

**Morpheo.Core** est le cœur du framework. Il contient l'ensemble de la logique de synchronisation, les algorithmes distribués, et les implémentations par défaut pour le stockage et le réseau.

## 🏗 Architecture Interne

Le moteur est orchestré par le **`DataSyncService`**, qui agit comme le chef d'orchestre des données. Son rôle est de :
1. Intercepter les modifications locales.
2. Les logger de manière immuable avec un vecteur de temps.
3. Les diffuser intelligemment sur le réseau (Push).
4. Recevoir et appliquer les modifications distantes (Pull/Merge).

### Algorithmes Clés
Morpheo utilise des structures de données avancées pour garantir la cohérence :

- **Vector Clocks (Causalité)** : Chaque modification porte une horloge logique (ex: `[A:1, B:2]`) permettant de détecter si deux événements sont séquentiels ou concurrents, sans dépendre de l'horloge système (NTP).
- **Merkle Trees (Anti-entropie)** : Un arbre de hachage permet de comparer rapidement des gigaoctets de données entre deux nœuds pour trouver les différences exactes sans tout transférer.
- **Delta Compression** : Seuls les octets modifiés (diff) sont transmis sur le réseau, réduisant drastiquement la consommation de bande passante.

## 💾 Storage Providers

Morpheo est agnostique au stockage, mais fournit des implémentations robustes clé-en-main :

| Provider | Description | Usage |
| :--- | :--- | :--- |
| **SQlite** | Stockage relationnel embarqué performant. Supporte WAL mode pour la concurrence. | Default |
| **In-Memory** | Stockage volatile pour les tests ou les nœuds éphémères. | Testing |
| **FileSystem** | Stockage de gros fichiers (BLOBs) directement sur le disque. | Assets |

## 🌐 Network Capabilities

Le framework supporte nativement plusieurs protocoles de transport pour s'adapter à la topologie :

- **HTTP/REST** : Pour les échanges standards et la compatibilité firewall.
- **SignalR (WebSockets)** : Pour la notification temps réel ("Push") et les mises à jour instantanées.
- **UDP Multicast** : Pour la découverte automatique des pairs (Zero-Conf) sur le réseau local.

## ⚙️ Configuration Avancée (Builder)

L'initialisation se fait via une API fluide (`Fluent API`) dans le conteneur d'injection de dépendances .NET classique :

```csharp
services.AddMorpheo(options => 
{
    options.NodeName = "Server-01";
    options.Role = NodeRole.Server;
})
.UseSqlite("Data Source=morpheo_core.db") // Choix du stockage
.AddExternalDbSync<MyAppDbContext>()      // Synchronisation d'une DB existante
.EnableDashboard();                       // Activation de l'UI d'admin
```

---
---

# Morpheo Core Engine (English)

**Morpheo.Core** is the core of the framework. It acts as the execution engine containing all synchronization logic, distributed algorithms, and default implementations for storage and networking.

## 🏗 Internal Architecture

The engine is orchestrated by the **`DataSyncService`**, which acts as the data conductor. Its role is to:
1. Intercept local changes.
2. Log them immutably with a time vector.
3. Intelligently broadcast them over the network (Push).
4. Receive and apply remote changes (Pull/Merge).

### Key Algorithms
Morpheo uses advanced data structures to ensure consistency:

- **Vector Clocks (Causality)**: Each modification carries a logical clock (e.g., `[A:1, B:2]`) used to detect if two events are sequential or concurrent, without relying on the system clock (NTP).
- **Merkle Trees (Anti-entropy)**: A hash tree allows rapid comparison of gigabytes of data between two nodes to find exact differences without transferring everything.
- **Delta Compression**: Only modified bytes (diff) are transmitted over the network, drastically reducing bandwidth consumption.

## 💾 Storage Providers

Morpheo is storage-agnostic but provides robust turnkey implementations:

| Provider | Description | Usage |
| :--- | :--- | :--- |
| **SQlite** | High-performance embedded relational storage. Supports WAL mode for concurrency. | Default |
| **In-Memory** | Volatile storage for tests or ephemeral nodes. | Testing |
| **FileSystem** | Storage for large files (BLOBs) directly on disk. | Assets |

## 🌐 Network Capabilities

The framework natively supports multiple transport protocols to adapt to the topology:

- **HTTP/REST**: For standard exchanges and firewall compatibility.
- **SignalR (WebSockets)**: For real-time notification ("Push") and instant updates.
- **UDP Multicast**: For automatic peer discovery (Zero-Conf) on the local network.

## ⚙️ Advanced Configuration (Builder)

Initialization is done via a Fluent API within the classic .NET dependency injection container:

```csharp
services.AddMorpheo(options => 
{
    options.NodeName = "Server-01";
    options.Role = NodeRole.Server;
})
.UseSqlite("Data Source=morpheo_core.db") // Storage choice
.AddExternalDbSync<MyAppDbContext>()      // Existing DB synchronization
.EnableDashboard();                       // Enable Admin UI
```
