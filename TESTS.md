# 🧪 Morpheo Testing Strategy

Morpheo utilizes a comprehensive validation strategy divided into two main categories:
1. **Unit & Integration Tests** (`Morpheo.Tests`): Validates logic correctness, edge cases, and distributed system behaviors using in-memory simulations.
2. **Performance Benchmarks** (`Morpheo.Benchmarks`): Measures CPU latency, throughput, and memory allocations for critical hot paths.

---

## 1. Running Unit Tests

The test suite is built with **xUnit** and can be executed using the standard .NET CLI.

### 🚀 Execute All Tests
Run the entire suite from the solution root:
```bash
dotnet test
```

### 🔍 Filter Specific Scenarios
To run a specific test case or suite, use the `--filter` option:
```bash
# Run all Merkle Tree tests
dotnet test --filter "MerkleTree"

# Run a specific system replication scenario
dotnet test --filter "Data_Should_Propagate"
```

> **Note:** Integration tests utilize a sophisticated **In-Memory Network Simulator**. This allows complex 3-node mesh replication scenarios (including offline/reconnects) to run instantly without requiring real network ports or firewall configurations.

---

## 2. Running Benchmarks

Performance critical components are profiled using **BenchmarkDotNet**.

### ⚠️ Performance Requirement
Benchmarks **MUST** be compiled in **Release** mode to provide valid results. Running in Debug mode will produce inaccurate metrics due to lack of compiler optimizations.

### 🚀 Execute Benchmarks
Run the following command from the root directory:
```bash
dotnet run -c Release --project Morpheo.Benchmarks
```

### 📊 Understanding Results
After execution, a table will appear with the following metrics:
- **Mean**: The average time taken to execute the operation (lower is better).
- **Gen0**: The number of Garbage Collections in Generation 0 per 1000 operations.
- **Allocated**: The amount of memory allocated per operation (critical for high-throughput sync).

*Example Output:*
| Method | Mean | Gen0 | Allocated |
|------- |-----:|-----:|----------:|
| Merge | 15 ns | 0.00 | 0 B |

---

## 3. Key Test Scenarios

The validation suite covers the following critical areas:

- ✅ **Core Algorithms**
  - **Vector Clocks:** Validation of causality tracking and `Merge` logic.
  - **Merkle Trees:** Ensuring hash determinism for data verification.
  - **CRDTs:** Conflict resolution and Last-Write-Wins fallback.

- ✅ **Storage Layer**
  - **Filesystem:** Log appending and manifest management validity.
  - **SQLite:** EF Core idempotency and timestamp filtering.

- ✅ **System Simulation**
  - **Mesh Replication:** Verified data propagation across a 3-node cluster.
  - **Resilience:** Nodes going offline store no data, but successfully perform a **Cold Sync** ("Catch-Up") upon reconnection using the `InMemoryNetworkSimulator`.

---
---

# 🇫🇷 Stratégie de Test Morpheo

Morpheo utilise une stratégie de validation complète divisée en deux catégories principales :
1. **Tests Unitaires & d'Intégration** (`Morpheo.Tests`) : Valide la correction de la logique, les cas limites et les comportements du système distribué à l'aide de simulations en mémoire.
2. **Benchmarks de Performance** (`Morpheo.Benchmarks`) : Mesure la latence CPU, le débit et les allocations mémoire pour les chemins critiques.

---

## 1. Exécuter les Tests Unitaires

La suite de tests est construite avec **xUnit** et peut être exécutée en utilisant la CLI .NET standard.

### 🚀 Exécuter Tous les Tests
Lancez la suite complète depuis la racine de la solution :
```bash
dotnet test
```

### 🔍 Filtrer des Scénarios Spécifiques
Pour lancer un cas de test ou une suite spécifique, utilisez l'option `--filter` :
```bash
# Lancer tous les tests Merkle Tree
dotnet test --filter "MerkleTree"

# Lancer un scénario spécifique de réplication système
dotnet test --filter "Data_Should_Propagate"
```

> **Note :** Les tests d'intégration utilisent un **Simulateur Réseau En Mémoire** sophistiqué. Cela permet d'exécuter instantanément des scénarios complexes de réplication maillée à 3 nœuds (incluant déconnexions/reconexions) sans nécessiter de ports réseau réels ou de configuration de pare-feu.

---

## 2. Exécuter les Benchmarks

Les composants critiques pour la performance sont profilés en utilisant **BenchmarkDotNet**.

### ⚠️ Prérequis de Performance
Les benchmarks **DOIVENT** être compilés en mode **Release** pour fournir des résultats valides. L'exécution en mode Debug produira des métriques inexactes en raison de l'absence d'optimisations du compilateur.

### 🚀 Exécuter les Benchmarks
Lancez la commande suivante depuis le répertoire racine :
```bash
dotnet run -c Release --project Morpheo.Benchmarks
```

### 📊 Comprendre les Résultats
Après l'exécution, un tableau apparaîtra avec les métriques suivantes :
- **Mean** : Le temps moyen pris pour exécuter l'opération (plus c'est bas, mieux c'est).
- **Gen0** : Le nombre de Garbage Collections en Génération 0 pour 1000 opérations.
- **Allocated** : La quantité de mémoire allouée par opération (critique pour la synchronisation à haut débit).

*Exemple de Sortie :*
| Method | Mean | Gen0 | Allocated |
|------- |-----:|-----:|----------:|
| Merge | 15 ns | 0.00 | 0 B |

---

## 3. Scénarios de Test Clés

La suite de validation couvre les zones critiques suivantes :

- ✅ **Algorithmes Core**
  - **Vector Clocks :** Validation du suivi de causalité et de la logique `Merge`.
  - **Merkle Trees :** Garantie du déterminisme du hachage pour la vérification des données.
  - **CRDTs :** Résolution de conflits et repli Last-Write-Wins.

- ✅ **Couche de Stockage**
  - **Système de Fichiers :** Validité de l'ajout de logs et de la gestion du manifeste.
  - **SQLite :** Idempotence EF Core et filtrage par timestamp.

- ✅ **Simulation Système**
  - **Réplication Maillée :** Vérification de la propagation des données à travers un cluster de 3 nœuds.
  - **Résilience :** Les nœuds hors ligne ne stockent aucune donnée, mais effectuent avec succès une **Cold Sync** ("Rattrapage") lors de la reconnexion en utilisant le `InMemoryNetworkSimulator`.
