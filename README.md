# OpenTelemetry Observability Demo

A local demonstration project showcasing end-to-end observability using OpenTelemetry with the LGTM stack (Loki, Grafana, Tempo, and Prometheus eventually to be replace by Mimir).

In the future, we will transform this project to a hybrid solution to export telemetry on-prem/cloud.

**Local/On-Premises Hosting**
1. **Docker Compose:** Simple single-node setup, ideal for development and small-scale deployments
2. **Kubernetes (K3s/MicroK8s):** Lightweight Kubernetes distributions for edge or on-prem servers

**Cloud Hosting**

1. **Azure Container Apps:** Serverless container hosting with built-in scaling
2. **Azure Kubernetes Service (AKS)** – Managed Kubernetes for production workloads

**Managed Observability Alternatives**

1. **Grafana Cloud:** Fully managed Loki, Tempo, Prometheus, and Grafana
2. **Azure Monitor + Application Insights:** Native Azure observability with OpenTelemetry support. However, this might lock us with a vendor which doesn't make it a viable option.
3. **Datadog / New Relic / Dynatrace:** Third-party APM platforms with OTLP ingestion (all of them are pricey!)

## Architecture

```
┌─────────────────┐         ┌─────────────────┐
│  Camera Gateway │────────▶│  Alert Service  │
└────────┬────────┘         └────────┬────────┘
         │                           │
         │      OTLP (traces,        │
         │       logs, metrics)      │
         ▼                           ▼
┌──────────────────────────────────────────────┐
│           OpenTelemetry Collector            │
└──────┬─────────────┬─────────────┬───────────┘
       │             │             │
       ▼             ▼             ▼
┌──────────┐  ┌──────────┐  ┌──────────┐
│  Tempo   │  │   Loki   │  │Prometheus│
│ (traces) │  │  (logs)  │  │(metrics) │
└────┬─────┘  └────┬─────┘  └────┬─────┘
     │             │             │
     └─────────────┼─────────────┘
                   ▼
            ┌──────────┐
            │ Grafana  │
            └──────────┘
```

## Tech Stack

| Component | Purpose |
|-----------|---------|
| **.NET 9** | Application runtime |
| **OpenTelemetry** | Telemetry signals collection (traces, logs, metrics) |
| **Grafana** | Visualization and dashboards |
| **Loki** | Log aggregation |
| **Tempo** | Distributed tracing |
| **Prometheus** | Metrics storage |
| **Docker** | Containerization |

## Applications

| Service | Description |
|---------|-------------|
| **Camera Gateway** | Entry point service that processes camera events |
| **Alert Service** | Receives and processes alerts from Camera Gateway |

## Prerequisites

- [Docker](https://www.docker.com/) and Docker Compose
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (for local development)

## Getting Started

1. Clone the repository:
   ```bash
   git clone <repository-url>
   cd otel-collector
   ```

2. Start the infrastructure:
   ```bash
   docker-compose up -d
   ```

3. Access the services:

   | Service | URL |
   |---------|-----|
   | Grafana | http://localhost:3000 |
   | Prometheus | http://localhost:9090 |
   | Loki | http://localhost:3100 |
   | Tempo | http://localhost:3200 |
   | Camera Gateway | http://localhost:5000 |
   | Alert Service | http://localhost:5001 |

   Default Grafana credentials: `admin` / `admin`

   Make sure all the services are up and running by using the following command:
   ```bash
   docker ps
   ```

4. Shutdown services:
    ```bash
   docker-compose down -v
   ```

## Project Structure

```
├── docker-compose.yml
├── otel-collector-config.yaml
├── grafana/
│   └── provisioning/
├── loki/
│   └── loki-config.yaml
├── tempo/
│   └── tempo-config.yaml
├── prometheus/
│   └── prometheus.yml
└── src/
    ├── CameraGateway/
    └── AlertService/
```

## Observability Features

- **Traces**: Distributed tracing across Camera Gateway → Alert Service calls via Tempo
- **Logs**: Centralized logging with Loki, correlated with trace IDs
- **Metrics**: Application and infrastructure metrics stored in Prometheus

## Configuration

### OpenTelemetry Collector

The collector is configured to receive telemetry data via OTLP and export to the appropriate backends:
- Traces → Tempo
- Logs → Loki
- Metrics → Prometheus (in Prod we will use Mimir with a hybrid approach)

Please refer to the following documentation for more info about the OSS observability stack:
- **OpenTelemetry**: https://opentelemetry.io/docs/
- **Grafana**: https://grafana.com/docs/
- **Tempo**: https://grafana.com/docs/tempo/latest/
- **Loki**: https://grafana.com/docs/loki/latest/
- **Prometheus**: https://prometheus.io/docs/introduction/overview/

We can use this stack without a commercial license :D

## License

This project is for demonstration purposes.