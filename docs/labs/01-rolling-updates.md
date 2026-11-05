# Lab 01 · Rolling updates

**Scenario.** You are on call for the ordering shop. The team ships `order-service` three times today:

| Release | What's in it | What should happen |
|---|---|---|
| **v2** | A normal release | Customers notice nothing (zero downtime) |
| **v3** | Someone typed the image tag wrong | The rollout gets stuck, the old pods keep serving, you roll back |
| **v4** | A bad config (wrong database name) | New pods start but never become ready, the old pods keep serving, you roll back |

Then you change *how* the rollout happens (surge, unavailable, Recreate) and see the difference in the traffic.

**Cluster:** `kind-lab` (1 control plane + 2 workers, from `kind/cluster-3node.yaml`), namespace `ordering`. All commands are PowerShell, run from the repo root.

> **Quoting in `patch` commands:** the JSON is written as `'{\"spec\":…}'`, which is what **Windows PowerShell 5.1** needs. In **PowerShell 7.3+** remove the backslashes: `'{"spec":…}'`. Check your version with `$PSVersionTable.PSVersion`.

---

## 0 · Set up three terminals

| Terminal | Runs | Purpose |
|---|---|---|
| **A** | your commands | Releases, rollbacks |
| **B** | `kubectl -n ordering get pods -l app=order-service -w` | Watch pods appear and disappear |
| **C** | the traffic generator (step 3) | Proves whether customers saw errors |

Optional fourth terminal: `kubectl -n ordering get rs -l app=order-service -w` (watch ReplicaSets).

---

## 1 · Baseline: see what a Deployment really is

```powershell
kubectl -n ordering scale deploy/order-service --replicas=3
kubectl -n ordering get deploy,rs,pods -l app=order-service
kubectl -n ordering get deploy order-service -o jsonpath="{.spec.strategy}"; ""
```

**Questions to answer:**
1. How many ReplicaSets exist? Which one owns the 3 pods? (`kubectl -n ordering describe pod <pod> | Select-String "Controlled By"`)
2. What is the strategy? (Default: `RollingUpdate` with `maxSurge: 25%`, `maxUnavailable: 25%`.)
3. With 3 replicas, what do 25% / 25% become? (surge rounds **up** → 1 extra pod; unavailable rounds **down** → 0 pods may be missing. So: add 1 new, wait until it is Ready, remove 1 old, repeat.)

---

## 2 · Build v1 and v2 images and load them into kind

v1 and v2 are the same app with a different label. That's enough to give each one its own image ID, so Kubernetes treats them as different versions.

```powershell
docker pull waseembit/order-service:latest

"FROM waseembit/order-service:latest`nLABEL app.version=v1" | docker build -t waseembit/order-service:v1 -
"FROM waseembit/order-service:latest`nLABEL app.version=v2" | docker build -t waseembit/order-service:v2 -

