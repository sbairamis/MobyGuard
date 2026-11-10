import { useEffect, useState } from 'react'
import { api, formatDate } from '../api'
import Icon from '../components/Icon'

export default function PermissionsPage({ user, notify }) {
  const isAdmin = user?.userType === 'Admin'

  const [permissions, setPermissions] = useState([])
  const [totalRecords, setTotalRecords] = useState(0)
  const [pageNumber, setPageNumber] = useState(1)
  const [pageSize] = useState(15)

  const [filterUsername, setFilterUsername] = useState('')
  const [filterPattern, setFilterPattern] = useState('')
  const [loading, setLoading] = useState(true)

  const [usersList, setUsersList] = useState([])
  const [modalOpen, setModalOpen] = useState(false)
  const [editingRule, setEditingRule] = useState(null)

  const [formUserId, setFormUserId] = useState('')
  const [formPattern, setFormPattern] = useState('')
  const [formPull, setFormPull] = useState(true)
  const [formPush, setFormPush] = useState(false)
  const [formDelete, setFormDelete] = useState(false)
  const [saving, setSaving] = useState(false)

  async function loadPermissions(page = pageNumber) {
    setLoading(true)
    try {
      if (!isAdmin) {
        const myPerms = await api.get('/api/RepositoryPermission/me')
        setPermissions(myPerms || [])
        setTotalRecords(myPerms?.length || 0)
      } else {
        const params = new URLSearchParams({
          PageNumber: String(page),
          PageSize: String(pageSize)
        })
        if (filterUsername.trim()) params.set('Username', filterUsername.trim())
        if (filterPattern.trim()) params.set('RepositoryPattern', filterPattern.trim())

        const res = await api.get(`/api/RepositoryPermission?${params.toString()}`)
        setPermissions(res?.data || [])
        setTotalRecords(res?.totalRecords || 0)
      }
    } catch (err) {
      notify(err.message, 'err')
    } finally {
      setLoading(false)
    }
  }

  async function loadUsersForDropdown() {
    if (!isAdmin) return
    try {
      const res = await api.get('/api/User?PageNumber=1&PageSize=100')
      const nonAdmins = (res?.data || []).filter((u) => u.userType !== 'Admin')
      setUsersList(nonAdmins)
    } catch {}
  }

  useEffect(() => {
    loadPermissions(1)
    loadUsersForDropdown()
  }, [])

  function openAddModal() {
    setEditingRule(null)
    setFormUserId(usersList[0]?.id ? String(usersList[0].id) : '')
    setFormPattern('mobyguard/*')
    setFormPull(true)
    setFormPush(false)
    setFormDelete(false)
    setModalOpen(true)
  }

  function openEditModal(rule) {
    setEditingRule(rule)
    setFormUserId(String(rule.userId))
    setFormPattern(rule.repositoryPattern)
    setFormPull(rule.canPull)
    setFormPush(rule.canPush)
    setFormDelete(rule.canDelete)
    setModalOpen(true)
  }

  async function handleSaveRule(e) {
    e.preventDefault()
    setSaving(true)
    try {
      if (editingRule) {
        await api.put(`/api/RepositoryPermission/${editingRule.id}`, {
          repositoryPattern: formPattern.trim().toLowerCase(),
          canPull: formPull,
          canPush: formPush,
          canDelete: formDelete
        })
        notify('Permission policy updated.', 'ok')
      } else {
        await api.post('/api/RepositoryPermission', {
          userId: Number(formUserId),
          repositoryPattern: formPattern.trim().toLowerCase(),
          canPull: formPull,
          canPush: formPush,
          canDelete: formDelete
        })
        notify('Permission policy created.', 'ok')
      }
      setModalOpen(false)
      await loadPermissions(pageNumber)
    } catch (err) {
      notify(err.message, 'err')
    } finally {
      setSaving(false)
    }
  }

  async function handleDeleteRule(rule) {
    if (!window.confirm(`Delete permission rule "${rule.repositoryPattern}" for user "${rule.username}"?`)) {
      return
    }
    try {
      await api.del(`/api/RepositoryPermission/${rule.id}`)
      notify('Permission policy deleted.', 'ok')
      await loadPermissions(pageNumber)
    } catch (err) {
      notify(err.message, 'err')
    }
  }

  const totalPages = Math.max(1, Math.ceil(totalRecords / pageSize))

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Repository Permissions (Policies)</h1>
          <p className="sub">
            {isAdmin
              ? 'Define fine-grained pull, push, and delete permissions per user using exact names ("mobyguard/app"), namespace wildcards ("mobyguard/*"), or global "*"'
              : 'Your assigned repository access policies'}
          </p>
        </div>
        {isAdmin && (
          <button className="primary" onClick={openAddModal}>
            <Icon name="plus" size={15} />
            New Permission Policy
          </button>
        )}
      </div>

      {isAdmin && (
        <div className="card" style={{ marginBottom: 14, padding: '12px 16px' }}>
          <form
            className="row"
            onSubmit={(e) => {
              e.preventDefault()
              setPageNumber(1)
              loadPermissions(1)
            }}
          >
            <input
              type="text"
              placeholder="Filter by username…"
              value={filterUsername}
              onChange={(e) => setFilterUsername(e.target.value)}
              style={{ width: 220 }}
            />
            <input
              type="text"
              placeholder="Filter by pattern (e.g. mobyguard/*)…"
              value={filterPattern}
              onChange={(e) => setFilterPattern(e.target.value)}
              style={{ width: 260 }}
            />
            <button type="submit">
              <Icon name="search" size={14} /> Search
            </button>
            {(filterUsername || filterPattern) && (
              <button
                type="button"
                className="ghost"
                onClick={() => {
                  setFilterUsername('')
                  setFilterPattern('')
                  setPageNumber(1)
                  setTimeout(() => loadPermissions(1), 0)
                }}
              >
                Clear
              </button>
            )}
          </form>
        </div>
      )}

      <div className="card tight">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>ID</th>
                <th>User</th>
                <th>Repository Pattern</th>
                <th>Allowed Actions</th>
                <th>Updated</th>
                {isAdmin && <th className="num">Manage</th>}
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr>
                  <td colSpan={isAdmin ? 6 : 5} className="empty">
                    <span className="spin" /> Loading policies…
                  </td>
                </tr>
              ) : permissions.length === 0 ? (
                <tr>
                  <td colSpan={isAdmin ? 6 : 5} className="empty">
                    No repository permission policies found.
                  </td>
                </tr>
              ) : (
                permissions.map((rule) => (
                  <tr key={rule.id}>
                    <td className="muted mono">#{rule.id}</td>
                    <td><b>{rule.username}</b></td>
                    <td>
                      <code style={{ fontSize: 13 }}>{rule.repositoryPattern}</code>
                      {rule.repositoryPattern === '*' && (
                        <span className="badge purple" style={{ marginLeft: 8 }}>Global + Catalog</span>
                      )}
                      {rule.repositoryPattern.endsWith('/*') && (
                        <span className="badge accent" style={{ marginLeft: 8 }}>Namespace Wildcard</span>
                      )}
                    </td>
                    <td>
                      <div className="row" style={{ gap: 5 }}>
                        {rule.canPull && <span className="badge info">pull</span>}
                        {rule.canPush && <span className="badge ok">push</span>}
                        {rule.canDelete && <span className="badge err">delete</span>}
                      </div>
                    </td>
                    <td className="muted">{formatDate(rule.updatedAt)}</td>
                    {isAdmin && (
                      <td className="num">
                        <div className="actions">
                          <button className="sm" onClick={() => openEditModal(rule)}>
                            <Icon name="edit" size={13} /> Edit
                          </button>
                          <button className="danger sm" onClick={() => handleDeleteRule(rule)}>
                            <Icon name="trash" size={13} />
                          </button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {isAdmin && totalPages > 1 && (
          <div className="row" style={{ padding: '10px 16px', borderTop: '1px solid var(--border)', justifyContent: 'space-between' }}>
            <span className="muted" style={{ fontSize: 12.5 }}>
              Page {pageNumber} of {totalPages} ({totalRecords} policies)
            </span>
            <div className="row" style={{ gap: 6 }}>
              <button
                className="sm"
                disabled={pageNumber <= 1}
                onClick={() => {
                  const next = pageNumber - 1
                  setPageNumber(next)
                  loadPermissions(next)
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
                  loadPermissions(next)
                }}
              >
                Next
              </button>
            </div>
          </div>
        )}
      </div>

      {modalOpen && (
        <div className="modal-backdrop" onClick={() => setModalOpen(false)}>
          <form className="modal" onClick={(e) => e.stopPropagation()} onSubmit={handleSaveRule}>
            <div className="modal-head">
              <h2>{editingRule ? `Edit Policy #${editingRule.id}` : 'Create Repository Permission Policy'}</h2>
            </div>

            {!editingRule ? (
              <label className="field">
                User / Service Account
                <select
                  required
                  value={formUserId}
                  onChange={(e) => setFormUserId(e.target.value)}
                >
                  <option value="" disabled>Select user…</option>
                  {usersList.map((u) => (
                    <option key={u.id} value={u.id}>
                      {u.username} ({u.userType} — {u.firstName} {u.lastName})
                    </option>
                  ))}
                </select>
                <span className="field-hint">Note: Admin accounts automatically have full access to all repositories.</span>
              </label>
            ) : (
              <div className="field">
                User
                <input type="text" value={editingRule.username} disabled />
              </div>
            )}

            <label className="field">
              Repository Pattern
              <input
                type="text"
                required
                value={formPattern}
                onChange={(e) => setFormPattern(e.target.value)}
                placeholder="mobyguard/iot-api, mobyguard/*, or *"
              />
              <span className="field-hint">
                Examples: <code>mobyguard/iot-api</code> (exact repo), <code>mobyguard/*</code> (entire namespace), or <code>*</code> (all repos + catalog).
              </span>
            </label>

            <div className="field">
              Allowed Registry Actions
              <div className="row" style={{ gap: 16, marginTop: 4 }}>
                <label className="row" style={{ gap: 6, cursor: 'pointer', color: 'var(--text)' }}>
                  <input
                    type="checkbox"
                    checked={formPull}
                    onChange={(e) => setFormPull(e.target.checked)}
                    style={{ width: 16, height: 16 }}
                  />
                  <b>Pull</b> <span className="muted">(read/download)</span>
                </label>

                <label className="row" style={{ gap: 6, cursor: 'pointer', color: 'var(--text)' }}>
                  <input
                    type="checkbox"
                    checked={formPush}
                    onChange={(e) => setFormPush(e.target.checked)}
                    style={{ width: 16, height: 16 }}
                  />
                  <b>Push</b> <span className="muted">(upload tags/blobs)</span>
                </label>

                <label className="row" style={{ gap: 6, cursor: 'pointer', color: 'var(--text)' }}>
                  <input
                    type="checkbox"
                    checked={formDelete}
                    onChange={(e) => setFormDelete(e.target.checked)}
                    style={{ width: 16, height: 16 }}
                  />
                  <b>Delete</b> <span className="muted">(remove manifests)</span>
                </label>
              </div>
            </div>

            <div className="modal-foot">
              <button type="button" className="ghost" onClick={() => setModalOpen(false)}>
                Cancel
              </button>
              <button type="submit" className="primary" disabled={saving}>
                {saving ? <span className="spin" /> : (editingRule ? 'Save Changes' : 'Create Policy')}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  )
}
