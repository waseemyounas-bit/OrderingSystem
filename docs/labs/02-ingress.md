# Lab 02: Ingress

Cluster: `kind-lab` (1 control plane, 2 workers, `localhost:8000 → control-plane:80`).
Manifests for this lab: [docs/labs/ingress/](ingress/). They are **not** part of `K8s/kustomization.yaml`.

---

## The mental model (read this first)

An Ingress setup has **three separate things**. Most misconceptions come from mixing them up.

| Thing | What it is | Who creates it |
|---|---|---|
| **Ingress** (resource) | A YAML set of *rules*: "host X + path Y → Service Z". Nothing more. | You (`K8s/edge/ingress.yaml`) |
| **IngressClass** | A name (`nginx`) that says which controller owns an Ingress | The controller install |
| **Ingress controller** | A real program (here nginx in a pod) that *watches* Ingress objects and reconfigures itself | You install it (or the cloud provides it) |

The request path in your kind cluster:

```
browser http://localhost:8000/
   │  Docker Desktop port publish (set at cluster creation, extraPortMappings)
   ▼
lab-control-plane container :80
   │  hostPort 80 on the controller pod
   ▼
ingress-nginx-controller pod (nginx)          ← reads ALL Ingress objects with class "nginx"
   │  matches host + path → picks Service web-client
   │  looks up its EndpointSlice → pod IPs
   ▼
web-client POD IP:8080 (on lab-worker / lab-worker2)   ← goes to the pod directly, not the ClusterIP
   │  web-client's own nginx: /api/* → api-gateway:8080
   ▼
api-gateway → order/product/payment services
```

## Misconceptions this lab will break

| # | Misconception | Reality | Proved in |
|---|---|---|---|
| 1 | "Creating an Ingress exposes my app." | An Ingress with no controller does nothing. `ADDRESS` stays empty. | Step 0 |
| 2 | "Installing the controller is enough." | Traffic must physically reach the controller pod. On kind that means the node with the port mapping. | Step 1 |
| 3 | "Ingress sends traffic to the Service's ClusterIP." | ingress-nginx uses the Service only to *find pod IPs*, then proxies to pods directly. | Step 3 |
| 4 | "Ingress replaces Services." | The backend **must** be a Service. No Service, no route. | Steps 3, 7 |
| 5 | "Each app needs its own load balancer / port." | One controller, one port, many apps, split by **path** or **host**. | Steps 4, 5 |
| 6 | "`/api` rule strips `/api`." | Paths are passed through unchanged unless you rewrite. Rewrites are controller-specific annotations. | Step 6 |
| 7 | "Ingress can expose Postgres/RabbitMQ." | Ingress is HTTP/HTTPS only (L7). TCP needs a LoadBalancer Service or Gateway API `TCPRoute`. | Discussion |
| 8 | "An Ingress can point at any namespace." | Backend Service must be in the **same namespace** as the Ingress. | Step 7 |
| 9 | "Ingress YAML is portable across clouds." | The spec is; **annotations are not**. On GKE the controller is Google's HTTP(S) Load Balancer, not a pod. | Step 9 |

> **Note:** ingress-nginx (the project) was archived in March 2026: no more releases or security fixes.
> It's still the best-documented controller for *learning* Ingress, and the **Ingress API itself is stable**.
> The successor is **Gateway API**, which is the next lab.

---

## Step 0: Before any controller (prediction)

`apply -k` already created the Ingress `ordering`. Without a controller:

```powershell
kubectl get ingressclass
kubectl -n ordering get ingress
kubectl -n ordering describe ingress ordering
curl.exe -i http://localhost:8000/
```

**Predict:** What's in `CLASS` and `ADDRESS`? What does curl return, and *who* answers?

## Step 1: Install the controller and get it on the right node

```powershell
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.15.1/deploy/static/provider/kind/deploy.yaml
kubectl -n ingress-nginx get pods -o wide -w
```

Look at the `NODE` column. This manifest tolerates the control-plane taint but has **no nodeSelector**, so the pod can land on a worker.

**Predict:** If it's on `lab-worker`, does `http://localhost:8000` work? Why or why not? (Hint: look at `kind/cluster-3node.yaml`. Which node has `extraPortMappings`?)

