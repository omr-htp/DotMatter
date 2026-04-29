# DotMatter.Core Benchmark Thresholds

This document defines the initial pass/fail thresholds for the benchmark harness in `DotMatter.Benchmarks`.

## Measurement notes

- Baseline sample captured on 2026-04-18 using the local desktop environment.
- The numbers below are intentionally looser than the baseline so normal machine-to-machine variation does not cause noisy failures.
- Thresholds should only be tightened after repeated runs on representative CI or release hardware.

## Current baselines

| Benchmark | Sample mean | Sample allocation |
| --- | ---: | ---: |
| `SessionCryptoBenchmarks.DeriveCaseSigma2Key` | `4.589 us` | `880 B` |
| `TlvBenchmarks.EncodeCertificateLikePayload` | `226.1 ns` | `656 B` |
| `TlvBenchmarks.DecodeReportPayloadToString` | `1.213 us` | `3968 B` |

## Release thresholds

| Benchmark | Mean threshold | Allocation threshold |
| --- | ---: | ---: |
| `SessionCryptoBenchmarks.DeriveCaseSigma2Key` | `<= 10 us` | `<= 1024 B` |
| `TlvBenchmarks.EncodeCertificateLikePayload` | `<= 500 ns` | `<= 1024 B` |
| `TlvBenchmarks.DecodeReportPayloadToString` | `<= 2 us` | `<= 4608 B` |

## Usage

Run the benchmark harness from the repo root:

```powershell
dotnet run --project DotMatter.Benchmarks -c Release
```

For quick smoke checks without a full benchmark session:

```powershell
dotnet run --project DotMatter.Benchmarks -c Release -- --list flat
```
