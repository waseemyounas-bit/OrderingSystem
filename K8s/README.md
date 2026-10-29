# Kubernetes manifests

Deploys the whole Ordering System into the `ordering` namespace with one command, using Kustomize (built into `kubectl`).

```
K8s/
├── kustomization.yaml        # entry point: lists everything, sets namespace, images, Secret
├── namespace.yaml
├── config/
│   ├── app-config.yaml       # ConfigMap: non-secret shared settings
│   └── secrets.env.example   # copy to secrets.env (git-ignored) -> Secret "app-secrets"
├── infrastructure/           # stateful backing services
│   ├── postgres.yaml         # StatefulSet + headless Service + init SQL
│   ├── rabbitmq.yaml         # StatefulSet + Service
│   └── seq.yaml              # Deployment + PVC + Service
├── services/                 # stateless business services
│   ├── product-service.yaml  # Deployment + Service (most detailed comments)
│   ├── order-service.yaml
│   └── payment-service.yaml
└── edge/                     # what the outside world reaches
    ├── api-gateway.yaml
    ├── web-client.yaml
    └── ingress.yaml
```

## docker-compose → Kubernetes

| docker-compose | Kubernetes | Where |
|---|---|---|
| `services:` entry (stateless) | **Deployment** + **Service** | services/, edge/ |
| `services:` entry with data (DB, broker) | **StatefulSet** + **PersistentVolumeClaim** | infrastructure/ |
| service name as DNS (`postgres`) | **Service** name as DNS (`postgres`) | same names → same connection strings |
| `environment:` (plain) | **ConfigMap** | config/app-config.yaml |
| `.env` passwords | **Secret** | kustomization.yaml `secretGenerator` |
| `healthcheck:` | **startup / liveness / readiness probes** | every container |
| `volumes:` | **PersistentVolumeClaim** | infrastructure/ |
| `ports: "4200:8080"` | **Ingress** (or `kubectl port-forward`) | edge/ingress.yaml |
| `depends_on:` | *(none)* - apps retry + readiness probes | Kubernetes starts everything in parallel |
| `restart: unless-stopped` | built in: the Deployment controller recreates dead pods | |

## Request flow

```
browser ──► Ingress (ingress-nginx) ──► web-client Service ──► nginx pod
                                                         │ /api/*
                                                         ▼
                                             api-gateway Service ──► YARP pod
                                                         │
                     ┌───────────────────────────────────┼─────────────────────┐
                     ▼                                   ▼                     ▼
          product-service Service           order-service Service    payment-service Service
                     │   ▲ HTTP (circuit breaker)        │                     │
                     │   └───────────────────────────────┘                     │
                     └──────────── postgres / rabbitmq / seq Services ─────────┘
```

Only the Ingress is reachable from outside. Every other Service is `ClusterIP` (internal).

## Run it locally

**1. A cluster.** Use one of the following:
- Docker Desktop → Settings → Kubernetes → *Enable*. Simplest on Windows; give Docker ~6 GB RAM.
- `kind create cluster` or `minikube start --memory 6g`.

Check it with `kubectl get nodes`. The node should be `Ready`.

**2. Secrets.** Run this once:
```bash
cp K8s/config/secrets.env.example K8s/config/secrets.env    # then edit the passwords
```

**3. Images.** The manifests pull `waseembit/<service>:latest` from Docker Hub, which the CI pipeline pushes.

**4. Deploy:**
```bash
kubectl apply -k K8s/
kubectl get pods -n ordering -w        # watch until all are 1/1 Running (Ctrl+C)
```
Startup order doesn't matter. Services retry Postgres and RabbitMQ, and the `startupProbe` gives them up to 90s.

**5. Open it.** Either option works:
- **Port-forward** (no extra install):
  ```bash
  kubectl port-forward -n ordering svc/web-client 4200:8080    # http://localhost:4200
  kubectl port-forward -n ordering svc/rabbitmq 15672          # http://localhost:15672
  kubectl port-forward -n ordering svc/seq 8081:80             # http://localhost:8081
  ```
- **Ingress.** Install the ingress-nginx controller once, then open http://localhost:
  ```bash
  kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.12.1/deploy/static/provider/cloud/deploy.yaml
  ```
  The ingress-nginx project has been retired upstream. It still works for learning; the modern replacement is the **Gateway API**.

## Everyday commands

```bash
kubectl get all -n ordering                                  # everything at a glance
kubectl describe pod <pod> -n ordering                       # Events: why is it Pending / CrashLoopBackOff?
kubectl logs deploy/order-service -n ordering -f             # follow logs (or use Seq)
kubectl exec -it postgres-0 -n ordering -- psql -U postgres  # shell into the DB

kubectl scale deploy/product-service --replicas=3 -n ordering        # scale out
kubectl rollout restart deploy/order-service -n ordering             # restart pods one by one
kubectl rollout status  deploy/order-service -n ordering             # wait for a rollout
kubectl rollout undo    deploy/order-service -n ordering             # roll back to the previous version

kubectl kustomize K8s/                     # preview the final YAML (images, hashed Secret name)
```

### Experiments that show the concepts

| Try | Watch |
|---|---|
| `kubectl delete pod -l app=payment-service -n ordering` | A new pod appears immediately (self-healing). |
| `kubectl scale statefulset/postgres --replicas=0 -n ordering` | Service pods go `0/1` (readiness fails) but are **not** restarted (liveness only checks the process). Scale back to 1 and they recover. |
| `kubectl scale deploy/product-service --replicas=0 -n ordering`, then place an order | The circuit breaker in order-service opens (see Seq). |
| Change a password in `secrets.env` and re-run `apply -k` | A new hashed Secret is created and the pods roll automatically. |

## Clean up

```bash
kubectl delete -k K8s/                                 # deletes the namespace and everything in it
```
Deleting the namespace also removes the StatefulSet PVCs, so the data is gone. To keep the data, delete only the workloads instead of the namespace.

## Not production-ready yet (on purpose)

- **Database and broker inside the cluster.** Production uses a managed Postgres (Azure Database for PostgreSQL) and a RabbitMQ operator or managed broker.
- **Secrets in a file.** Production uses Sealed Secrets, External Secrets with Azure Key Vault, or the CD pipeline creating the Secret.
- **No HorizontalPodAutoscaler, PodDisruptionBudget, NetworkPolicy, or TLS on the Ingress.** These are good next steps.
- **`EnsureCreated()` at startup.** With migrations, use a Kubernetes **Job** that runs before the rollout.

## Next: CD

The CD pipeline will:
1. Pin the images to the commit just pushed by CI:
   `kustomize edit set image product-service=waseembit/product-service:sha-<sha>`.
2. Create `config/secrets.env` from GitHub Secrets.
3. Run `kubectl apply -k K8s/` against the cluster, then `kubectl rollout status` for every Deployment.
