# traffic-events-k8s — every workflow is one target. `make help` lists them.
SHELL := /bin/bash
.DEFAULT_GOAL := help
NS := traffic-events

.PHONY: help tools secrets cluster-up cluster-down build test test-unit test-integration \
        images deploy smoke demo load rollout-demo queues status logs jenkins-up jenkins-down

help: ## List targets
	@grep -E '^[a-z-]+:.*?## ' $(MAKEFILE_LIST) | awk -F':.*?## ' '{printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2}'

tools: ## Check docker, kind, kubectl, dotnet
	@scripts/check-tools.sh

secrets: ## Generate k8s/overlays/dev/secrets.env (random, git-ignored)
	@scripts/make-secrets.sh

cluster-up: tools secrets ## Create the kind cluster + metrics-server
	@scripts/cluster-up.sh

cluster-down: ## Delete the kind cluster (and its volumes)
	kind delete cluster --name traffic-events

build: ## dotnet build (analyzers on, warnings are errors)
	dotnet build -c Release

test: test-unit test-integration ## Unit + integration tests

test-unit: ## Unit tests (no Docker needed)
	dotnet test tests/TrafficEvents.UnitTests -c Release

test-integration: ## Integration tests (Testcontainers: RabbitMQ + MongoDB)
	dotnet test tests/TrafficEvents.IntegrationTests -c Release

images: ## Build all service images (tag = git sha)
	@scripts/build-images.sh

deploy: images ## Build images, load into kind, apply k8s/overlays/dev, wait for rollout
	@scripts/deploy.sh

smoke: ## End-to-end smoke test against the cluster
	@scripts/smoke.sh

demo: ## Send traffic and show status, incidents, alerts and the DLQ
	@scripts/demo.sh

load: ## HPA demo: sustained load, watch processor scale 1 → 3
	@scripts/load-test.sh

rollout-demo: ## Zero-downtime rolling update of query-api, then rollout undo
	@scripts/rollout-demo.sh

queues: ## Queue depths + first DLQ message
	@scripts/queues.sh

status: ## Pods, HPA, resource usage
	@kubectl -n $(NS) get pods,svc,hpa,pdb
	@kubectl -n $(NS) top pods 2>/dev/null || true

logs: ## Tail processor logs (JSON)
	kubectl -n $(NS) logs -l app.kubernetes.io/name=processor -f --max-log-requests 5

jenkins-up: secrets ## Run Jenkins locally (don't run alongside load tests: memory)
	docker compose -f ci/jenkins/docker-compose.yml up -d --build
	@echo "Jenkins: http://localhost:8081  (user admin, password \$${JENKINS_ADMIN_PASSWORD:-admin})"

jenkins-down: ## Stop Jenkins
	docker compose -f ci/jenkins/docker-compose.yml down
