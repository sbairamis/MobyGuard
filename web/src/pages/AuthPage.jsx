import { useEffect, useState } from 'react'
import { api } from '../api'

export default function AuthPage({ setupRequired, onAuthenticated, notify }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [setupToken, setSetupToken] = useState('')
  const [tokenVerified, setTokenVerified] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!setupRequired) {
      setTokenVerified(false)
    }
  }, [setupRequired])

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      if (setupRequired && !tokenVerified) {
        await api.post('/api/v1/User/verify-setup-token', {
          setupToken: setupToken.trim()
        })
        setTokenVerified(true)
      } else if (setupRequired && tokenVerified) {
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
            {setupRequired && (
              <span>
                {tokenVerified ? 'Create Initial Admin Account' : 'Verify One-Time Setup Token'}
              </span>
            )}
          </div>
        </div>

        {setupRequired && !tokenVerified && (
          <p className="sub" style={{ fontSize: 13, margin: 0 }}>
            Enter the one-time setup token printed to the MobyGuard container logs to unlock initial administrator setup.
          </p>
        )}

        {setupRequired && tokenVerified && (
          <p className="sub" style={{ fontSize: 13, margin: 0 }}>
            Setup token verified. Create the primary Administrator account to get started.
          </p>
        )}

        {error && (
          <div className="alert-banner-err" role="alert">
            {error}
          </div>
        )}

        {setupRequired && !tokenVerified ? (
          <label className="field">
            One-Time Setup Token
            <input
              type="password"
              required
              autoFocus
              autoComplete="one-time-code"
              value={setupToken}
              onChange={(e) => setSetupToken(e.target.value)}
              placeholder="Paste token from container logs…"
            />
            <span className="field-hint">
              Printed to the MobyGuard container logs on first startup.
            </span>
          </label>
        ) : (
          <>
            <label className="field">
              Username
              <input
                type="text"
                required
                autoFocus
                autoComplete="username"
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
                autoComplete={setupRequired ? 'new-password' : 'current-password'}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="••••••••"
              />
              {setupRequired && (
                <span className="field-hint">
                  Must be 8–64 characters with uppercase, lowercase, number, and symbol.
                </span>
              )}
            </label>
          </>
        )}

        <button type="submit" className="primary" disabled={loading} style={{ height: 38, marginTop: 4 }}>
          {loading ? (
            <span className="spin" />
          ) : setupRequired && !tokenVerified ? (
            'Verify Setup Token'
          ) : setupRequired ? (
            'Complete Setup & Sign In'
          ) : (
            'Sign In'
          )}
        </button>
      </form>
    </div>
  )
}