Pin it to the control plane (the patch is a file, so there's no PowerShell quoting trouble):

```powershell
kubectl -n ingress-nginx patch deploy ingress-nginx-controller --patch-file docs/labs/ingress/01-controller-on-control-plane.yaml
kubectl -n ingress-nginx rollout status deploy/ingress-nginx-controller
kubectl -n ingress-nginx get pods -o wide
```

Then rerun Step 0's commands. What changed in `ADDRESS`?

## Step 2: Use the app through Ingress

- Open `http://localhost:8000` in the browser. No port-forward is needed.
- Create an order in the UI, then:

```powershell
curl.exe -s http://localhost:8000/api/orders
kubectl -n ingress-nginx logs deploy/ingress-nginx-controller --tail=10
```

In the log lines, find the **upstream address** (`ip:port` near the end). Whose IP is it?

## Step 3: Ingress goes to pods, not the ClusterIP (misconceptions 3 and 4)

```powershell
kubectl -n ordering get svc web-client
kubectl -n ordering get endpointslices -l kubernetes.io/service-name=web-client
kubectl -n ordering get pods -l app=web-client -o wide
```

Compare these with the upstream IP from Step 2: is it the Service's `CLUSTER-IP` or a pod IP?

Now remove all the backends:

```powershell
kubectl -n ordering scale deploy/web-client --replicas=0
curl.exe -i http://localhost:8000/
kubectl -n ordering scale deploy/web-client --replicas=1
```

**Predict the status code** before you curl. Who generated that error page: your app or the controller?

## Step 4: Path routing (misconception 5)

```powershell
kubectl apply -f docs/labs/ingress/02-path-routing.yaml
kubectl -n ordering get ingress
```

There are now two Ingresses for the same (empty) host: `/` → web-client and `/api` → api-gateway. The controller **merges** them, and the **longest matching path wins**.

| Request | Which backend? (predict) |
|---|---|
| `/` | |
| `/api/orders` | |
| `/apix` | |
| `/API/orders` | |

Check by watching which pod logs the request, or the controller log's upstream IP:

```powershell
curl.exe -s -o NUL -w "%{http_code}`n" http://localhost:8000/apix
kubectl -n ingress-nginx logs deploy/ingress-nginx-controller --tail=3
```

`Prefix` matches whole path *segments* (`/api` matches `/api/x`, not `/apix`), and it's case-sensitive.
The app still worked before this step because web-client's nginx forwarded `/api`. Now the Ingress does it instead. Both are valid designs.

## Step 5: Host routing

```powershell
kubectl apply -f docs/labs/ingress/03-host-routing.yaml
curl.exe -s -H "Host: gateway.localhost" http://localhost:8000/api/orders
curl.exe -s -H "Host: app.localhost"     http://localhost:8000/ | Select-Object -First 5
curl.exe -s -H "Host: whatever.com"      http://localhost:8000/ | Select-Object -First 5
```

In a browser, open `http://app.localhost:8000` (`*.localhost` already points to 127.0.0.1).

**Predict the 3rd curl:** `whatever.com` has no rule. Why does it still get the Angular app? (Hint: the rule in `K8s/edge/ingress.yaml` has no `host:`.)

## Step 6: Rewrites are an annotation, not a feature of Ingress (misconception 6)

```powershell
kubectl apply -f docs/labs/ingress/04-rewrite.yaml
curl.exe -i http://localhost:8000/gw/api/orders
```

The browser asks for `/gw/api/orders`, and the gateway receives `/api/orders`. Compare with `kubectl -n ordering logs deploy/api-gateway --tail=5`.

Then **remove the `rewrite-target` annotation**, apply again and curl again. What status code do you get now, and why?

## Step 7: Failure drills

```powershell
kubectl apply -f docs/labs/ingress/05-broken.yaml
```

For each one, **predict first**, then check it:

| Host | Bug | Your prediction | `curl.exe -i -H "Host: ..." http://localhost:8000/` | What `describe ingress` / controller logs say |
|---|---|---|---|---|
| `noclass.localhost` | no `ingressClassName` | | | |
| `typo.localhost` | Service `web-clinet` | | | |
| `ns.localhost` | Ingress in `default`, Service in `ordering` | | | |

Useful commands:

```powershell
kubectl get ingress -A
kubectl -n ordering describe ingress broken-svc
kubectl -n ingress-nginx logs deploy/ingress-nginx-controller --tail=30 | Select-String -Pattern "broken|error|warn"
```

Note: a wrong Service name is *accepted* by the API server. Ingress validation doesn't check that the backend exists.

## Step 8 (optional): TLS on localhost:8443

```powershell
& "C:\Program Files\Git\usr\bin\openssl.exe" req -x509 -nodes -newkey rsa:2048 -days 30 -keyout tls.key -out tls.crt -subj "/CN=app.localhost"
kubectl -n ordering create secret tls app-localhost-tls --cert=tls.crt --key=tls.key
Remove-Item tls.key, tls.crt
```

Add this under `spec:` in `03-host-routing.yaml`, then apply it:

```yaml
  tls:
    - hosts: [app.localhost]
      secretName: app-localhost-tls
```

```powershell
curl.exe -vk https://app.localhost:8443/ 2>&1 | Select-String "subject|HTTP/"
```

The **controller** terminates TLS. Your pods still get plain HTTP. The cert Secret must be in the Ingress's namespace.

## Step 9: How this maps to GKE (discussion, no commands)

| | kind (this lab) | GKE |
|---|---|---|
| Controller | ingress-nginx pod you installed | Google-managed. The Ingress becomes a **Google Cloud HTTP(S) Load Balancer** (nothing runs in your cluster) |
| `ingressClassName` | `nginx` | `gce` (external) / `gce-internal` |
| How traffic reaches you | localhost:8000 → Docker → hostPort | Public IP on Google's edge → pod IPs directly (container-native LB, NEGs) |
| Health checks | Readiness probes only | **The LB does its own health checks too** (a common "502 on GKE" cause) |
| TLS | Secret | Secret, or a Google-managed certificate |
| Rewrite annotation | works | ignored (not supported) |
| Recommended going forward | Gateway API | Gateway API (`gke-l7-global-external-managed`) |

## Step 10: Cleanup

```powershell
kubectl delete -f docs/labs/ingress/05-broken.yaml
kubectl delete -f docs/labs/ingress/04-rewrite.yaml -f docs/labs/ingress/03-host-routing.yaml -f docs/labs/ingress/02-path-routing.yaml
kubectl -n ordering delete secret app-localhost-tls --ignore-not-found
```

Keep the controller. `K8s/edge/ingress.yaml` keeps working at `http://localhost:8000`.

## Checklist (answer without notes)

- [ ] What happens to an Ingress object when no controller is installed?
- [ ] Why did the controller have to run on `lab-control-plane`?
- [ ] Does ingress-nginx send traffic to the ClusterIP or to the pod IPs? How did you prove it?
- [ ] Two Ingresses define `/` and `/api` for the same host. Which one handles `/api/orders`? `/apix`?
- [ ] Why can't you expose Postgres with an Ingress?
- [ ] Would your rewrite annotation work on GKE? Why or why not?
- [ ] Which three things were wrong in the failure drills, and what was each symptom?
