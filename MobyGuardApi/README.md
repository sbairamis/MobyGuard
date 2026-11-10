# MobyGuard (`MobyGuardApi`)

Docker Registry v3 (Distribution) Token Authentication & Repository Permission Management API built on .NET 10, PostgreSQL (`citext`), and Redis.

---

## 1. Generate the Signing Certificate & Private Key

Generate a self-signed RSA keypair used by `MobyGuardApi` to sign Registry JWTs and by Distribution v3 to verify them:

```bash
mkdir -p certs
openssl req -x509 -newkey rsa:4096 -sha256 -nodes \
  -keyout certs/registry-auth.key \
  -out certs/registry-auth.crt \
  -days 3650 \
  -subj "/CN=mobyguard-token-issuer"
```

---

## 2. Docker Registry v3 (`config.yml`) Configuration

Configure your Registry v3 instance to delegate authentication and authorization to `MobyGuardApi`:

```yaml
version: 0.1
storage:
  filesystem:
    rootdirectory: /var/lib/registry

http:
  addr: :5000

auth:
  token:
    realm: "https://registry.yourcompany.com/api/Token"
    service: "registry"
    issuer: "mobyguard-token-issuer"
    rootcertbundle: "/etc/distribution/certs/registry-auth.crt"
    # Or in Registry v3 you can use JWKS instead of rootcertbundle:
    # jwks: "/etc/distribution/certs/jwks.json"
```

---

## 3. Nginx Reverse Proxy (`nginx.conf`) Configuration

Route `/api/` to `mobyguard-api` and `/v2/` directly to the `registry` container so image layers stream directly between Nginx and the Registry without passing through C#:

```nginx
location /api/ {
    proxy_pass http://mobyguard-api:5090/api/;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
}

location /v2/ {
    client_max_body_size 0;
    chunked_transfer_encoding on;

    proxy_pass http://registry:5000/v2/;
    proxy_set_header Host $http_host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_read_timeout 900;
}
```

---

## 4. Key Endpoints

| Endpoint | Method | Auth | Description |
| :--- | :--- | :--- | :--- |
| `/api/Token` | `GET` | Basic Auth (`docker login`) | Issues RS256-signed Docker Registry v3 JWTs with evaluated `access` scopes (`pull`, `push`, `delete`). |
| `/api/Token/jwks` | `GET` | Anonymous | Returns the public JSON Web Key Set (`JWKS`) for the signing certificate. |
| `/api/RepositoryPermission` | `GET`, `POST`, `PUT`, `DELETE` | Bearer JWT (`Admin` / `User`) | Manage per-user repository permissions (exact `team-a/app`, namespace wildcard `team-a/*`, or global `*`). |
| `/api/User` | `GET`, `POST`, `PUT`, `DELETE` | Bearer JWT (`Admin` / `User`) | Manage users, passwords, and login (`POST /api/User/login`). |
| `/api/UserType` | `GET`, `POST`, `PUT`, `DELETE` | Bearer JWT (`Admin` / `User`) | Manage roles (`Admin`, `User`, `Service`). |
| `/api/AuditLog` | `GET` | Bearer JWT (`Admin` / `User`) | Search and paginate audit logs for issued tokens, user creations, and permission changes. |
| `/api/health` | `GET` | Anonymous | Verifies PostgreSQL, Redis, and Registry signing key health. |
