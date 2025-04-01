## Install rancher desktop
https://docs.rancherdesktop.io/getting-started/installation
Remember this if you're using ubuntu: `sudo sysctl -w net.ipv4.ip_unprivileged_port_start=80`
Ingress resources will not work without this.

## Build images
build images:
```
docker build -t turboapi-auth:latest -f ./Turboapi-auth/Dockerfile .
docker build -t turboapi-geo:latest -f ./Turboapi-geo/Dockerfile .
docker build -t turboapi-activity:latest -f ./Turboapi-activity/Dockerfile .
```

```
docker build -t turboapi-auth-migration:latest -f auth-migration.Dockerfile .
docker build -t turboapi-geo-migration:latest -f geo-migration.Dockerfile .
docker build -t turboapi-activity-migration:latest -f activity-migration.Dockerfile .
```

Install the monitoring stack:
```
helm install prometheus prometheus-community/kube-prometheus-stack \
  -f prometheus-values.yaml \
  --namespace monitoring \
  --create-namespace
```

## Apply Kubernetes resources
`kubectl apply -f kubernetes-resources.yaml`