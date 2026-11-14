import { useState } from 'react'
import { api } from '../api'

export default function AuthPage({ setupRequired, onAuthenticated, notify }) {
  const [username, setUsername] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [password, setPassword] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      if (setupRequired) {
        const res = await api.post('/api/v1/User/setup', {
          username,
          firstName,
          lastName,
          password
        })
        notify('Initial Admin account created.', 'ok')
        onAuthenticated(res)
      } else {
        const res = await api.post('/api/v1/User/login', {
          username,
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
        <div className="brand" style={{ padding: 0, gap: 14 }}>
          <div className="brand-logo" style={{ width: 42, height: 42, borderRadius: 11 }}>
            <img src="/logo.png" alt="MobyGuard" />
          </div>
          <div className="brand-text">
            <b style={{ fontSize: 18 }}>MobyGuard</b>
            <span>Docker Registry v3 Auth & Access Control</span>
          </div>
        </div>

        <div>
          <h1 style={{ fontSize: 18 }}>
            {setupRequired ? 'Create Initial Admin Account' : 'Sign in to MobyGuard'}
          </h1>
          {setupRequired && (
            <p className="sub" style={{ fontSize: 13 }}>
              No users exist yet. Create the primary Administrator account to get started.
            </p>
          )}
        </div>

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

        <label className="field">
          Username
          <input
            type="text"
            required
            autoFocus
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            placeholder="admin"
          />
        </label>

        {setupRequired && (
          <>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
              <label className="field">
                First Name
                <input
                  type="text"
                  required
                  value={firstName}
                  onChange={(e) => setFirstName(e.target.value)}
                  placeholder="Admin"
                />
              </label>
              <label className="field">
                Last Name
                <input
                  type="text"
                  required
                  value={lastName}
                  onChange={(e) => setLastName(e.target.value)}
                  placeholder="User"
                />
              </label>
            </div>
          </>
        )}

        <label className="field">
          Password
          <input
            type="password"
            required
            minLength={setupRequired ? 6 : 1}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="••••••••"
          />
        </label>

        <button type="submit" className="primary" disabled={loading} style={{ height: 38, marginTop: 4 }}>
          {loading ? <span className="spin" /> : (setupRequired ? 'Complete Setup & Sign In' : 'Sign In')}
        </button>
      </form>
    </div>
  )
}
