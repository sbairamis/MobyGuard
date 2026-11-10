<p align="center">
  <img src="web/public/logo.png" width="120" alt="MobyGuard Logo" style="border-radius: 24px;" />
</p>

<h1 align="center">MobyGuard</h1>

<p align="center">
  <b>Lightweight, self-hosted Docker and OCI Registry Control Plane.</b><br />
  Fine-grained repository access control, ECDSA token authentication, and management dashboard for Docker Distribution.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/React-19-61DAFB?logo=react&logoColor=black" alt="React 19" />
  <img src="https://img.shields.io/badge/Docker-Registry_v2%2Fv3-2496ED?logo=docker&logoColor=white" alt="Docker Registry" />
  <img src="https://img.shields.io/badge/PostgreSQL-17-4169E1?logo=postgresql&logoColor=white" alt="PostgreSQL" />
  <img src="https://img.shields.io/badge/Redis-8-DC382D?logo=redis&logoColor=white" alt="Redis 8" />
  <img src="https://img.shields.io/badge/License-MIT-green.svg" alt="License" />
</p>

---

## Why MobyGuard?

Organizations deploying private Docker registries typically face two limitations:

* **Docker Distribution (`registry:2`) Limitations**:
  * Relies on basic `htpasswd` authentication, granting all-or-nothing access to all images.
  * Lacks repository-level authorization and cannot restrict user permissions by namespace.
  * Does not provide a web management interface for browsing tags, inspecting manifests, or deleting images.

* **Enterprise Registry Suites (e.g., Harbor) Overhead**:
  * Requires substantial server resources (6 to 8 containers, 4GB to 8GB+ RAM).
  * Involves complex deployment charts and operational maintenance overhead.

**MobyGuard provides a focused solution**: A lightweight, single-appliance control plane (~80MB RAM) that adds native Docker Registry v2 token authentication, fine-grained access control lists, and an administrative dashboard directly on top of vanilla Docker Distribution.

---

## Features

