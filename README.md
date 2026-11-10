<p align="center">
  <img src="web/public/logo.png" width="120" alt="MobyGuard Logo" style="border-radius: 24px;" />
</p>

<h1 align="center">MobyGuard</h1>

<p align="center">
  <b>The lightweight, self-hosted Docker & OCI Registry Control Plane.</b><br />
  Fine-grained access control, ECDSA token authentication, and a modern dashboard for vanilla Docker Registry.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/React-18-61DAFB?logo=react&logoColor=black" alt="React 18" />
  <img src="https://img.shields.io/badge/Docker-Registry_v2%2Fv3-2496ED?logo=docker&logoColor=white" alt="Docker Registry" />
  <img src="https://img.shields.io/badge/PostgreSQL-17-4169E1?logo=postgresql&logoColor=white" alt="PostgreSQL" />
  <img src="https://img.shields.io/badge/Redis-7-DC382D?logo=redis&logoColor=white" alt="Redis" />
  <img src="https://img.shields.io/badge/License-MIT-green.svg" alt="License" />
</p>

---

## 💡 Why MobyGuard?

Setting up a private, self-hosted Docker registry usually leaves teams stuck between two extremes:

1. **Vanilla Docker Distribution (`registry:2`)**:
   * ❌ Primitive `htpasswd` basic authentication (all-or-nothing access).
   * ❌ No fine-grained repository permissions (if a user has access, they can pull or overwrite *any* image).
   * ❌ No web dashboard to view images, tags, layer sizes, or delete tags.
2. **Enterprise Suites (e.g., Harbor)**:
   * ❌ Heavyweight footprint (6–8 microservice containers, requiring 4GB–8GB+ RAM).
   * ❌ Complex Helm charts and operational overhead for small-to-medium teams.

**MobyGuard solves this dilemma.** It is a single, self-contained appliance (~80MB RAM) that acts as the **native Docker Registry v2 Token Server**, **Control Plane**, and **Web Dashboard** for vanilla Docker Distribution.

---

## ✨ Features

