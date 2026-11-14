import { useEffect, useState } from 'react'
import { api } from '../api'

export default function ProfilePage({ notify }) {
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [username, setUsername] = useState('')
  const [role, setRole] = useState('')
  const [loadingProfile, setLoadingProfile] = useState(true)
  const [savingProfile, setSavingProfile] = useState(false)

  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [savingPassword, setSavingPassword] = useState(false)

  useEffect(() => {
    api.get('/api/v1/User/me')
      .then((me) => {
        if (me) {
          setUsername(me.username)
          setRole(me.userType)
          setFirstName(me.firstName)
          setLastName(me.lastName)
        }
      })
      .catch(() => {})
      .finally(() => setLoadingProfile(false))
  }, [])

  async function handleSaveProfile(e) {
    e.preventDefault()
    setSavingProfile(true)
    try {
      await api.put('/api/v1/User/me', {
        firstName: firstName.trim(),
        lastName: lastName.trim()
      })
      notify('Profile updated.', 'ok')
    } catch (err) {
      notify(err.message, 'err')
    } finally {
      setSavingProfile(false)
    }
  }

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
        <form className="card" onSubmit={handleSaveProfile} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <h2 style={{ margin: 0 }}>Profile Details</h2>
          {loadingProfile ? (
            <div className="empty"><span className="spin" /></div>
          ) : (
            <>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                <label className="field">
                  Username
                  <input type="text" value={username} disabled />
                </label>
                <label className="field">
                  Role
                  <input type="text" value={role} disabled />
                </label>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                <label className="field">
                  First Name
                  <input type="text" required value={firstName} onChange={(e) => setFirstName(e.target.value)} />
                </label>
                <label className="field">
                  Last Name
                  <input type="text" required value={lastName} onChange={(e) => setLastName(e.target.value)} />
                </label>
              </div>

              <div>
                <button type="submit" className="primary" disabled={savingProfile}>
                  {savingProfile ? <span className="spin" /> : 'Save Profile'}
                </button>
              </div>
            </>
          )}
        </form>

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
              minLength={4}
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
            />
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
