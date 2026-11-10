import { useEffect, useState } from 'react'
import { api, formatDate } from '../api'
import Icon from '../components/Icon'

export default function OverviewPage({ user, notify }) {
  const [repos, setRepos] = useState([])
  const [usersCount, setUsersCount] = useState(0)
  const [permsCount, setPermsCount] = useState(0)
  const [recentLogs, setRecentLogs] = useState([])
  const [jwks, setJwks] = useState(null)
  const [healthy, setHealthy] = useState(true)
  const [loading, setLoading] = useState(true)

  const isAdmin = user?.userType === 'Admin'
  const host = window.location.host || 'registry.yourcompany.com'

  async function loadOverview() {
    setLoading(true)
    try {
      const [jwksRes, logsRes, usersRes] = await Promise.all([
        api.get('/api/Token/jwks').catch(() => null),
        api.get('/api/AuditLog?PageNumber=1&PageSize=8').catch(() => null),
        api.get('/api/User?PageNumber=1&PageSize=1').catch(() => null)
      ])
      setJwks(jwksRes)
      setRecentLogs(logsRes?.data || [])
      setUsersCount(usersRes?.totalRecords || 0)

      if (isAdmin) {
        const pRes = await api.get('/api/RepositoryPermission?PageNumber=1&PageSize=1').catch(() => null)
        setPermsCount(pRes?.totalRecords || 0)
      } else {
        const myPerms = await api.get('/api/RepositoryPermission/me').catch(() => [])
        setPermsCount(myPerms?.length || 0)
      }

      const rRes = await api.get('/api/Registry/repositories').catch(() => [])
      setRepos(rRes || [])

      const hRes = await fetch('/api/health')
      setHealthy(hRes.ok)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadOverview()
  }, [])

  const keyInfo = jwks?.keys?.[0]

  function copyText(text, label) {
    navigator.clipboard.writeText(text)
    notify(`${label} copied to clipboard`, 'ok')
  }

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Overview</h1>
          <p className="sub">Docker Registry v3 token authority, repository policies, and audit activity</p>
        </div>
        <button onClick={loadOverview} disabled={loading}>
          <Icon name="refresh" size={15} />
          Refresh
        </button>
      </div>

      <div className="grid cols-4" style={{ marginBottom: 16 }}>
        <div className="card stat">
          <span className="l">Accessible Repositories</span>
          <span className="v">{repos.length}</span>
          <span className="dim" style={{ fontSize: 12 }}>In upstream Registry v3</span>
        </div>

        <div className="card stat">
          <span className="l">{isAdmin ? 'Total Permission Rules' : 'My Permission Rules'}</span>
          <span className="v">{permsCount}</span>
          <span className="dim" style={{ fontSize: 12 }}>Pull / Push / Delete policies</span>
        </div>

        <div className="card stat">
          <span className="l">Registered Accounts</span>
          <span className="v">{usersCount}</span>
          <span className="dim" style={{ fontSize: 12 }}>Admin, User & Service roles</span>
        </div>

        <div className="card stat">
          <span className="l">System & Signing Status</span>
          <div className="row" style={{ gap: 8, marginTop: 4 }}>
            <span className={`dot ${healthy ? 'ok' : 'err'}`} />
            <b style={{ fontSize: 16 }}>{healthy ? 'Healthy' : 'Degraded'}</b>
            {keyInfo && <span className="badge accent">{keyInfo.alg} ({keyInfo.crv})</span>}
          </div>
          <span className="dim mono" style={{ fontSize: 11, marginTop: 2 }}>
            {keyInfo?.kid ? `kid: ${keyInfo.kid.slice(0, 24)}…` : 'ES256 Signer Active'}
          </span>
        </div>
      </div>

      <div className="grid cols-2" style={{ marginBottom: 16 }}>
        <div className="card">
          <div className="row" style={{ justifyContent: 'space-between', marginBottom: 8 }}>
            <h2 style={{ margin: 0 }}>Quick Docker CLI Reference</h2>
            <span className="badge info">Registry v3 Token Auth</span>
          </div>
          <p className="muted" style={{ margin: '0 0 10px', fontSize: 13 }}>
            Authenticate with your MobyGuard username and password:
          </p>
          <div className="row" style={{ justifyContent: 'space-between', background: 'var(--bg2)', padding: '8px 12px', borderRadius: 'var(--r-sm)', border: '1px solid var(--border)', marginBottom: 8 }}>
            <code style={{ border: 0, background: 'none' }}>docker login {host} -u {user?.username}</code>
            <button className="ghost sm" onClick={() => copyText(`docker login ${host} -u ${user?.username}`, 'Command')}>
              <Icon name="copy" size={14} /> Copy
            </button>
          </div>
          <div className="row" style={{ justifyContent: 'space-between', background: 'var(--bg2)', padding: '8px 12px', borderRadius: 'var(--r-sm)', border: '1px solid var(--border)' }}>
            <code style={{ border: 0, background: 'none' }}>docker pull {host}/{repos[0]?.name || 'olympia/app'}:latest</code>
            <button className="ghost sm" onClick={() => copyText(`docker pull ${host}/${repos[0]?.name || 'olympia/app'}:latest`, 'Command')}>
              <Icon name="copy" size={14} /> Copy
            </button>
          </div>
        </div>

        <div className="card">
          <div className="row" style={{ justifyContent: 'space-between', marginBottom: 8 }}>
            <h2 style={{ margin: 0 }}>Signing Key & JWKS Metadata</h2>
            <a href="/api/Token/jwks" target="_blank" rel="noreferrer" className="btn sm">
              View /api/Token/jwks
            </a>
          </div>
          {keyInfo ? (
            <div style={{ display: 'grid', gridTemplateColumns: '130px 1fr', rowGap: 7, fontSize: 13 }}>
              <span className="muted">Algorithm:</span>
              <span><code>{keyInfo.alg}</code> (Elliptic Curve <code>{keyInfo.crv}</code>)</span>
              <span className="muted">Key Type:</span>
              <span><code>{keyInfo.kty}</code> (Public X/Y + X.509 <code>x5c</code>)</span>
              <span className="muted">libtrust Key ID:</span>
              <span className="mono" style={{ fontSize: 12, wordBreak: 'break-all' }}>{keyInfo.kid}</span>
            </div>
          ) : (
            <p className="muted">Loading signing key metadata…</p>
          )}
        </div>
      </div>

      <div className="card tight">
        <div className="row" style={{ padding: '14px 18px', borderBottom: '1px solid var(--border)', justifyContent: 'space-between' }}>
          <h2 style={{ margin: 0 }}>Recent Audit Activity</h2>
          <a href="#/audit" className="btn sm">View All Audit Logs</a>
        </div>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Event</th>
                <th>User</th>
                <th>Repository / Scope</th>
                <th>Granted Actions</th>
                <th>Client IP</th>
                <th>Timestamp</th>
              </tr>
            </thead>
            <tbody>
              {recentLogs.length === 0 ? (
                <tr>
                  <td colSpan={6} className="empty">No audit events recorded yet.</td>
                </tr>
              ) : (
                recentLogs.map((log) => (
                  <tr key={log.id}>
                    <td>
                      <span className={`badge ${
                        log.description === 'Manifest_Deleted' || log.description === 'Permission_Deleted'
                          ? 'err'
                          : log.description === 'Token_Issued'
                            ? 'info'
                            : 'ok'
                      }`}>
                        {log.description}
                      </span>
                    </td>
                    <td><b>{log.username}</b></td>
                    <td><code style={{ fontSize: 12 }}>{log.repository || '—'}</code></td>
                    <td>
                      {log.actions ? (
                        <span className="badge accent">{log.actions}</span>
                      ) : '—'}
                    </td>
                    <td className="mono muted">{log.clientIp || '—'}</td>
                    <td className="muted">{formatDate(log.createdAt)}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