* 🛡️ **Native Docker v2 / OCI Token Authentication**:
  * Fully compliant with the [Docker Registry v2 Bearer Token Specification](https://distribution.github.io/distribution/spec/auth/token/).
  * Works out-of-the-box with `docker login`, `docker pull`, and `docker push`.
  * Asymmetric **ECDSA P-256 (`ES256`)** token signing with public JWKS (`/api/Token/jwks`).
* 🔐 **Granular Repository Access Policies (ACL)**:
  * Define per-user access with exact matches (`team-a/backend`), namespace wildcards (`team-a/*`), or global access (`*`).
  * Explicit toggles for **Pull**, **Push**, and **Delete** actions.
* 🐳 **Modern Registry Dashboard**:
  * Explore repositories with **RFC 5988 catalog pagination** (supports 10,000+ repositories).
  * Inspect OCI / Docker v2 manifests, digest hashes, layer sizes, and container configs.
  * Delete image tags directly from the UI.
* 👥 **Users & Headless Service Accounts**:
  * Create human accounts (`Admin`, `User`) and headless automation accounts (`Service`) for CI/CD pipelines (GitHub Actions, GitLab CI, Drone).
* ⚡ **Production Hardened**:
  * `PBKDF2-HMAC-SHA512` password hashing with cryptographic salts.
  * 15-minute JWT lifetime with Redis-backed refresh token rotation and **instant user-level revocation**.
  * Redis-backed brute-force lockout for failed login attempts.
  * Zero source maps served in production.
* 📜 **Audit Trail**:
  * Full audit logs recording issued registry tokens, tag deletions, user logins, and policy modifications.

---

## 🏗️ Architecture

```
                  ┌────────────────────────────────────────┐
                  │          Docker CLI / Kaniko           │
                  │       (docker push / docker pull)      │
                  └──────────────────┬─────────────────────┘
                                     │
           1. Basic Auth /api/Token   │  3. Bearer Token Pull/Push /v2/*
                                     ▼
                     ┌───────────────────────────────┐
                     │      Nginx Reverse Proxy      │
                     └───────┬───────────────┬───────┘
                             │               │
            /api/* (Control) │               │ /v2/* (Layer Streaming)
                             ▼               ▼
                   ┌───────────────────┐   ┌───────────────────┐
                   │     MobyGuard     │   │  Docker Registry  │
                   │  (Auth & Web UI)  │   │   (registry:2)    │
                   └─┬───────────────┬─┘   └─────────┬─────────┘
                     │               │               │
                     ▼               ▼               ▼
               ┌───────────┐   ┌───────────┐   ┌───────────┐
               │PostgreSQL │   │   Redis   │   │  Storage  │
               │ (citext)  │   │  (Cache)  │   │  Volume   │
               └───────────┘   └───────────┘   └───────────┘
```

Image layers (`/v2/`) stream directly between the client and Docker Registry for maximum throughput, while MobyGuard handles authentication, access evaluation, and dashboard management (`/api/`).

---

## 🚀 Quick Start (Docker Compose)

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

### 3. Configure the Environment
Copy the example environment configuration:

```bash
cp .env.example .env
```

Edit `.env` and set secure passwords:
* `DB_USER_PASS`: PostgreSQL password.
* `REDIS_PASS`: Redis password.
* `JWT_SECURITY_KEY`: A random 32+ character string (e.g. `openssl rand -base64 32`).

### 4. Start the Stack
```bash
docker compose up -d
```

### 5. Create the Initial Administrator Account
1. Open your browser at **`http://localhost:5090`**.
2. Complete the initial one-time Administrator setup form.
3. You are ready to manage repositories, create users, and define access policies!

---

## 💻 Docker CLI Usage

### 1. Log in from the Command Line
```bash
docker login localhost:5090
# Username: your-username
# Password: your-password
```

### 2. Tag and Push an Image
```bash
docker tag my-app:latest localhost:5090/team-a/my-app:1.0.0
docker push localhost:5090/team-a/my-app:1.0.0
```

### 3. Pull an Image
```bash
docker pull localhost:5090/team-a/my-app:1.0.0
```

> **Note for CI/CD Pipelines**: Create a user with the **`Service`** role in the MobyGuard dashboard and assign it policy permissions. Use those credentials in your CI/CD runner secrets (`DOCKER_USERNAME`, `DOCKER_PASSWORD`).

---

## 🌐 Production Reverse Proxy (Nginx)

When exposing MobyGuard to your corporate intranet or public domain, place Nginx in front to route traffic efficiently:

```nginx
# Nginx Configuration
server {
    listen 443 ssl http2;
    server_name registry.yourcompany.com;

    ssl_certificate /etc/letsencrypt/live/registry.yourcompany.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/registry.yourcompany.com/privkey.pem;

    # 1. Route Dashboard & Control Plane to MobyGuard
    location / {
        proxy_pass http://127.0.0.1:5090;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # 2. Route Docker Distribution Layer Blobs Directly to Registry
    location /v2/ {
        # Disable client upload limits for multi-gigabyte layers
        client_max_body_size 0;
        chunked_transfer_encoding on;

        proxy_pass http://127.0.0.1:5000/v2/;
        proxy_set_header Host $http_host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 900;
    }
}
```

---

## 🛠️ Tech Stack

* **Backend**: [.NET 10 (C#)](https://dotnet.microsoft.com/) — ASP.NET Core Web API with Microsoft OpenAPI.
* **Frontend**: [React 18](https://react.dev/) + [Vite](https://vitejs.dev/) embedded directly in `wwwroot`.
* **Database**: [PostgreSQL 17](https://www.postgresql.org/) with `citext` for case-insensitive matching.
* **Cache**: [Redis 7](https://redis.io/) for JWT revocation, sessions, and login rate-limiting.
* **Token Signer**: Built-in cryptographic ECDSA `ES256` token generation.
* **Testing**: Comprehensive xUnit v3 integration test suite using **Testcontainers**.

---

## 🧪 Running Tests

Integration tests run real PostgreSQL and Redis containers automatically via Testcontainers:

```bash
dotnet test
```

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
