import { useEffect, useState } from 'react'
import { api, DASHBOARD_VERSION, formatDate } from '../api'

export default function ProfilePage({ apiVersion = 'v1.0.7', notify }) {
  const [profile, setProfile] = useState(null)
  const [loadingProfile, setLoadingProfile] = useState(true)

  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [savingPassword, setSavingPassword] = useState(false)

  useEffect(() => {
    api.get('/api/v1/User/me')
      .then((me) => {
        if (me) {
          setProfile(me)
        }
      })
      .catch(() => {})
      .finally(() => setLoadingProfile(false))
  }, [])

  async function handleChangePassword(e) {
    e.preventDefault()
    setSavingPassword(true)
    try {
      await api.put('/api/v1/User/me/password', {
        currentPassword,
        password: newPassword
      })
      setCurrentPassword('')
      setNewPassword('')
      notify('Password updated.', 'ok')
    } catch (err) {
      notify(err.message, 'err')
    } finally {
      setSavingPassword(false)
    }
  }

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>My Account</h1>
        </div>
      </div>

      <div className="grid cols-2">
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <div className="card" style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
            <h2 style={{ margin: 0 }}>Account Details</h2>
            {loadingProfile ? (
              <div className="empty"><span className="spin" /></div>
            ) : (
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                <label className="field">
                  Username
                  <input type="text" value={profile?.username || ''} disabled />
                </label>
                <label className="field">
                  Role
                  <input type="text" value={profile?.userType || ''} disabled />
                </label>
                <label className="field">
                  Created
                  <input type="text" value={formatDate(profile?.createdAt)} disabled />
                </label>
                <label className="field">
                  Last Login
                  <input type="text" value={formatDate(profile?.lastLogin)} disabled />
                </label>
              </div>
            )}
          </div>

          <div className="card" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            <h2 style={{ margin: 0 }}>System & Version Info</h2>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
              <div style={{ background: 'var(--bg2)', padding: '10px 12px', borderRadius: 'var(--r-sm)', border: '1px solid var(--border)' }}>
                <div className="dim" style={{ fontSize: 11, textTransform: 'uppercase', fontWeight: 650 }}>API Version</div>
                <div className="mono" style={{ fontSize: 13.5, marginTop: 4 }}>{apiVersion} (/api/v1)</div>
              </div>
              <div style={{ background: 'var(--bg2)', padding: '10px 12px', borderRadius: 'var(--r-sm)', border: '1px solid var(--border)' }}>
                <div className="dim" style={{ fontSize: 11, textTransform: 'uppercase', fontWeight: 650 }}>Dashboard Version</div>
                <div className="mono" style={{ fontSize: 13.5, marginTop: 4 }}>{DASHBOARD_VERSION}</div>
              </div>
            </div>
          </div>
        </div>

        <form className="card" onSubmit={handleChangePassword} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <h2 style={{ margin: 0 }}>Change Password</h2>
          <p className="muted" style={{ margin: 0, fontSize: 13 }}>
            Your password is used for both this web dashboard and <code>docker login</code>.
          </p>

          <label className="field">
            Current Password
            <input
              type="password"
              required
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
            />
          </label>

          <label className="field">
            New Password
            <input
              type="password"
              required
              minLength={8}
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
            />
            <span className="muted" style={{ fontSize: 11.5 }}>
              Must be 8–128 characters with uppercase, lowercase, number, and symbol (not blacklisted).
            </span>
          </label>

          <div>
            <button type="submit" className="primary" disabled={savingPassword}>
              {savingPassword ? <span className="spin" /> : 'Update Password'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