* **Docker v2 and OCI Token Authentication**:
  * Fully compliant with the [Docker Registry v2 Bearer Token Specification](https://distribution.github.io/distribution/spec/auth/token/).
  * Compatible with standard `docker login`, `docker pull`, and `docker push` workflows.
  * Asymmetric ECDSA P-256 (`ES256`) token signing with a public JWKS endpoint (`/api/Token/jwks`).

* **Repository Access Control (ACL)**:
  * Per-user policy scoping supporting exact matches (`team-a/backend`), namespace wildcards (`team-a/*`), or global access (`*`).
  * Granular action control with independent toggles for **Pull**, **Push**, and **Delete** capabilities.

* **Web Management Dashboard**:
  * Catalog exploration with RFC 5988 pagination supporting large-scale registries (10,000+ repositories).
  * OCI and Docker v2 manifest inspection, including media types, digest checksums, layer sizes, and container configurations.
  * Image tag deletion workflows directly from the interface.

* **User and Service Account Management**:
  * User account provisioning with role assignments (`Admin`, `User`).
  * Dedicated headless accounts (`Service`) designed for CI/CD automation pipelines (GitHub Actions, GitLab CI, Drone).

* **Security Architecture**:
  * High-iteration `PBKDF2-HMAC-SHA512` password hashing with unique per-user cryptographic salts.
  * 15-minute JWT lifetime with Redis-backed refresh token rotation and instant user-level revocation.
  * Rate-limiting and brute-force lockout protection via Redis.
  * Hardened frontend delivery with sourcemap generation disabled in production builds.

* **Audit Logging**:
  * Structured audit trail recording token issuances, image deletions, user creations, and policy changes.

---

## Architecture

```
                             ┌───────────────────────────────┐
                             │      Docker CLI / Kaniko      │
                             │  (docker push / docker pull)  │
                             └──────────┬─────────▲──────────┘
                                        │         │
                     1. Basic Auth      │         │ 2. Issue Signed JWT
                     GET /api/Token     │         │    (Bearer Token)
                                        ▼         │
                     ┌────────────────────────────┴──┐
                     │    Reverse Proxy / Gateway    │
                     └───────┬───────────────┬───────┘
                             │               │
            /api/* (Control) │               │ 3. Stream Layers /v2/*
                             ▼               ▼    (Bearer Token Auth)
                   ┌───────────────────┐   ┌───────────────────┐
                   │     MobyGuard     │   │  Docker Registry  │
                   │  (Auth & Web UI)  │   │   (registry:2)    │
                   └─┬───────────────┬─┘   └─────────┬─────────┘
                     │               │               │
                     ▼               ▼               ▼
               ┌───────────┐   ┌───────────┐   ┌───────────┐
               │PostgreSQL │   │   Redis   │   │  Storage  │
               │(Database) │   │  (Cache)  │   │  Volume   │
               └───────────┘   └───────────┘   └───────────┘
```

* **Step 1 (Token Request)**: The Docker client requests an access token from MobyGuard (`/api/Token`) using HTTP Basic authentication.
* **Step 2 (Token Issuance)**: MobyGuard evaluates the user's repository permissions in PostgreSQL and returns a cryptographically signed ECDSA `ES256` Bearer JWT.
* **Step 3 (Layer Streaming)**: The Docker client presents the Bearer token directly to Docker Registry (`/v2/`), which verifies the signature and streams image layer blobs directly to storage.
* **Control Plane Isolation**: MobyGuard handles authentication, authorization policy evaluation, user management, and dashboard operations without bottlenecking image layer transfers.

---

## Quick Start

### 1. Clone the Repository
```bash
git clone https://github.com/sbairamis/mobyguard.git
cd mobyguard/MobyGuard
```

### 2. Generate the Token Signing Certificate
Generate the ECDSA P-256 (`ES256`) keypair used by MobyGuard to sign Registry JWTs and by Docker Registry to verify them:

```bash
mkdir -p certs
openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:prime256v1 -sha256 -nodes \
  -keyout certs/registry-auth.key \
  -out certs/registry-auth.crt \
  -days 3650 \
  -subj "/CN=mobyguard-token-issuer"
chmod 644 certs/*
```

### 3. Configure Environment Variables
Copy the template configuration file:

```bash
cp .env.example .env
```

Set the required environment secrets in `.env`:
* `DB_USER_PASS`: Password for PostgreSQL.
* `REDIS_PASS`: Password for Redis.
* `JWT_SECURITY_KEY`: A 32+ character string for signing session tokens (e.g., `openssl rand -base64 32`).

### 4. Deploy the Services
```bash
docker compose up -d
```

### 5. Initialize the Administrator Account
* Navigate to `http://localhost:5090` in your web browser.
* Complete the initial administrator setup form.

---

## Docker CLI Usage

### Authentication
```bash
docker login localhost:5090
```

### Push Workflow
```bash
docker tag my-app:latest localhost:5090/team-a/my-app:1.0.0
docker push localhost:5090/team-a/my-app:1.0.0
```

### Pull Workflow
```bash
docker pull localhost:5090/team-a/my-app:1.0.0
```

* **CI/CD Pipelines**: Provision an account with the `Service` role in the dashboard, grant appropriate repository permissions, and store the credentials in pipeline secrets (`DOCKER_USERNAME`, `DOCKER_PASSWORD`).

---

## Tech Stack

* **Backend**: [.NET 10 (C#)](https://dotnet.microsoft.com/) — ASP.NET Core Web API with Microsoft OpenAPI.
* **Frontend**: [React 19](https://react.dev/) + [Vite](https://vitejs.dev/) embedded in `wwwroot`.
* **Database**: [PostgreSQL 17](https://www.postgresql.org/) for relational persistence and policy storage.
* **Cache**: [Redis 8](https://redis.io/) for JWT revocation, sessions, and rate-limiting.
* **Token Signer**: Cryptographic ECDSA `ES256` token signing.
* **Testing**: Integration testing with xUnit v3 and **Testcontainers**.

---

## License

This project is licensed under the [MIT License](LICENSE).
