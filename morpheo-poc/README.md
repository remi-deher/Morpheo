# Morpheo - Docker Proof of Concept (PoC)

Ce dossier contient un environnement de démonstration complet pour le framework Morpheo, permettant de tester et valider les différents modes de synchronisation distributed/offline-first.

L'architecture se compose de :
*   **1 Serveur Central (`server`)** : Fait office de relais central HTTP et serveur de hub SignalR.
*   **3 Clients (`client1`, `client2`, `client3`)** : Des nœuds indépendants qui exécutent l'application.

Chaque nœud dispose :
1.  D'une **API utilisateur** sur un port dédié (CRUD de Todos).
2.  De l'**infrastructure Morpheo** (Moteur de Synchro + Dashboard d'administration) sur le port `500x`.

---

## 📑 Table des Matières
1. [Ports et Services](#-ports-et-services)
2. [Démarrage Rapide](#-démarrage-rapide)
3. [Les 4 Modes de Synchronisation](#-les-4-modes-de-synchronisation)
4. [Changement Dynamique de Mode (On-the-fly)](#-changement-dynamique-de-mode-on-the-fly)
5. [Scénario de Test Guidé (P2P & Résolution de conflits)](#-scénario-de-test-guidé)

---

## 🔌 Ports et Services

| Service | Port API Client (Todo CRUD) | Port Morpheo (Sync & Dashboard) | Dashboard URL |
| :--- | :--- | :--- | :--- |
| **server** | `8080` | `5000` | [http://localhost:5000/dashboard](http://localhost:5000/dashboard) |
| **client1** | `8081` | `5001` | [http://localhost:5001/dashboard](http://localhost:5001/dashboard) |
| **client2** | `8082` | `5002` | [http://localhost:5002/dashboard](http://localhost:5002/dashboard) |
| **client3** | `8083` | `5003` | [http://localhost:5003/dashboard](http://localhost:5003/dashboard) |

---

## 🚀 Démarrage Rapide

1.  **Lancer les conteneurs** (depuis le dossier `morpheo-poc`) :
    ```bash
    docker compose build
    docker compose up -d
    ```
2.  **Vérifier le statut des nœuds** avec l'utilitaire `test.sh` :
    ```bash
    chmod +x test.sh
    ./test.sh status
    ```
3.  Vous pouvez également ouvrir les Dashboards Morpheo de chaque conteneur dans votre navigateur (voir liens dans la section [Ports et Services](#-ports-et-services)).

---

## 🌐 Les 4 Modes de Synchronisation

Vous pouvez modifier le mode de fonctionnement de deux façons :
*   **Statiquement** : en modifiant la variable d'environnement `MODE` dans le fichier `docker-compose.yml` et en redémarrant le conteneur.
*   **Dynamiquement** : depuis le Dashboard web ou via l'API, comme décrit dans la section suivante.

Voici comment tester chaque mode :

### Mode A : Local-Only (`MODE=local-only`)
Le nœud n'écoute pas sur le réseau et ne cherche aucun pair.
*   **Comportement** : Ajoutez un Todo sur `client1`. Listez les Todos sur `client2` ou `server` ; ils restent vides. L'application conserve les données localement sans aucune interaction.

### Mode B : P2P Mesh (`MODE=p2p-mesh`)
Les clients découvrent automatiquement leurs pairs sur le réseau local via UDP Multicast (Port 5000) et se synchronisent en direct (Peer-to-Peer) sans serveur central.
*   **Comportement** : Ajoutez un Todo sur `client1`. Il se propage instantanément sur `client2` et `client3`. Le `server` central, lui, ne reçoit rien si sa configuration n'est pas en P2P.

### Mode C : Client-Serveur (`MODE=client-server`)
Les clients n'utilisent pas la découverte locale. Ils poussent leurs modifications au serveur central en HTTP et se connectent au Hub SignalR du serveur central pour recevoir les mises à jour temps réel.
*   **Comportement** : Un changement sur `client1` est envoyé au `server`, qui le propage ensuite à `client2` et `client3`. Si le `server` est éteint (`docker compose stop server`), la synchronisation s'arrête immédiatement entre les clients.

### Mode D : Hybride (`MODE=hybrid`) (Default)
Le meilleur des deux mondes. Les clients se synchronisent localement en P2P (gratuit et ultra-rapide) et envoient également une copie de sauvegarde au serveur central dans le Cloud quand il est disponible.
*   **Comportement** : Même si le serveur central est éteint, les clients s'échangent les Todos en direct via le Mesh local. Dès que le serveur redémarre, ils rechargent l'historique manqué (Cold Sync & Merkle Tree reconciliation).

---

## ⚡ Changement Dynamique de Mode (On-the-fly)

Morpheo intègre un **Manager de Configuration Runtime** qui sauvegarde les paramètres dans un fichier `morpheo.runtime.json` persistant. 

### Depuis l'API (ex. sur `client1` - Port 5001) :
1.  **Récupérer la configuration actuelle** :
    ```bash
    curl http://localhost:5001/dashboard/api/sys/config
    ```
2.  **Modifier la configuration** (ex. Désactiver le Mesh P2P et activer uniquement le serveur central) :
    ```bash
    curl -X POST -H "Content-Type: application/json" \
      -d '{"nodeName":"client1","httpPort":5000,"databaseType":"Sqlite","connectionString":"Data Source=/app/data/morpheo.db","enableMesh":false,"centralServerUrl":"http://server:5000"}' \
      http://localhost:5001/dashboard/api/sys/config
    ```
3.  **Redémarrer le nœud** pour appliquer :
    ```bash
    curl -X POST http://localhost:5001/dashboard/api/sys/restart
    ```
    *Note : Le conteneur s'arrête, et grâce à la règle `restart: always` de Docker Compose, il redémarre instantanément en chargeant la nouvelle configuration.*

---

## 🧪 Scénario de Test Guidé

Voici un scénario complet pour tester la synchronisation hybride et la résolution de conflits (LWW - Last Write Wins) :

1.  **Ajouter un Todo sur `client1`** :
    ```bash
    ./test.sh add 1 "Tâche initiale client 1"
    ```
2.  **Vérifier la propagation immédiate** sur les autres nœuds :
    ```bash
    ./test.sh list 2
    ./test.sh list server
    ```
3.  **Simuler une coupure du réseau central** (Arrêt du serveur) :
    ```bash
    docker compose stop server
    ```
4.  **Ajouter un Todo sur `client2` hors ligne** :
    ```bash
    ./test.sh add 2 "Achat local P2P"
    ```
    *   Vérifiez que le Todo s'est propagé sur `client1` et `client3` grâce au **Mesh P2P local**, même si le serveur central est éteint.

5.  **Créer un conflit (Modifications hors ligne concurrentes)** :
    *   Coupez la connexion de `client1` (ex. en changeant sa config en `local-only` ou en arrêtant son conteneur).
    *   Modifiez un même Todo existant sur `client1` et `client2` avec des titres différents.
    *   Reconnectez `client1`.
    *   Morpheo va comparer les **Vector Clocks (Horloges vectorielles)** et fusionner les modifications en appliquant le LWW (le timestamp le plus récent l'emporte).

6.  **Relancer le serveur central** :
    ```bash
    docker compose start server
    ```
    *   Les nœuds vont déclencher une **Reconciliation de Merkle Tree** (Anti-Entropy) en arrière-plan et pousser le retard accumulé au serveur central.
