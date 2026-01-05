# Morpheo.Sdk : Le Kit d'Extension Universel

[![NuGet](https://img.shields.io/nuget/v/Morpheo.Sdk.svg)](https://www.nuget.org/packages/Morpheo.Sdk/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](./LICENSE)

> **Vision & Philosophie : Ouvert à l'extension, fermé à la modification.**

---

## 📑 Table des Matières
1.  [Vision : La Prise Universelle](#1-vision--la-prise-universelle)
2.  [Les 3 Piliers d'Extension (Transport, Stockage, Hardware)](#2-les-3-piliers-dextension)
3.  [Matrice de Décision : Quoi Étendre et Quand ?](#3-matrice-de-décision--quoi-étendre-et-quand-)
4.  [Modélisation de Données (DTOs)](#4-modélisation-de-données)
5.  [Tutoriel : Créer votre Premier Plugin](#5-tutoriel--créer-votre-premier-plugin)

---

## 1. Vision : La Prise Universelle

Morpheo est conçu selon le principe **Open-Closed**. Le moteur (`Morpheo.Core`) est un bloc scellé garantissant la stabilité, mais le système est conçu pour être infiniment extensible via le SDK.

Le **Morpheo.Sdk** agit comme une "prise de courant universelle". Il expose les contrats (Interfaces) nécessaires pour brancher n'importe quelle technologie tierce sans jamais toucher au code source du moteur.

*Vous voulez synchroniser via Bluetooth ? Stocker dans MongoDB ? Imprimer sur un bras robotique ?* **C'est ici que ça se passe.**

---

## 2. Les 3 Piliers d'Extension

Pour interagir avec le framework, vous implémentez l'une de ces trois interfaces fondamentales.

### A. Transport (`ISyncStrategyProvider`)
*La voix de Morpheo.*
Cette interface définit *comment* les données voyagent.
*   **Implémentation par défaut :** UDP Multicast, HTTP/SignalR.
*   **Vos extensions possibles :** Bluetooth LE, LoRaWAN, RabbitMQ, Échange de Fichiers USB, Azure Service Bus.

### B. Stockage (`ISyncLogStore`)
*La mémoire de Morpheo.*
Cette interface définit *où* les données reposent.
*   **Implémentation par défaut :** FileLogStore (LSM), SqlSyncLogStore (EF Core).
*   **Vos extensions possibles :** Redis, MongoDB, CosmosDB, AWS S3, XML Files.

### C. Hardware (`IPrintGateway`)
*Les bras de Morpheo.*
Cette interface abstrait les périphériques physiques du monde réel.
*   **Implémentation par défaut :** WindowsPrinterService (WinSpool).
*   **Vos extensions possibles :** Android Bluetooth Printer, CUPS (Linux), GPIO (Raspberry Pi), Écrans Série.

---

## 3. Matrice de Décision : Quoi Étendre et Quand ?

Ne réinventez pas la roue inutilement. Utilisez ce tableau pour identifier le bon point d'extension pour votre besoin.

| Votre Besoin | Interface à Implémenter | Complexité | Exemple Concret |
|:---|:---|:---|:---|
| **Connecter une nouvelle BDD** | `ISyncLogStore` | 🟡 Moyenne | Stocker les logs dans une base Neo4j existante. |
| **Réseau IoT Spécifique** | `ISyncStrategyProvider` | 🔴 Haute | Sync de capteurs via radio ZigBee propriétaire. |
| **Hardware Exotique** | `IPrintGateway` | 🟢 Faible | Piloter une imprimante ticket Epson via COM3. |
| **Règles Métier RH** | `IConflictResolver` | 🟡 Moyenne | Fusionner deux fiches employés selon l'ancienneté. |
| **Nouvelle Entité** | `MorpheoType` | 🟢 Très Faible | Ajouter `Product`, `Invoice` au modèle de données. |

---

## 4. Modélisation de Données

Pour communiquer avec le noyau, vous utilisez des objets de transfert standardisés.

*   `MorpheoEntity` : La classe mère obligatoire pour vos modèles. Elle gère automatiquement les `Id` (GUID) et les timestamps de modification.
*   `SyncLogDto` : L'enveloppe de transport universelle. Elle contient :
    *   Le Payload (Donnée brute ou Patch Delta).
    *   L'Action (`UPDATE`, `DELETE`).
    *   La Vector Clock (Pour la résolution de conflits).

---

## 5. Tutoriel : Créer votre Premier Plugin

Objectif : Créer une stratégie de transport "Debug" qui affiche les synchronisations dans la console au lieu de les envoyer sur le réseau.

### Étape 1 : Implémenter l'Interface

```csharp
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

// 1. On implémente le contrat de Transport
public class ConsoleDebugStrategy : ISyncStrategyProvider
{
    private readonly ILogger _logger;

    public ConsoleDebugStrategy(ILogger<ConsoleDebugStrategy> logger)
    {
        _logger = logger;
    }

    // 2. Méthode appelée par le moteur à chaque sync
    public async Task PropagateAsync(SyncLogDto log, IEnumerable<PeerInfo> peers)
    {
        // Au lieu d'envoyer des paquets UDP, on écrit juste
        _logger.LogInformation($"[DEBUG-PLUGIN] 📡 Syncing Entity {log.EntityId} ({log.Action}) to {peers.Count()} peers.");
        
        // Simulez un délai réseau
        await Task.Delay(10);
    }
}
```

### Étape 2 : Brancher le Plugin (Injection de Dépendances)

Dans le fichier `Program.cs` de votre application :

```csharp
builder.Services.AddMorpheo(morpheo =>
{
    // ... configuration standard ...

    // Enregistrement du plugin.
    // Morpheo détecte automatiquement toutes les implémentations de ISyncStrategyProvider.
    morpheo.Services.AddSingleton<ISyncStrategyProvider, ConsoleDebugStrategy>();
});
```

C'est tout. Votre plugin est maintenant un citoyen de première classe dans l'écosystème Morpheo.

---
*Le pouvoir de l'adaptation, sans la complexité.*
