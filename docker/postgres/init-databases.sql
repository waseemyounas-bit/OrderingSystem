-- DATABASE PER SERVICE on one PostgreSQL server (cheap for local dev).
-- Each service only ever connects to its own database. In production / Kubernetes
-- they could just as well live on completely separate servers - no code change needed.
CREATE DATABASE productdb;
CREATE DATABASE orderdb;
CREATE DATABASE paymentdb;
