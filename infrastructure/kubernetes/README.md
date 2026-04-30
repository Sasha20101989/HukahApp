# Kubernetes

Base Kubernetes manifests for the platform.

Apply locally:

```bash
kubectl apply -f infrastructure/kubernetes/namespace.yaml
kubectl apply -f infrastructure/kubernetes/configmap.yaml
kubectl apply -f infrastructure/kubernetes/
```

The manifests are intentionally minimal and image names use the `hookah/*:latest` convention from Docker Compose. Add image tags, secrets, ingress TLS and persistent storage per environment.
