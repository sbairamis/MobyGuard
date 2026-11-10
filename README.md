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

<p align="center">
  <img src="docs/screenshots/repositories.png" width="900" alt="MobyGuard Dashboard" style="border-radius: 8px;" />
</p>

---

## Why MobyGuard?

Organizations deploying private Docker registries typically face two limitations:

* **Docker Registry & CNCF Distribution (`registry:2` / `registry:3`) Limitations**:
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

## Screenshots

### Images & Manifest Inspector
Browse catalog repositories, inspect OCI and Docker v2 image tags, examine layer sizes, and retrieve pull commands:

<p align="center">
  <img src="docs/screenshots/repositories.png" alt="Images & Manifests" />
</p>

### Access Control Policies
Manage per-user repository authorization rules with exact or wildcard (`namespace/*`) patterns and granular Pull, Push, and Delete toggles:

<p align="center">
  <img src="docs/screenshots/policies.png" alt="Repository Access Control" />
</p>

### User & Service Account Management
Provision human operator accounts and dedicated headless service accounts for CI/CD automation pipelines:

<p align="center">
  <img src="docs/screenshots/users.png" alt="User Management" />
</p>

### Audit Trail
Monitor token issuances, administrative actions, client IP addresses, and timestamps:

<p align="center">
  <img src="docs/screenshots/audit-logs.png" alt="Audit Logs" />
</p>

### Authentication Interface
Clean sign-in interface with ECDSA token authentication and administrative onboarding:

<p align="center">
  <img src="docs/screenshots/login.png" alt="Sign In" />
</p>

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
                   │  (Auth & Web UI)  │   │ (registry:2 / v3) │
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

Deploy MobyGuard with Docker Compose without needing to clone the source repository.

### 1. Create Working Directory and Generate Certificates
Create a project folder and generate the ECDSA P-256 (`ES256`) keypair used for Docker Registry token authentication:

```bash
mkdir -p mobyguard/certs && cd mobyguard

openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:prime256v1 -sha256 -nodes \
  -keyout certs/registry-auth.key \
  -out certs/registry-auth.crt \
  -days 3650 \
  -subj "/CN=mobyguard-token-issuer"
chmod 644 certs/*
```

