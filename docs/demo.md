# 5-minute live demo

**Before the interview:** `make cluster-up deploy` (takes a few minutes, so do it beforehand). Close memory-hungry
apps: on an 8 GB machine the cluster competes with the browser. Have two terminals open, plus the RabbitMQ UI at
http://localhost:15672 (credentials in `k8s/overlays/dev/secrets.env`).

---

### 0:00 — What's running (30 s)

```bash
kubectl -n traffic-events get pods,svc,hpa
```

> "Three .NET 10 services: ingest-api takes sensor events over HTTP and publishes them to RabbitMQ; the processor
> consumes, de-duplicates, enriches and stores them in MongoDB and raises alerts; query-api serves reads.
> RabbitMQ and MongoDB run in the cluster as StatefulSets with their own volumes. Plain YAML, Kustomize overlay
> for the laptop."

### 0:30 — Happy path and validation (1 min)

```bash
make demo
```

Talk through the output:

1. A hand-written event → `202`. *"202 only after the broker confirmed it: publisher confirms. If RabbitMQ were
   down this would be a 503, never a silent drop."*
2. A bad event → `400` with field errors.
3. 30 s of simulated traffic from a **Job inside the cluster**, with 5 % duplicates, 1 % malformed, 1 % poison.
4. Status, the 15-minute speed aggregate and recent incidents from query-api.
5. Queue depths: alerts raised, and the **DLQ** with its `error` header ("Unknown junction 'J-999'").

> "Duplicates are expected: sensors resend and RabbitMQ is at-least-once. The consumer is idempotent:
> processed_events keyed by eventId, written last, and every write before it is idempotent too."

### 1:30 — Kubernetes: a manifest, line by line (1 min)

Open `k8s/base/ingest-api.yaml`. Point at:
- **Probes**: liveness has no dependency checks (no restart storms); readiness checks RabbitMQ.
- **Requests/limits**: requests are the scheduler's reservation and the HPA's baseline.
- **Rolling update**: `maxSurge 1 / maxUnavailable 0`, plus `preStop sleep`.
- **Secrets**: `$(VAR)` expansion; generated Secret with a hash suffix → secret change = rolling restart.

### 2:30 — Rolling update and rollback (1 min)

```bash
make rollout-demo
```

> "It polls query-api throughout. A new image rolls out one pod at a time: the new pod must be Ready before an
> old one is terminated. Then `kubectl rollout undo` goes back. The failure count should be zero."

### 3:30 — Autoscaling (1 min, start it early)

```bash
make load            # terminal 1: 600 events/s for 3 min, then watches the HPA
make status          # terminal 2: kubectl top pods
```

> "CPU-based HPA on the processor, 1 to 3 replicas. More pods means more competing consumers on one queue, and
> prefetch keeps the work spread. In production I'd scale on queue depth with KEDA: backlog is the real signal,
> CPU is a proxy."

Scale-up usually shows within 30–60 s (metrics-server scrapes every 15 s). Scale-in follows 60 s after load stops.

### 4:30 — Failure on demand (30 s, if time allows)

```bash
kubectl -n traffic-events delete pod -l app.kubernetes.io/name=processor   # during load
make queues
```

> "Unacked messages go back to the queue and another consumer finishes them. Because writes are idempotent, the
> redelivery is harmless. That exact case is an integration test."

Or stop the broker: `kubectl -n traffic-events scale sts rabbitmq --replicas=0`, then POST, and you get `503` until
readiness removes the pod. Scale it back to 1 afterwards.

### Close

> "Jenkins builds, runs unit and Testcontainers integration tests, builds images, loads them into kind, applies
> the overlay and runs a smoke test. All the Jenkins config is code. GitHub Actions runs the same build and tests
> on every push."

---

**If something misbehaves:** `make status`, `kubectl -n traffic-events describe pod <name>` (the Events section
at the bottom), `make logs`.
