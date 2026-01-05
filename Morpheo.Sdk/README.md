# Morpheo SDK (Contrats Publics)

**Morpheo.Sdk** contient les abstractions, interfaces et modèles de données nécessaires pour intégrer le framework. Ce paquet est conçu pour être extrêmement léger afin d'être référencé par vos projets "Domain" ou "Shared" sans introduire de dépendances lourdes vers le moteur de synchronisation.

## 📦 Installation

```bash
dotnet add package Morpheo.Sdk
```

## 🔑 Concepts Clés

### 1. `IMorpheoNode`
C'est le contrat principal représentant une instance Morpheo en cours d'exécution.
- **`StartAsync()`** : Initialise la base de données et commence l'écoute réseau (UDP/TCP).
- **`StopAsync()`** : Arrête proprement les services et ferme les connexions.
- **`Discovery`** : Permet de s'abonner aux événements de topologie (`PeerFound`, `PeerLost`).

### 2. Résolution de Conflits (`IMergeable<T>`)
Pour bénéficier des capacités de fusion automatique (CRDT), vos classes de données peuvent implémenter cette interface.
- **`Merge(T remote)`** : Méthode appelée par le `ConflictResolutionEngine` lorsqu'une modification concurrente est détectée. Vous devez y définir la logique métier de fusion (ex: additionner des quantités, concaténer des listes).

### 3. Modèles de Données (`SyncLogDto`)
Le **`SyncLogDto`** est l'objet de transfert standard (DTO) utilisé pour échanger des modifications entre les nœuds. Il encapsule :
- Le contenu de l'entité (JSON).
- Les métadonnées de causalité (Vector Clock).
- L'origine de la modification.

### 4. Configuration (`MorpheoOptions`)
La classe **`MorpheoOptions`** permet de définir le comportement du nœud :
- **`NodeName`** : Identité unique sur le réseau.
- **`Role`** : `StandardClient` (Passif), `Relay`, ou `Server`.
- **`DiscoveryPort`** : Port UDP utilisé pour le multicast.
- **`Capabilities`** : Liste extensible des fonctionnalités du nœud (ex: "HasPrinter", "HasScanner").

---
---

# Morpheo SDK (Public Contracts)

**Morpheo.Sdk** contains the abstractions, interfaces, and data models required to integrate the framework. This package is designed to be extremely lightweight, allowing it to be referenced by your "Domain" or "Shared" projects without introducing heavy dependencies on the synchronization engine.

## 📦 Installation

```bash
dotnet add package Morpheo.Sdk
```

## 🔑 Key Concepts

### 1. `IMorpheoNode`
This is the primary contract representing a running Morpheo instance.
- **`StartAsync()`**: Initializes the database and begins network listening (UDP/TCP).
- **`StopAsync()`**: Gracefully stops services and closes connections.
- **`Discovery`**: Allows subscription to topology events (`PeerFound`, `PeerLost`).

### 2. Conflict Resolution (`IMergeable<T>`)
To leverage automatic conflict resolution capabilities (CRDT), your data classes can implement this interface.
- **`Merge(T remote)`**: Method called by the `ConflictResolutionEngine` when a concurrent modification is detected. You must define the merge business logic here (e.g., adding quantities, concatenating lists).

### 3. Data Models (`SyncLogDto`)
The **`SyncLogDto`** is the standard Data Transfer Object (DTO) used to exchange changes between nodes. It encapsulates:
- The entity content (JSON).
- Causality metadata (Vector Clock).
- The origin of the modification.

### 4. Configuration (`MorpheoOptions`)
The **`MorpheoOptions`** class allows defining the node's behavior:
- **`NodeName`**: Unique identity on the network.
- **`Role`**: `StandardClient` (Passive), `Relay`, or `Server`.
- **`DiscoveryPort`**: UDP port used for multicast.
- **`Capabilities`**: Extensible list of node features (e.g., "HasPrinter", "HasScanner").
