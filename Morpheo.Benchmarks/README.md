# 📊 Morpheo Performance Benchmarks

Ce projet est dédié à la mesure et l'optimisation des performances du framework. Il utilise [BenchmarkDotNet](https://benchmarkdotnet.org/) pour garantir que les chemins critiques (Hot Paths) restent rapides et n'allouent pas de mémoire excessive.

## ⚠️ AVERTISSEMENT IMPORTANT

**Exécutez toujours les benchmarks en mode RELEASE !**

Les résultats obtenus en mode Debug sont invalides car le compilateur n'applique pas les optimisations JIT essentielles.

## 🚀 Lancer les Benchmarks

Pour démarrer la suite de benchmarks :

```bash
dotnet run -c Release --project Morpheo.Benchmarks
```

Vous pourrez ensuite sélectionner interactivement quel benchmark exécuter (ex: `MerkleTreeBenchmarks`).

## 🧪 Scénarios Couverts

Nous surveillons particulièrement les composants suivants :

- **`MerkleTreeBenchmarks`** : Mesure la vitesse de calcul du Hash Root (SHA256) pour des milliers d'éléments. C'est crucial pour l'anti-entropie rapide.
- **`DeltaCompressionBenchmarks`** : Mesure le coût CPU de la génération (Diff) et de l'application (Patch) des changements JSON.
- **`VectorClockBenchmarks`** : Analyse les allocations mémoire lors de la fusion de vecteurs d'horloge massifs.

## 📈 Comprendre les Résultats

Une fois le benchmark terminé, un tableau s'affichera :

| Method | Mean | Gen0 | Allocated |
| :--- | :--- | :--- | :--- |
| **ComputeHash** | **12.5 us** | **0.05** | **120 B** |

- **Mean** : Temps moyen d'exécution (us = microsecondes). Plus c'est bas, mieux c'est.
- **Allocated** : Mémoire allouée par opération. Une valeur élevée (> 1KB) sur un chemin fréquent indique un risque de pression sur le Garbage Collector.

---
---

# 📊 Morpheo Performance Benchmarks (English)

This project is dedicated to measuring and optimizing the framework's performance. It utilizes [BenchmarkDotNet](https://benchmarkdotnet.org/) to ensure that critical Hot Paths remain fast and do not allocate excessive memory.

## ⚠️ IMPORTANT WARNING

**Always run benchmarks in RELEASE mode!**

Results obtained in Debug mode are invalid because the compiler does not apply essential JIT optimizations.

## 🚀 Running Benchmarks

To start the benchmark suite:

```bash
dotnet run -c Release --project Morpheo.Benchmarks
```

You can then interactively select which benchmark to run (e.g., `MerkleTreeBenchmarks`).

## 🧪 Covered Scenarios

We specifically monitor the following components:

- **`MerkleTreeBenchmarks`**: Measures the calculation speed of the Root Hash (SHA256) for thousands of items. Critical for fast anti-entropy.
- **`DeltaCompressionBenchmarks`**: Measures the CPU cost of generating (Diff) and applying (Patch) JSON changes.
- **`VectorClockBenchmarks`**: Analyzes memory allocations during the merge of massive vector clocks.

## 📈 Understanding Results

Once the benchmark is complete, a table will be displayed:

| Method | Mean | Gen0 | Allocated |
| :--- | :--- | :--- | :--- |
| **ComputeHash** | **12.5 us** | **0.05** | **120 B** |

- **Mean**: Average execution time (us = microseconds). Lower is better.
- **Allocated**: Memory allocated per operation. A high value (> 1KB) on a frequent path indicates a risk of Garbage Collector pressure.
