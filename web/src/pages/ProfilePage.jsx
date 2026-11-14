import { useEffect, useState } from 'react'
import { api, formatDate } from '../api'
import Icon from '../components/Icon'

export default function ProfilePage({ notify }) {
  const [profile, setProfile] = useState(null)
  const [loadingProfile, setLoadingProfile] = useState(true)

  const [showPasswordModal, setShowPasswordModal] = useState(false)
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

  function openPasswordModal() {
    setCurrentPassword('')
    setNewPassword('')
    setShowPasswordModal(true)
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
      setShowPasswordModal(false)
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
        <button className="primary" onClick={openPasswordModal}>
          <Icon name="key" size={15} />
          Change Password
        </button>
      </div>

      <div className="card" style={{ display: 'flex', flexDirection: 'column', gap: 14, maxWidth: 640 }}>
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

      {showPasswordModal && (
        <div className="modal-backdrop" onClick={() => setShowPasswordModal(false)}>
          <form className="modal" onClick={(e) => e.stopPropagation()} onSubmit={handleChangePassword}>
            <div className="modal-head">
              <h2>Change Password</h2>
            </div>

            <p className="muted" style={{ margin: 0, fontSize: 13 }}>
              Your password is used for both this web dashboard and <code>docker login</code>.
            </p>

            <label className="field">
              Current Password
              <input
                type="password"
                required
                autoFocus
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

            <div className="modal-foot">
              <button type="button" className="ghost" onClick={() => setShowPasswordModal(false)}>
                Cancel
              </button>
              <button type="submit" className="primary" disabled={savingPassword}>
                {savingPassword ? <span className="spin" /> : 'Update Password'}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  )
}
