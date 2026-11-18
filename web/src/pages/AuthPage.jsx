import { useState } from 'react'
import { api } from '../api'

export default function AuthPage({ setupRequired, onAuthenticated, notify }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [setupToken, setSetupToken] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      if (setupRequired) {
        const res = await api.post('/api/v1/User/setup', {
          username: username.trim(),
          password,
          setupToken: setupToken.trim()
        })
        notify('Initial Admin account created.', 'ok')
        onAuthenticated(res)
      } else {
        const res = await api.post('/api/v1/User/login', {
          username: username.trim(),
          password
        })
        onAuthenticated(res)
      }
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="auth-wrap">
      <form className="auth-card" onSubmit={handleSubmit}>
        <div className="brand" style={{ padding: 0, gap: 12 }}>
          <div className="brand-logo" style={{ width: 40, height: 40, borderRadius: 10 }}>
            <img src="/logo.png" alt="MobyGuard" />
          </div>
          <div className="brand-text">
            <b style={{ fontSize: 18 }}>MobyGuard</b>
            {setupRequired && <span>Create Initial Admin Account</span>}
          </div>
        </div>

        {setupRequired && (
          <p className="sub" style={{ fontSize: 13, margin: 0 }}>
            No users exist yet. Create the primary Administrator account to get started.
          </p>
        )}

        {error && (
          <div style={{
            padding: '9px 12px',
            borderRadius: 'var(--r-sm)',
            background: 'color-mix(in srgb, var(--err) 12%, transparent)',
            border: '1px solid color-mix(in srgb, var(--err) 35%, transparent)',
            color: 'var(--err)',
            fontSize: 13
          }}>
            {error}
          </div>
        )}

        {setupRequired && (
          <label className="field">
            One-Time Setup Token
            <input
              type="password"
              required
              autoFocus
              value={setupToken}
              onChange={(e) => setSetupToken(e.target.value)}
              placeholder="Paste token from container logs…"
            />
            <span className="muted" style={{ fontSize: 11.5 }}>
              Printed to the MobyGuard container logs on first startup.
            </span>
          </label>
        )}

        <label className="field">
          Username
          <input
            type="text"
            required
            autoFocus={!setupRequired}
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            placeholder="admin"
          />
        </label>

        <label className="field">
          Password
          <input
            type="password"
            required
            minLength={setupRequired ? 8 : 1}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="••••••••"
          />
          {setupRequired && (
            <span className="muted" style={{ fontSize: 11.5 }}>
              Must be 8–64 characters with uppercase, lowercase, number, and symbol.
            </span>
          )}
        </label>

        <button type="submit" className="primary" disabled={loading} style={{ height: 38, marginTop: 4 }}>
          {loading ? <span className="spin" /> : (setupRequired ? 'Complete Setup & Sign In' : 'Sign In')}
        </button>
      </form>
    </div>
  )
}
