# 🧪 Morpheo Test Suite

Ce projet contient l'ensemble des tests automatisés pour valider la robustesse et la fiabilité du framework Morpheo. La stratégie de test est divisée en deux couches principales.

## 📦 Couverture de Tests

### 1. Tests Unitaires (Unit Tests)
Valide la logique pure des composants isolés.
- **Data Sync** : Vérification des algorithmes de synchronisation et des horloges vectorielles.
- **Data Structures** : Tests des Arbres de Merkle et de la compression Delta.
- **Sécurité** : Validation des authentificateurs et des permissions.

### 2. Tests d'Intégration (Integration Tests)
Valide l'interaction entre les composants dans un environnement simulé.
- **Simulateur Réseau In-Memory** : Nous utilisons un `MemoryTransport` spécial qui simule un réseau TCP/UDP sans passer par la stack réseau de l'OS. Cela permet de tester des scénarios complexes (coupure réseau, latence, reconnexion) de manière déterministe et ultra-rapide.

## 🚀 Exécution des Tests

Pour lancer l'ensemble de la suite de tests, exécutez la commande suivante à la racine du projet ou dans ce dossier :

```bash
dotnet test
```

Pour filtrer une catégorie spécifique :

```bash
dotnet test --filter "Category=Sync"
```

## 📂 Structure des Dossiers

L'arborescence des tests reflète celle du projet `Morpheo.Core` pour faciliter la navigation :

- `/Sync` : Tests du moteur de synchronisation et résolution de conflits.
- `/Network` : Tests de découverte et de transport.
- `/Security` : Tests d'authentification et chiffrement.
- `/Simulation` : Outils et mocks pour le simulateur réseau.

---
---

# 🧪 Morpheo Test Suite (English)

This project contains the complete automated test suite to validate the robustness and reliability of the Morpheo framework. The testing strategy is divided into two main layers.

## 📦 Test Coverage

### 1. Unit Tests
Validates the pure logic of isolated components.
- **Data Sync**: Verification of synchronization algorithms and vector clocks.
- **Data Structures**: Tests for Merkle Trees and Delta compression.
- **Security**: Validation of authenticators and permissions.

### 2. Integration Tests
Validates the interaction between components in a simulated environment.
- **In-Memory Network Simulator**: We utilize a special `MemoryTransport` that simulates a TCP/UDP network without traversing the OS network stack. This allows testing complex scenarios (network partition, latency, reconnection) deterministically and at high speed.

## 🚀 Running Tests

To execute the entire test suite, run the following command from the project root or within this directory:

```bash
dotnet test
```

To filter for a specific category:

```bash
dotnet test --filter "Category=Sync"
```

## 📂 Folder Structure

The test directory structure mirrors that of `Morpheo.Core` for easy navigation:

- `/Sync`: Synchronization engine and conflict resolution tests.
- `/Network`: Discovery and transport tests.
- `/Security`: Authentication and encryption tests.
- `/Simulation`: Tools and mocks for the network simulator.