#### Certificate Roles and Trust Relationship
* **Asymmetric ECDSA (`ES256`)**: MobyGuard implements the [Docker Registry v2 Bearer Token Specification](https://distribution.github.io/distribution/spec/auth/token/). Token generation uses elliptic curve cryptography (`prime256v1`), delivering the security strength of 3072-bit RSA with smaller token payload sizes and faster verification.
* **`certs/registry-auth.key` (Private Key)**:
  * Kept strictly confidential by MobyGuard.
  * Mounted into the MobyGuard container at `/app/certs/registry-auth.key`.
  * Used exclusively by MobyGuard's `/api/Token` endpoint to sign outgoing Bearer JWTs granted to Docker CLI clients.
* **`certs/registry-auth.crt` (Public Certificate)**:
  * Contains the public key and issuer identity (`CN=mobyguard-token-issuer`).
  * Mounted into both MobyGuard and the Docker Registry container.
  * Docker Registry uses this certificate as its root trust bundle to verify the signature of incoming Bearer tokens without requiring access to the private key.

#### Parameter Breakdown
* `-x509`: Generates a self-signed X.509 certificate directly instead of a certificate signing request.
* `-newkey ec -pkeyopt ec_paramgen_curve:prime256v1`: Generates an ECDSA private key using the NIST P-256 curve.
* `-sha256`: Signs the certificate using the SHA-256 digest algorithm.
* `-nodes`: Stores the private key unencrypted so container services can load it unattended on startup.
* `-days 3650`: Sets a 10-year validity period for internal service authentication.
* `-subj "/CN=mobyguard-token-issuer"`: Sets the subject Common Name matching the configured token issuer.
* `chmod 644 certs/*`: Grants read access to the container process while preventing unauthorized host modifications.

#### Upstream Docker Registry Configuration
To configure Docker Registry (or CNCF Distribution) to authenticate via MobyGuard using this certificate, apply the following settings to your registry container:

```yaml
environment:
  REGISTRY_AUTH: token
  REGISTRY_AUTH_TOKEN_REALM: https://registry.example.com/api/Token
  REGISTRY_AUTH_TOKEN_SERVICE: registry
  REGISTRY_AUTH_TOKEN_ISSUER: mobyguard-token-issuer
  REGISTRY_AUTH_TOKEN_ROOTCERTBUNDLE: /certs/registry-auth.crt
volumes:
  - ./certs/registry-auth.crt:/certs/registry-auth.crt:ro
```

### 2. Configure `compose.yml`
Save the following configuration as `compose.yml`:

```yaml
services:
  mobyguard:
    image: ghcr.io/sbairamis/mobyguard:${API_VERSION:-latest}
    restart: unless-stopped
    container_name: mobyguard
    ports:
      - "${API_PORT:-5090}:5090"
    volumes:
      - ${CERTS_DIR:-./certs}:/app/certs:ro
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_healthy
    environment:
      ConnectionStrings__DefaultConnection: >-
        Host=postgres;Port=5432;Database=${DB_NAME:-mobyguard};
        Username=${DB_USER:-mobyguard};Password=${DB_USER_PASS};
      ConnectionStrings__RedisConnection: >-
        redis:6379,password=${REDIS_PASS}
      RegistryTokenOptions__RegistryUrl: "${REGISTRY_URL:-http://registry:5000}"
      JwtOptions__SecurityKey: "${JWT_SECURITY_KEY}"
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:5090/api/health"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 10s
    networks:
      - default
      - registry-net

  postgres:
    image: postgres:17-alpine
    restart: unless-stopped
    container_name: mobyguard-postgres
    ports:
      - "${DB_PORT:-127.0.0.1:5432}:5432"
    environment:
      POSTGRES_USER: ${DB_USER:-mobyguard}
      POSTGRES_PASSWORD: ${DB_USER_PASS}
      POSTGRES_DB: ${DB_NAME:-mobyguard}
    volumes:
      - ./data/postgres:/var/lib/postgresql/data
    healthcheck:
      test:
        - "CMD-SHELL"
        - "pg_isready -U ${DB_USER:-mobyguard} -d ${DB_NAME:-mobyguard}"
      interval: 10s
      timeout: 5s
      retries: 5

  redis:
    image: redis:8.10-alpine
    container_name: mobyguard-redis
    restart: unless-stopped
    volumes:
      - ./data/redis:/data
    environment:
      REDISCLI_AUTH: ${REDIS_PASS}
    command: >-
      redis-server
      --requirepass ${REDIS_PASS}
      --appendonly yes
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 5s

networks:
  registry-net:
    external: true
```

### 3. Configure Environment Variables
Create a `.env` file in the same directory (or copy from `.env.example`):

```bash
cat << 'EOF' > .env
# Database Credentials
DB_USER_PASS=change_this_postgres_password

# Redis Credentials
REDIS_PASS=change_this_redis_password

# Web UI Authentication (minimum 32 characters)
JWT_SECURITY_KEY=generate_a_secure_random_32_character_security_key_here!

# Upstream Docker Registry / CNCF Distribution URL
REGISTRY_URL=http://registry:5000
EOF
```

* **`JWT_SECURITY_KEY`**: Set a cryptographically secure random string with at least 32 characters (e.g., generated with `openssl rand -base64 32`).
* **`REGISTRY_URL`**: Set the URL of your Docker Registry or CNCF Distribution instance.

### 4. Deploy the Services
Ensure the shared Docker bridge network exists and start the containers:

```bash
docker network create registry-net 2>/dev/null || true
docker compose up -d
```

### 5. Initialize the Administrator Account
* Navigate to `http://localhost:5090` in your web browser.
* Complete the initial administrator setup form.

---

## Docker CLI Usage

Replace `registry.example.com` with your registry's fully qualified domain name or IP address.

### 1. Authentication
Authenticate with an administrator, standard user, or dedicated CI/CD service account:

```bash
docker login registry.example.com
```

For non-interactive environments (scripts or automated builds):

```bash
echo "$DOCKER_PASSWORD" | docker login registry.example.com -u "$DOCKER_USERNAME" --password-stdin
```

### 2. Push Workflow
Tag a local image with a repository path matching an authorized ACL policy (e.g., `team-a/*` with `Push` permission enabled):

```bash
docker tag my-app:1.0.0 registry.example.com/team-a/my-app:1.0.0
docker push registry.example.com/team-a/my-app:1.0.0
```

### 3. Pull Workflow
Pull images matching an authorized ACL policy (requiring `Pull` permission):

```bash
docker pull registry.example.com/team-a/my-app:1.0.0
```

### 4. CI/CD Pipeline Automation
For headless CI/CD systems (GitHub Actions, GitLab CI, Drone, Jenkins):
* **Account Provisioning**: Create a dedicated account with the `Service` role in the MobyGuard dashboard (e.g., `github-runner`).
* **Policy Assignment**: Grant targeted repository permissions (e.g., `team-a/*` with `Pull` and `Push` enabled).
* **Pipeline Configuration**: Inject credentials through pipeline secrets. Example for GitHub Actions:

```yaml
- name: Log in to Docker Registry
  uses: docker/login-action@v3
  with:
    registry: registry.example.com
    username: ${{ secrets.REGISTRY_USERNAME }}
    password: ${{ secrets.REGISTRY_PASSWORD }}

- name: Build and Push Image
  uses: docker/build-push-action@v6
  with:
    push: true
    tags: registry.example.com/team-a/my-app:${{ github.sha }}
```

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
