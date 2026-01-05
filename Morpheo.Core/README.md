# Morpheo.Core : Le Moteur de Cohérence Distribuée

[![NuGet](https://img.shields.io/nuget/v/Morpheo.Core.svg)](https://www.nuget.org/packages/Morpheo.Core/)
[![Build Status](https://img.shields.io/badge/Build-Passing-brightgreen.svg)]()

> **Vision & Philosophie : Une complexité interne invisible pour une simplicité externe absolue.**

---

## 📑 Table des Matières
1.  [Vision : La Salle des Machines](#1-vision--la-salle-des-machines)
2.  [Architecture Interne : Le Pipeline Réactif](#2-architecture-interne--le-pipeline-réactif)
3.  [Les 3 Piliers de l'Implémentation](#3-les-3-piliers-de-limplémentation)
4.  [Orchestration des Processus (Hosted Services)](#4-orchestration-des-processus-hosted-services)
5.  [Guide du Contributeur : Règles d'Or](#5-guide-du-contributeur--règles-dor)

---

## 1. Vision : La Salle des Machines

**Morpheo.Core** ne contient aucune logique métier. Son unique responsabilité est d'être la "Glu" invisible et indestructible entre trois mondes hostiles :
1.  **Le Réseau** (Instable, Latent, Partitionné).
2.  **Le Disque** (Lent, Faillible).
3.  **La Logique Applicative** (Exigeante, Concurrente).

Il agit comme un noyau de système d'exploitation distribué, garantissant que malgré le chaos ambiant (coupure Wifi, crash disque), l'intégrité des données est préservée.

---

## 2. Architecture Interne : Le Pipeline Réactif

Le cœur de Morpheo est un **Pipeline de Traitement de Paquets** hautement optimisé. Voici le chemin critique d'une donnée (`SyncLog`) depuis son arrivée sur le réseau jusqu'à sa persistence durable.

```mermaid
graph TD
    A[Listener (UDP/HTTP)] -->|Binary/Json| B(Deserializer)
    B -->|SyncLogDto| C{Routing Strategy}
    C -->|Relevant?| D[DataSyncService (The Brain)]
    D -->|Conflict Check| E{Conflict Resolver (Vector Clock)}
    E -->|Approved| F[HybridLogStore]
    F -->|Hot Path (Fast)| G[FileLogStore (LSM Append-Only)]
    F -->|Cold Path (Archive)| H[SqlSyncLogStore (EF Core)]
    D -->|Ack| I[Network Acknowledge]
```

Ce pipeline est conçu pour être **Non-Bloquant**. L'écriture disque (Hot Path) est découplée de l'archivage SQL (Cold Path), permettant un débit d'ingestion massif (Backpressure maitrisée).

---

## 3. Les 3 Piliers de l'Implémentation

Pourquoi Morpheo est-il robuste ? Parce qu'il repose sur des choix d'architecture bas niveau radicaux.

### A. Stockage Hybride (LSM + SQL)
*   **Le Problème : ** Les bases SQL (B-Tree) sont trop lentes pour l'écriture massive de logs (Write Amplification). Les fichiers plats sont rapides mais difficiles à requêter.
*   **La Solution Morpheo : **
    *   **Hot Store (LSM) : ** Écriture séquentielle pure dans des fichiers `.jsonl`. Chaque entrée est protégée par un **CRC32**. Si le courant saute pendant l'écriture, la corruption est détectée et isolée au redémarrage (Crash-Safety).
    *   **Cold Store (SQL) : ** Un processus d'arrière-plan déplace calmement les données vers SQLite/Postgres pour l'historique infini.

### B. Consistance via Merkle Trees (Anti-Entropy)
*   **Le Problème : ** En UDP (Gossip), des paquets se perdent. Comment savoir si deux nœuds sont parfaitement synchronisés sans tout comparer (trop coûteux) ?
*   **La Solution Morpheo : ** Chaque nœud maintient un **Arbre de Merkle** (Hash Tree) de ses données. Pour se synchroniser, ils comparent juste la racine (Root Hash). Si elle diffère, ils descendent intelligemment dans l'arbre pour trouver *le* paquet manquant. C'est la technologie derrière Git et Bitcoin.

### C. Compression Récursive (Deep Diff RFC 6902)
*   **Le Problème : ** Envoyer un objet JSON entier de 10KB pour changer une propriété booléenne est un gaspillage criminel de bande passante 4G.
*   **La Solution Morpheo : ** L'algorithme `DeltaCompressionService` traverse le graphe d'objet et génère un **Patch JSON** minimal.
    *   *Avant :* `{ "id": "u1", "deeply": { "nested": { "value": "new" } }, ... }` (Tout l'objet)
    *   *Après :* `[ { "op": "replace", "path": "/deeply/nested/value", "value": "new" } ]` (40 octets)

---

## 4. Orchestration des Processus (Hosted Services)

Morpheo maintient plusieurs boucles de contrôle autonomes (`IHostedService`) pour assurer la santé du système en tâche de fond.

| Service | Rôle Critique | Fréquence |
|:---|:---|:---|
| **`DataSyncService`** | Orchestrateur Principal. Gère la file d'attente des événements entrants. | Temps Réel |
| **`HybridLogStore` (Archiver)** | "Garbage Collector" des logs. Déplace les données du Hot (Fichier) vers le Cold (SQL). | 10 min |
| **`FileLogStore` (Flusher)** | Vide le tampon mémoire (MemTable) sur le disque pour minimiser la perte en cas de crash. | 2 sec / 100 items |
| **`UdpDiscoveryService`** | Phare réseau. Envoie des "Heartbeats" pour maintenir la table de voisinage à jour. | 3 sec |
| **`AntiEntropyService`** | Le Réparateur. Initie des sessions de réconciliation Merkle avec des pairs aléatoires. | 30 sec |

---

## 5. Guide du Contributeur : Règles d'Or

Contribuer au Core est une responsabilité majeure. Une erreur ici peut corrompre les données de milliers d'utilisateurs.

1.  **Crash Safety First : ** Toute modification du `FileLogStore` doit être accompagnée d'un test de simulation de coupure de courant (écriture partielle).
2.  **No Deadlocks : ** Le moteur est hautement concurrent. L'utilisation de `.Result` ou `.Wait()` est formellement interdite. Tout doit être `async`.
3.  **Backward Compatibility : ** Le format de sérialisation (`SyncLogDto`) est sacré. Ne jamais retirer un champ ou changer son ID binaire (MessagePack).
4.  **Zero-Allocation Focus : ** Dans le Hot Path, préférez `Span<T>` et `System.Text.Json.Nodes` pour minimiser la pression sur le Garbage Collector.

---
*Architecturé avec précision pour .NET 10.*
