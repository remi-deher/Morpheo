# Framework Morpheo

![Build Status](https://img.shields.io/badge/build-passing-brightgreen) ![NuGet Version](https://img.shields.io/nuget/v/Morpheo.Core?label=Morpheo.Core) ![License](https://img.shields.io/badge/license-MIT-blue)

**Morpheo est un Framework de Synchronisation de Données Distribuées (.NET 9+).**

Ce n'est pas une simple librairie réseau, c'est un changement de paradigme. Il transforme des applications client-serveur fragiles en **systèmes distribués, auto-organisés et indestructibles**. Morpheo permet à vos applications d'être "Offline-First" et "Local-First" tout en garantissant une cohérence éventuelle forte à travers un maillage de nœuds.

## 🏗 Architecture

La solution est organisée en composants modulaires conçus pour la flexibilité et la testabilité :

- **`Morpheo.Core`** : Le moteur de synchronisation contenant 80% de la logique (Horloges Vectorielles, Arbres de Merkle, CRDTs, Résolution de Conflits).
- **`Morpheo.Sdk`** : Contrats publics et interfaces légères pour intégrer Morpheo dans vos applications hôtes.
- **`Morpheo.Tests`** : Suite détaillée de tests Unitaires & d'Intégration utilisant un Simulateur Réseau En Mémoire pour valider les comportements distribués robustes.
- **`Morpheo.Benchmarks`** : Outils de profilage de performance pour assurer une latence faible et des allocations mémoire minimales sur les chemins critiques.

> [!IMPORTANT]
> Une documentation détaille se trouve à ce lien https://remi-deher.github.io/Morpheo

## ✨ Fonctionnalités Clés

- **Offline-First & Local-First** : Les nœuds écrivent toujours dans leur base de données locale en premier. La connectivité (Internet/Serveur) est traitée comme une optimisation optionnelle, pas comme une exigence.
- **Sans Conflit (CRDTs)** : Le moteur de résolution gère automatiquement les modifications concurrentes via des CRDTs (Conflict-free Replicated Data Types) ou des stratégies déterministes "Last-Write-Wins".
- **Stockage Agnostique** : Adaptateurs disponibles pour Entity Framework Core (SQLite, SQL Server, PostgreSQL) et stockage Système de Fichiers (Blobs).
- **Sync P2P (Mesh)** : Les nœuds peuvent se synchroniser directement entre eux (Peer-to-Peer) lorsque le serveur central est inaccessible, créant un maillage local résilient.
- **Efficience Bande Passante** : Utilise la **Compression Delta** et les **Arbres de Merkle** (Hash Trees) pour identifier et transférer uniquement le strict minimum de données modifiées.

## 🚀 Démarrage Rapide

### Installation
Morpheo est disponible sous forme de package NuGet. Installez la librairie Core :

```bash
dotnet add package Morpheo.Core
```

### Configuration Minimale (Nœud Standard)
Voici comment démarrer un nœud standard avec la découverte automatique activée :

```csharp
using Morpheo.Core;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateDefaultBuilder();

builder.ConfigureServices(services =>
{
    // 1. Ajouter Morpheo avec configuration basique
    services.AddMorpheo(options => 
    {
        options.NodeName = "Warehouse-Terminal-01";
        options.DiscoveryPort = 5000;
    })
    .UseSqlite(); // 2. Utiliser le stockage interne
});

var host = builder.Build();
await host.RunAsync();
```

## 🧪 Qualité & Tests

Morpheo est construit avec un focus fort sur la fiabilité et la correction dans les scénarios distribués.

- **Tests Unitaires & Intégration** : Validés via [xUnit](https://xunit.net/) et un Simulateur Réseau En Mémoire personnalisé pour prouver la résilience aux partitions et pannes de nœuds.
- **Performance** : Les chemins critiques (Hashing, Compression, Horloges Vectorielles) sont continuellement benchmarkés.

👉 **[Lire la Stratégie de Test Complète (TESTS.md)](./TESTS.md)** pour apprendre comment lancer les tests et interpréter les benchmarks.

## 📜 Licence

Ce projet est sous licence MIT - voir le fichier `LICENSE` pour plus de détails.

---
---

# Morpheo Framework (English)

**Morpheo is a Distributed Data Synchronization Framework built for .NET 9+.**

It transforms fragile client-server applications into persistent distributed systems, capable of operating offline, without central servers, and without complex configuration. Morpheo enables your applications to be "Offline-First" and "Local-First" while ensuring strong eventual consistency across a mesh of nodes.

## 🏗 Architecture

The solution is organized into modular components designed for flexibility and testability:

- **`Morpheo.Core`**: The synchronization engine containing 80% of the logic (Vector Clocks, Merkle Trees, CRDTs, Conflict Resolution).
- **`Morpheo.Sdk`**: Public contracts and lightweight interfaces for integrating Morpheo into your host applications.
- **`Morpheo.Tests`**: Detailed Unit & Integration tests suite utilizing an In-Memory Network Simulator for validating robust distributed behaviors.
- **`Morpheo.Benchmarks`**: Performance profiling tools to ensure low latency and minimal memory allocation on hot paths.

> [!IMPORTANT]
> Detailled documentation can be found at this link https://remi-deher.github.io/Morpheo

## ✨ Key Features

- **Offline-First & Local-First**: Nodes always write to their local database first. Connectivity is treated as an optional optimization, not a requirement.
- **Conflict Free (CRDTs)**: Resolution engine automatically handles concurrent edits using CRDTs (Conflict-free Replicated Data Types) or deterministic Last-Write-Wins strategies.
- **Agnostic Storage**: Adapters available for Entity Framework Core (SQLite, SQL Server, PostgreSQL) and File System storage.
- **P2P Sync**: Nodes can synchronize directly with each other (Peer-to-Peer) when the central server is unreachable, creating a resilient local mesh.
- **Bandwidth Efficient**: Uses **Delta Compression** and **Merkle Trees** (Hash Trees) to identify and transfer only the strict minimum of changed data.

## 🚀 Getting Started

### Installation
Morpheo is available as a NuGet package. Install the core library:

```bash
dotnet add package Morpheo.Core
```

### Minimal Setup (Standard Node)
Here is how to start a standard node with automatic discovery enabled:

```csharp
using Morpheo.Core;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateDefaultBuilder();

builder.ConfigureServices(services =>
{
    // 1. Add Morpheo with basic configuration
    services.AddMorpheo(options => 
    {
        options.NodeName = "Warehouse-Terminal-01";
        options.DiscoveryPort = 5000;
    })
    .UseSqlite(); // 2. Use internal storage
});

var host = builder.Build();
await host.RunAsync();
```

## 🧪 Quality & Testing

Morpheo is built with a strong focus on reliability and correctness in distributed scenarios.

- **Unit & Integration Tests**: Validated using [xUnit](https://xunit.net/) and a custom In-Memory Network Simulator to prove resilience against partitions and node failures.
- **Performance**: Critical paths (Hashing, Compression, Vector Clocks) are continuously benchmarked.

👉 **[Read the Full Testing Strategy (TESTS.md)](./TESTS.md)** to learn how to run tests and interpret benchmarks.

## 📜 License

This project is licensed under the MIT License - see the `LICENSE` file for details.
