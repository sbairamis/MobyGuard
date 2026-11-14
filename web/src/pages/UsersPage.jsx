import { useEffect, useState } from 'react'
import { api, formatDate } from '../api'
import Icon from '../components/Icon'

export default function UsersPage({ user: currentUser, notify }) {
  const isAdmin = currentUser?.userType === 'Admin'

  const [users, setUsers] = useState([])
  const [userTypes, setUserTypes] = useState([])
  const [totalRecords, setTotalRecords] = useState(0)
  const [pageNumber, setPageNumber] = useState(1)
  const [pageSize] = useState(15)

  const [searchUsername, setSearchUsername] = useState('')
  const [filterRoleId, setFilterRoleId] = useState('')
  const [loading, setLoading] = useState(true)

  const [modalMode, setModalMode] = useState(null) // 'add' | 'edit' | 'password'
  const [selectedUser, setSelectedUser] = useState(null)

  const [username, setUsername] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [password, setPassword] = useState('')
  const [userTypeId, setUserTypeId] = useState('')
  const [isActive, setIsActive] = useState(true)
  const [saving, setSaving] = useState(false)

  async function loadUserTypes() {
    try {
      const types = await api.get('/api/v1/UserType')
      setUserTypes(types || [])
    } catch {}
  }

  async function loadUsers(page = pageNumber) {
    setLoading(true)
    try {
      const params = new URLSearchParams({
        PageNumber: String(page),
        PageSize: String(pageSize)
      })
      if (searchUsername.trim()) params.set('Username', searchUsername.trim())
      if (filterRoleId) params.set('UserTypeId', filterRoleId)

      const res = await api.get(`/api/v1/User?${params.toString()}`)
      setUsers(res?.data || [])
      setTotalRecords(res?.totalRecords || 0)
    } catch (err) {
      notify(err.message, 'err')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadUserTypes()
    loadUsers(1)
  }, [])

  function openAddModal() {
    setSelectedUser(null)
    setUsername('')
    setFirstName('')
    setLastName('')
    setPassword('')
    const defaultRole = userTypes.find((t) => t.type === 'User') || userTypes[0]
    setUserTypeId(defaultRole ? String(defaultRole.id) : '2')
    setIsActive(true)
    setModalMode('add')
  }

  function openEditModal(u) {
    setSelectedUser(u)
    setUsername(u.username)
    setFirstName(u.firstName)
    setLastName(u.lastName)
    setUserTypeId(String(u.userTypeId))
    setIsActive(u.isActive)
    setModalMode('edit')
  }

  function openPasswordModal(u) {
    setSelectedUser(u)
    setPassword('')
    setModalMode('password')
  }

  async function handleSubmit(e) {
    e.preventDefault()
    setSaving(true)
    try {
      if (modalMode === 'add') {
        await api.post('/api/v1/User', {
          username: username.trim(),
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          password,
          userTypeId: Number(userTypeId)
        })
        notify(`User "${username}" created.`, 'ok')
      } else if (modalMode === 'edit') {
        await api.put(`/api/v1/User/${selectedUser.id}`, {
          username: username.trim(),
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          userTypeId: Number(userTypeId),
          isActive
        })
        notify(`User "${username}" updated.`, 'ok')
      } else if (modalMode === 'password') {
        await api.put(`/api/v1/User/${selectedUser.id}/password`, {
          password
        })
        notify(`Password reset for "${selectedUser.username}".`, 'ok')
      }
      setModalMode(null)
      await loadUsers(pageNumber)
    } catch (err) {
      notify(err.message, 'err')
    } finally {
      setSaving(false)
    }
  }

  async function handleDeleteUser(u) {
    if (!window.confirm(`Delete user "${u.username}" and all of their repository permission policies?`)) {
      return
    }
    try {
      await api.del(`/api/v1/User/${u.id}`)
      notify(`User "${u.username}" deleted.`, 'ok')
      await loadUsers(pageNumber)
    } catch (err) {
      notify(err.message, 'err')
    }
  }

  const totalPages = Math.max(1, Math.ceil(totalRecords / pageSize))

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Users & Service Accounts</h1>
        </div>
        {isAdmin && (
          <button className="primary" onClick={openAddModal}>
            <Icon name="plus" size={15} />
            Create Account
          </button>
        )}
      </div>

      <div className="card" style={{ marginBottom: 14, padding: '12px 16px' }}>
        <form
          className="row"
          onSubmit={(e) => {
            e.preventDefault()
            setPageNumber(1)
            loadUsers(1)
          }}
        >
          <input
            type="text"
            placeholder="Search by username…"
            value={searchUsername}
            onChange={(e) => setSearchUsername(e.target.value)}
            style={{ width: 230 }}
          />
          <select
            value={filterRoleId}
            onChange={(e) => setFilterRoleId(e.target.value)}
            style={{ width: 170 }}
          >
            <option value="">All Roles</option>
            {userTypes.map((ut) => (
              <option key={ut.id} value={ut.id}>{ut.type}</option>
            ))}
          </select>
          <button type="submit">
            <Icon name="search" size={14} /> Filter
          </button>
          {(searchUsername || filterRoleId) && (
            <button
              type="button"
              className="ghost"
              onClick={() => {
                setSearchUsername('')
                setFilterRoleId('')
                setPageNumber(1)
                setTimeout(() => loadUsers(1), 0)
              }}
            >
              Reset
            </button>
          )}
        </form>
      </div>

      <div className="card tight">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>ID</th>
                <th>Username</th>
                <th>Role</th>
                <th>Full Name</th>
                <th>Status</th>
                <th>Last Login / Token</th>
                {isAdmin && <th className="num">Actions</th>}
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr>
                  <td colSpan={isAdmin ? 7 : 6} className="empty">
                    <span className="spin" /> Loading accounts…
                  </td>
                </tr>
              ) : users.length === 0 ? (
                <tr>
                  <td colSpan={isAdmin ? 7 : 6} className="empty">
                    No users found.
                  </td>
                </tr>
              ) : (
                users.map((u) => (
                  <tr key={u.id}>
                    <td className="mono muted">#{u.id}</td>
                    <td><b>{u.username}</b></td>
                    <td>
                      <span className={`badge ${
                        u.userType === 'Admin' ? 'accent' : u.userType === 'Service' ? 'purple' : 'info'
                      }`}>
                        {u.userType}
                      </span>
                    </td>
                    <td>{u.firstName} {u.lastName}</td>
                    <td>
                      <span className={`badge ${u.isActive ? 'ok' : 'err'}`}>
                        {u.isActive ? 'Active' : 'Disabled'}
                      </span>
                    </td>
                    <td className="muted">{formatDate(u.lastLogin)}</td>
                    {isAdmin && (
                      <td className="num">
                        <div className="actions">
                          <button className="sm" title="Edit user" onClick={() => openEditModal(u)}>
                            <Icon name="edit" size={13} /> Edit
                          </button>
                          <button className="sm" title="Reset password" onClick={() => openPasswordModal(u)}>
                            <Icon name="key" size={13} /> Password
                          </button>
                          {u.id !== currentUser?.id && (
                            <button className="danger sm" title="Delete user" onClick={() => handleDeleteUser(u)}>
                              <Icon name="trash" size={13} />
                            </button>
                          )}
                        </div>
                      </td>
                    )}
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {totalPages > 1 && (
          <div className="row" style={{ padding: '10px 16px', borderTop: '1px solid var(--border)', justifyContent: 'space-between' }}>
            <span className="muted" style={{ fontSize: 12.5 }}>
              Page {pageNumber} of {totalPages} ({totalRecords} accounts)
            </span>
            <div className="row" style={{ gap: 6 }}>
              <button
                className="sm"
                disabled={pageNumber <= 1}
                onClick={() => {
                  const next = pageNumber - 1
                  setPageNumber(next)
                  loadUsers(next)
                }}
              >
                Previous
              </button>
              <button
                className="sm"
                disabled={pageNumber >= totalPages}
                onClick={() => {
                  const next = pageNumber + 1
                  setPageNumber(next)
                  loadUsers(next)
                }}
              >
                Next
              </button>
            </div>
          </div>
        )}
      </div>

      {modalMode && (
        <div className="modal-backdrop" onClick={() => setModalMode(null)}>
          <form className="modal" onClick={(e) => e.stopPropagation()} onSubmit={handleSubmit}>
            <div className="modal-head">
              <h2>
                {modalMode === 'add' && 'Create User / Service Account'}
                {modalMode === 'edit' && `Edit Account: ${selectedUser?.username}`}
                {modalMode === 'password' && `Reset Password: ${selectedUser?.username}`}
              </h2>
            </div>

            {modalMode === 'password' ? (
              <label className="field">
                New Password
                <input
                  type="password"
                  required
                  minLength={4}
                  autoFocus
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="Enter new password…"
                />
              </label>
            ) : (
              <>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                  <label className="field">
                    Username
                    <input
                      type="text"
                      required
                      value={username}
                      onChange={(e) => setUsername(e.target.value)}
                      placeholder="ci-deployer"
                    />
                  </label>
                  <label className="field">
                    Role
                    <select value={userTypeId} onChange={(e) => setUserTypeId(e.target.value)}>
                      {userTypes.map((ut) => (
                        <option key={ut.id} value={ut.id}>{ut.type}</option>
                      ))}
                    </select>
                  </label>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                  <label className="field">
                    First Name
                    <input
                      type="text"
                      required
                      value={firstName}
                      onChange={(e) => setFirstName(e.target.value)}
                    />
                  </label>
                  <label className="field">
                    Last Name
                    <input
                      type="text"
                      required
                      value={lastName}
                      onChange={(e) => setLastName(e.target.value)}
                    />
                  </label>
                </div>

                {modalMode === 'add' && (
                  <label className="field">
                    Password
                    <input
                      type="password"
                      required
                      minLength={4}
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                    />
                  </label>
                )}

                {modalMode === 'edit' && (
                  <label className="row" style={{ gap: 8, cursor: 'pointer' }}>
                    <input
                      type="checkbox"
                      checked={isActive}
                      onChange={(e) => setIsActive(e.target.checked)}
                      style={{ width: 16, height: 16 }}
                    />
                    <span>Account is Active (allowed to authenticate)</span>
                  </label>
                )}
              </>
            )}

            <div className="modal-foot">
              <button type="button" className="ghost" onClick={() => setModalMode(null)}>
                Cancel
              </button>
              <button type="submit" className="primary" disabled={saving}>
                {saving ? <span className="spin" /> : 'Save'}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  )
}
