import { useEffect, useState } from 'react'
import { api, formatDate } from '../api'
import Icon from '../components/Icon'

export default function WebhooksPage({ notify }) {
  const [subscriptions, setSubscriptions] = useState([])
  const [loading, setLoading] = useState(true)

  const [searchBy, setSearchBy] = useState('0')
  const [filterValue, setFilterValue] = useState('')

  const [modalOpen, setModalOpen] = useState(false)
  const [editingSub, setEditingSub] = useState(null)

  const [name, setName] = useState('')
  const [provider, setProvider] = useState('Slack')
  const [targetUrl, setTargetUrl] = useState('')
  const [repositoryPattern, setRepositoryPattern] = useState('*')
  const [notifyOnPush, setNotifyOnPush] = useState(true)
  const [notifyOnDelete, setNotifyOnDelete] = useState(true)
  const [notifyOnRetention, setNotifyOnRetention] = useState(true)
  const [isEnabled, setIsEnabled] = useState(true)
  const [saving, setSaving] = useState(false)

  const [testingId, setTestingId] = useState(null)

  async function loadSubscriptions() {
    setLoading(true)
    try {
      const res = await api.get('/api/v1/WebhookSubscription')
      setSubscriptions(res || [])
    } catch (err) {
      notify(err.message, 'err')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadSubscriptions()
  }, [])

  function openCreateModal() {
    setEditingSub(null)
    setName('')
    setProvider('Slack')
    setTargetUrl('')
    setRepositoryPattern('*')
    setNotifyOnPush(true)
    setNotifyOnDelete(true)
    setNotifyOnRetention(true)
    setIsEnabled(true)
    setModalOpen(true)
  }

  function openEditModal(sub) {
    setEditingSub(sub)
    setName(sub.name)
    setProvider(sub.provider)
    setTargetUrl(sub.targetUrl)
    setRepositoryPattern(sub.repositoryPattern)
    setNotifyOnPush(sub.notifyOnPush)
    setNotifyOnDelete(sub.notifyOnDelete)
    setNotifyOnRetention(sub.notifyOnRetention)
    setIsEnabled(sub.isEnabled)
    setModalOpen(true)
  }

  async function handleSave(e) {
    e.preventDefault()
    setSaving(true)
    try {
      const payload = {
        name: name.trim(),
        provider,
        targetUrl: targetUrl.trim(),
        repositoryPattern: repositoryPattern.trim().toLowerCase(),
        notifyOnPush,
        notifyOnDelete,
        notifyOnRetention,
        isEnabled
      }

      if (editingSub) {
        await api.put(`/api/v1/WebhookSubscription/${editingSub.id}`, payload)
        notify(`Webhook "${payload.name}" updated.`, 'ok')
      } else {
        await api.post('/api/v1/WebhookSubscription', payload)
        notify(`Webhook "${payload.name}" created.`, 'ok')
      }

      setModalOpen(false)
      await loadSubscriptions()
    } catch (err) {
      notify(err.message, 'err')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete(sub) {
    if (!window.confirm(`Delete webhook subscription "${sub.name}"?`)) {
      return
    }
    try {
      await api.del(`/api/v1/WebhookSubscription/${sub.id}`)
      notify(`Webhook "${sub.name}" deleted.`, 'ok')
      await loadSubscriptions()
    } catch (err) {
      notify(err.message, 'err')
    }
  }

  async function handleTest(sub) {
    setTestingId(sub.id)
    try {
      const res = await api.post(`/api/v1/WebhookSubscription/${sub.id}/test`)
      if (res?.success) {
        notify(`Test notification delivered to "${sub.name}" (${res.provider}, HTTP ${res.statusCode}).`, 'ok')
      } else {
        notify(`Delivery failed for "${sub.name}" (HTTP ${res?.statusCode || 0}): ${res?.responseBody || 'Error'}`, 'err')
      }
      await loadSubscriptions()
    } catch (err) {
      notify(err.message, 'err')
    } finally {
      setTestingId(null)
    }
  }

  const filtered = subscriptions.filter((sub) => {
    if (searchBy === '0' || !filterValue.trim()) return true
    const val = filterValue.trim().toLowerCase()
    if (searchBy === 'name') return sub.name.toLowerCase().includes(val)
    if (searchBy === 'provider') return sub.provider.toLowerCase() === val
    if (searchBy === 'pattern') return sub.repositoryPattern.toLowerCase().includes(val)
    return true
  })

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Outbound Webhooks</h1>
        </div>
        <button className="primary" onClick={openCreateModal}>
          <Icon name="plus" size={15} />
          New Webhook
        </button>
      </div>

      <div className="card" style={{ marginBottom: 14, padding: '12px 16px' }}>
        <form className="row" onSubmit={(e) => e.preventDefault()}>
          <select
            value={searchBy}
            onChange={(e) => {
              setSearchBy(e.target.value)
              setFilterValue('')
            }}
            style={{ width: 195 }}
          >
            <option value="0">All Fields (No Filter)</option>
            <option value="name">By Webhook Name</option>
            <option value="provider">By Provider</option>
            <option value="pattern">By Repository Pattern</option>
          </select>

          {searchBy === 'provider' ? (
            <select
              value={filterValue}
              onChange={(e) => setFilterValue(e.target.value)}
              style={{ width: 220 }}
            >
              <option value="">Select provider…</option>
              <option value="slack">Slack</option>
              <option value="discord">Discord</option>
              <option value="teams">Microsoft Teams</option>
            </select>
          ) : (
            <input
              type="text"
              disabled={searchBy === '0'}
              placeholder={searchBy === '0' ? 'Select a filter field first…' : 'Enter search value…'}
              value={filterValue}
              onChange={(e) => setFilterValue(e.target.value)}
              style={{ width: 260 }}
            />
          )}

          {searchBy !== '0' && (
            <button
              type="button"
              className="ghost"
              onClick={() => {
                setSearchBy('0')
                setFilterValue('')
              }}
            >
              Clear
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
                <th>Name</th>
                <th>Provider</th>
                <th>Repo Pattern</th>
                <th>Triggers</th>
                <th>Status</th>
                <th>Last Delivery</th>
                <th className="num">Actions</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr>
                  <td colSpan={8} className="empty">
                    <span className="spin" /> Loading webhooks…
                  </td>
                </tr>
              ) : filtered.length === 0 ? (
                <tr>
                  <td colSpan={8} className="empty">
                    No outbound webhooks configured.
                  </td>
                </tr>
              ) : (
                filtered.map((sub) => (
                  <tr key={sub.id}>
                    <td className="mono muted">#{sub.id}</td>
                    <td><b>{sub.name}</b></td>
                    <td>
                      <span className={`badge ${
                        sub.provider === 'Slack' ? 'accent' : sub.provider === 'Discord' ? 'purple' : 'info'
                      }`}>
                        {sub.provider === 'Teams' ? 'Microsoft Teams' : sub.provider}
                      </span>
                    </td>
                    <td><code>{sub.repositoryPattern}</code></td>
                    <td>
                      <div className="row" style={{ gap: 5 }}>
                        {sub.notifyOnPush && <span className="badge ok">push</span>}
                        {sub.notifyOnDelete && <span className="badge err">delete</span>}
                        {sub.notifyOnRetention && <span className="badge info">retention</span>}
                      </div>
                    </td>
                    <td>
                      <span className={`badge ${sub.isEnabled ? 'ok' : 'err'}`}>
                        {sub.isEnabled ? 'Enabled' : 'Disabled'}
                      </span>
                    </td>
                    <td className="muted">
                      {sub.lastTriggeredAt ? (
                        <span>
                          {formatDate(sub.lastTriggeredAt)}{' '}
                          <span className={`badge ${sub.lastStatusCode >= 200 && sub.lastStatusCode < 300 ? 'ok' : 'err'}`}>
                            {sub.lastStatusCode ? `HTTP ${sub.lastStatusCode}` : 'Failed'}
                          </span>
                        </span>
                      ) : (
                        'Never'
                      )}
                    </td>
                    <td className="num">
                      <div className="actions">
                        <button
                          className="primary sm"
                          disabled={testingId === sub.id}
                          onClick={() => handleTest(sub)}
                        >
                          {testingId === sub.id ? <span className="spin" /> : 'Test'}
                        </button>
                        <button className="sm" onClick={() => openEditModal(sub)}>
                          <Icon name="edit" size={13} /> Edit
                        </button>
                        <button className="danger sm" onClick={() => handleDelete(sub)}>
                          <Icon name="trash" size={13} />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      {modalOpen && (
        <div className="modal-backdrop" onClick={() => setModalOpen(false)}>
          <form className="modal" onClick={(e) => e.stopPropagation()} onSubmit={handleSave}>
            <div className="modal-head">
              <h2>{editingSub ? `Edit Webhook: ${editingSub.name}` : 'Create Outbound Webhook'}</h2>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
              <label className="field">
                Webhook Name
                <input
                  type="text"
                  required
                  autoFocus
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="DevOps Slack Channel"
                />
              </label>

              <label className="field">
                Provider Format
                <select value={provider} onChange={(e) => setProvider(e.target.value)}>
                  <option value="Slack">Slack (Block Kit)</option>
                  <option value="Discord">Discord (Rich Embed)</option>
                  <option value="Teams">Microsoft Teams (Adaptive Card)</option>
                </select>
              </label>
            </div>

            <label className="field">
              Incoming Webhook URL
              <input
                type="url"
                required
                value={targetUrl}
                onChange={(e) => setTargetUrl(e.target.value)}
                placeholder={
                  provider === 'Slack'
                    ? 'https://hooks.slack.com/services/T000.../B000.../XXXX...'
                    : provider === 'Discord'
                      ? 'https://discord.com/api/webhooks/123456789/abcdef...'
                      : 'https://prod-00.westus.logic.azure.com:443/workflows/...'
                }
              />
            </label>

            <label className="field">
              Repository Pattern
              <input
                type="text"
                required
                value={repositoryPattern}
                onChange={(e) => setRepositoryPattern(e.target.value)}
                placeholder="*, mobyguard/*, or mobyguard/api"
              />
            </label>

            <div className="field">
              Notification Triggers
              <div className="row" style={{ gap: 16, marginTop: 4 }}>
                <label className="row" style={{ gap: 6, cursor: 'pointer', color: 'var(--text)' }}>
                  <input
                    type="checkbox"
                    checked={notifyOnPush}
                    onChange={(e) => setNotifyOnPush(e.target.checked)}
                    style={{ width: 16, height: 16 }}
                  />
                  <b>Image Push</b>
                </label>

                <label className="row" style={{ gap: 6, cursor: 'pointer', color: 'var(--text)' }}>
                  <input
                    type="checkbox"
                    checked={notifyOnDelete}
                    onChange={(e) => setNotifyOnDelete(e.target.checked)}
                    style={{ width: 16, height: 16 }}
                  />
                  <b>Image Delete</b>
                </label>

                <label className="row" style={{ gap: 6, cursor: 'pointer', color: 'var(--text)' }}>
                  <input
                    type="checkbox"
                    checked={notifyOnRetention}
                    onChange={(e) => setNotifyOnRetention(e.target.checked)}
                    style={{ width: 16, height: 16 }}
                  />
                  <b>Retention Policy Run</b>
                </label>
              </div>
            </div>

            <label className="row" style={{ gap: 8, cursor: 'pointer' }}>
              <input
                type="checkbox"
                checked={isEnabled}
                onChange={(e) => setIsEnabled(e.target.checked)}
                style={{ width: 16, height: 16 }}
              />
              <span>Webhook is Enabled</span>
            </label>

            <div className="modal-foot">
              <button type="button" className="ghost" onClick={() => setModalOpen(false)}>
                Cancel
              </button>
              <button type="submit" className="primary" disabled={saving}>
                {saving ? <span className="spin" /> : (editingSub ? 'Save Changes' : 'Create Webhook')}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  )
}