kind load docker-image waseembit/order-service:v1 waseembit/order-service:v2 --name lab
```

**Why `kind load`:** the nodes are containers with their own image store (containerd). They can't see images on your laptop until you load them.

**Watch out for `imagePullPolicy`.** The Deployment was created with `:latest`, so Kubernetes filled in `imagePullPolicy: Always` **at creation time** and stored it. `set image` changes only the tag, not the stored policy. With `Always`, the node ignores the loaded `v1` and asks Docker Hub, where `v1` doesn't exist, and you get `ErrImagePull`. Switch the policy once before step 4:

```powershell
kubectl -n ordering patch deploy/order-service -p '{\"spec\":{\"template\":{\"spec\":{\"containers\":[{\"name\":\"order-service\",\"imagePullPolicy\":\"IfNotPresent\"}]}}}}'
```

Check that both workers have them (the scheduler may put a pod on either): `docker exec lab-worker crictl images | Select-String order-service` and the same for `lab-worker2`

---

## 3 · Start the traffic generator (terminal C)

The traffic must come **from inside the cluster**. `kubectl port-forward` connects to a single pod and breaks when that pod is deleted, so it would show failures that real users never get.

```powershell
kubectl -n ordering run traffic --image=curlimages/curl --restart=Never -it --rm -- sh -c 'while true; do echo $(date +%T) $(curl -s -o /dev/null -m 2 -w %{http_code} http://api-gateway:8080/api/orders); sleep 0.3; done'
```
(The command has no double quotes inside on purpose: Windows PowerShell 5.1 mangles them when it passes arguments to kubectl.)

You should see a stream of `200`s. **Any line that isn't `200` (`000`, `502` or `503`) is a request that failed for a customer.** Leave this running for the whole lab.

---

## 4 · Release v1 (gives the history a known starting point)

```powershell
kubectl -n ordering set image deploy/order-service order-service=waseembit/order-service:v1
kubectl -n ordering annotate deploy/order-service kubernetes.io/change-cause="release v1" --overwrite
kubectl -n ordering rollout status deploy/order-service
```

`order-service=` is the **container name** inside the pod, not the Deployment name. They happen to match here.

---

## 5 · Release v2: a normal rolling update

```powershell
kubectl -n ordering set image deploy/order-service order-service=waseembit/order-service:v2
kubectl -n ordering annotate deploy/order-service kubernetes.io/change-cause="release v2" --overwrite
kubectl -n ordering rollout status deploy/order-service
```

**Watch:**
- **Terminal B:** a new pod appears (`0/1` → `1/1`), *then* one old pod becomes `Terminating`, three times over.
- **Terminal C:** still all `200`.
- `kubectl -n ordering get rs -l app=order-service`: the old ReplicaSet scales 3→0 and the new one 0→3. **The old ReplicaSet is not deleted.** It's kept at 0 so you can roll back.

**Check it:**
```powershell
kubectl -n ordering get pods -l app=order-service -o custom-columns="POD:.metadata.name,IMAGE:.spec.containers[0].image,READY:.status.containerStatuses[0].ready"
kubectl -n ordering rollout history deploy/order-service
kubectl -n ordering rollout history deploy/order-service --revision=3     # template of one revision
```

**Questions:**
1. What decides when an old pod may be deleted? (The new pod's **readiness probe**: `/health/ready`.)
2. Why do the pod names change, including the part in the middle? (The middle part is the **pod-template-hash**, which is different for each ReplicaSet.)

---

## 6 · Release v3: wrong image tag

```powershell
kubectl -n ordering patch deploy/order-service -p '{\"spec\":{\"progressDeadlineSeconds\":60}}'
kubectl -n ordering set image deploy/order-service order-service=waseembit/order-service:v3-typo
kubectl -n ordering annotate deploy/order-service kubernetes.io/change-cause="release v3 (bad tag)" --overwrite
kubectl -n ordering rollout status deploy/order-service
```

**Watch:**
- **Terminal B:** one new pod is stuck in `ErrImagePull` / `ImagePullBackOff`. **No old pod is removed**, because the new one never became Ready.
- **Terminal C:** still `200`. The broken release never received traffic.
- After about 60 s, `rollout status` exits with `ProgressDeadlineExceeded`. A CD pipeline uses this to mark the deploy as failed.

**Diagnose, then roll back:**
```powershell
kubectl -n ordering describe pod -l app=order-service | Select-String "Failed|Back-off" | Select-Object -Last 3
kubectl -n ordering get deploy order-service -o jsonpath="{.status.conditions[?(@.type=='Progressing')].reason}"; ""
kubectl -n ordering rollout undo deploy/order-service
kubectl -n ordering rollout status deploy/order-service
```

**Questions:**
1. Why did Kubernetes **not** roll back by itself? (Deployments never roll back on their own. `progressDeadlineSeconds` only *reports* the failure. Rolling back is your decision or your pipeline's.)
2. After `undo`, what happened to the revision numbers in `rollout history`? (The v2 template comes back as a **new** revision number.)

---

## 7 · Release v4: config that breaks readiness

The image is fine, but the database credentials are wrong:

```powershell
kubectl -n ordering set env deploy/order-service ConnectionStrings__OrderDb="Host=postgres;Port=5432;Database=orderdb;Username=x;Password=y"
kubectl -n ordering annotate deploy/order-service kubernetes.io/change-cause="release v4 (bad config)" --overwrite
```

(Wrong *credentials*, not a wrong database name: with valid credentials, `EnsureCreated()` would simply create the missing database and the release would "succeed".)

**What the app does at startup** (`Program.cs` → `InitializeDatabaseAsync`): before the web server starts, it tries the database 15 times, 3 s apart. If every attempt fails, it throws and the process exits.

**Watch:**
- **Terminal B:** the new pod goes `Running 0/1` for about a minute, then `Error` → `CrashLoopBackOff`, and RESTARTS keeps going up. The waits between restarts get longer (10 s, 20 s, 40 s … up to 5 min).
- **Terminal C:** still `200`. The broken pod never became Ready, so no old pod was removed.

**Diagnose:**
```powershell
kubectl -n ordering logs <new-pod>              # current attempt: "Database not ready (attempt n/15)…"
kubectl -n ordering logs <new-pod> --previous   # the run that crashed: the final exception
kubectl -n ordering describe pod <new-pod> | Select-String "Startup probe|Back-off|Last State|Exit Code"
```

**Questions:**
1. Why do you need `--previous`? (After a restart, `logs` shows the *new* container. The crash reason is in the previous one.)
2. During the first minute, why does `describe` show `Startup probe failed: connection refused`? (The web server hasn't started yet. It's still stuck in the database retry loop.)
3. Restarting doesn't fix wrong credentials. Why is the rollout still safe? (Same as v3: the pod never becomes Ready, so `maxUnavailable: 0` keeps all old pods. **The rollout is only as safe as your readiness check.** If the app had started without a working database *and* readiness only checked `/health/live`, the broken pods would have counted as Ready and taken over.)

**Roll back to a specific revision:**
```powershell
kubectl -n ordering rollout history deploy/order-service
kubectl -n ordering rollout undo deploy/order-service --to-revision=<the "release v2" number>
```

---

## 8 · Tune the strategy

Run each one, release v1 ↔ v2 with `set image`, and watch terminals B and C.

| Experiment | Command | What you'll see |
|---|---|---|
| **No extra capacity** | `kubectl -n ordering patch deploy/order-service -p '{\"spec\":{\"strategy\":{\"rollingUpdate\":{\"maxSurge\":0,\"maxUnavailable\":1}}}}'` | An old pod is killed **first**, so only 2 serve during the update. Cheaper, less safe. |
| **Fast, more capacity** | `... '{\"spec\":{\"strategy\":{\"rollingUpdate\":{\"maxSurge\":3,\"maxUnavailable\":0}}}}'` | 3 new pods start at once (6 total briefly), then the old ones go. Close to blue/green. |
| **Slow it down** | `kubectl -n ordering patch deploy/order-service -p '{\"spec\":{\"minReadySeconds\":15}}'` | Each new pod must stay Ready for 15 s before the next step. Catches apps that crash shortly after starting. |
| **Recreate** | `kubectl -n ordering patch deploy/order-service -p '{\"spec\":{\"strategy\":{\"type\":\"Recreate\",\"rollingUpdate\":null}}}'` | **All** old pods stop, then the new ones start. Terminal C shows failures (`502`/`000`). This is what "downtime" looks like. |

`Recreate` is used when two versions **must not** run at the same time, for example a database schema change that the old version can't handle.

---

## 9 · Batch several changes into one rollout

Each `set image` or `set env` starts its own rollout. To make several changes in a single rollout:

```powershell
kubectl -n ordering rollout pause deploy/order-service
kubectl -n ordering set image deploy/order-service order-service=waseembit/order-service:v1
kubectl -n ordering set resources deploy/order-service -c order-service --limits=memory=300Mi
kubectl -n ordering rollout resume deploy/order-service      # one rollout with both changes
```

---

## 10 · Restart without changing anything

```powershell
kubectl -n ordering rollout restart deploy/order-service
```

This adds a timestamp annotation to the pod template, which creates a new ReplicaSet and a normal rolling update. Use it to pick up a changed ConfigMap, since `envFrom` values are read only when a pod starts.

---

## 11 · Do it the declarative way (how real deploys work)

Everything above changed the cluster directly. Run `kubectl diff -k K8s/` now and you'll see that **Git and the cluster disagree**: your image tag, env vars, strategy and replica count. The next `kubectl apply -k K8s/` would undo all of it.

The real release flow changes the file, not the cluster:
```powershell
# K8s/kustomization.yaml → images: order-service newTag: v2   (edit it, or:)
cd K8s; kustomize edit set image order-service=waseembit/order-service:v2; cd ..   # needs the standalone kustomize CLI
kubectl diff -k K8s/
kubectl apply -k K8s/
```
That's the step the CD pipeline (and later Argo CD) automates.

---

## 12 · Bonus: if you see a few errors during a *normal* rollout

When a pod is deleted, two things happen at the same time: the pod gets SIGTERM, and it's removed from the Service endpoints. If the app stops before every proxy has stopped sending to it, a few requests fail. The standard fix is a short pause before shutdown:

```yaml
# in the container spec
lifecycle:
  preStop:
    sleep: { seconds: 5 }     # built-in sleep action (no shell needed in the image)
terminationGracePeriodSeconds: 30   # pod spec level
```

Try a rollout with and without it while terminal C runs.

---

## 13 · Clean up

```powershell
# Ctrl+C in terminal C (the traffic pod deletes itself because of --rm)
kubectl apply -k K8s/                                       # back to what Git says (image :latest, original env, default strategy)
kubectl -n ordering patch deploy/order-service -p '{\"spec\":{\"progressDeadlineSeconds\":600,\"minReadySeconds\":0}}'
kubectl -n ordering scale deploy/order-service --replicas=1
kubectl diff -k K8s/                                        # should print nothing
```

`apply -k` leaves alone the fields your files don't set (like `progressDeadlineSeconds`), which is why the patch is needed.

---

## You've finished the lab when you can explain…

- [ ] Deployment → ReplicaSet → Pod, and why old ReplicaSets are kept at 0
- [ ] How `maxSurge` / `maxUnavailable` turn into actual pod counts
- [ ] Why readiness (not liveness) is what makes a rollout safe
- [ ] Why a bad release didn't cause an outage, and why it didn't roll itself back
- [ ] `rollout status / history / undo / pause / resume / restart`
- [ ] Why `kubectl set image` is fine for practice but not for real releases
