import { useEffect, useState } from 'react'
import { api, formatDate } from '../api'
import Icon from '../components/Icon'

export default function RetentionPoliciesPage({ notify }) {
  const [policies, setPolicies] = useState([])
  const [loading, setLoading] = useState(true)

  const [searchBy, setSearchBy] = useState('0')
  const [filterValue, setFilterValue] = useState('')

  const [modalOpen, setModalOpen] = useState(false)
  const [editingPolicy, setEditingPolicy] = useState(null)

  const [name, setName] = useState('')
  const [repositoryPattern, setRepositoryPattern] = useState('*')
  const [keepLastCount, setKeepLastCount] = useState('10')
  const [maxAgeDays, setMaxAgeDays] = useState('')
  const [protectedTagPatterns, setProtectedTagPatterns] = useState('latest,prod*,v*')
  const [runIntervalHours, setRunIntervalHours] = useState('24')
  const [isEnabled, setIsEnabled] = useState(true)
  const [saving, setSaving] = useState(false)

  const [runningId, setRunningId] = useState(null)
  const [executionReport, setExecutionReport] = useState(null)

  async function loadPolicies() {
    setLoading(true)
    try {
      const res = await api.get('/api/v1/RetentionPolicy')
      setPolicies(res || [])
    } catch (err) {
      notify(err.message, 'err')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadPolicies()
  }, [])

  function openCreateModal() {
    setEditingPolicy(null)
    setName('')
    setRepositoryPattern('*')
    setKeepLastCount('10')
    setMaxAgeDays('')
    setProtectedTagPatterns('latest,prod*,v*')
    setRunIntervalHours('24')
    setIsEnabled(true)
    setModalOpen(true)
  }

  function openEditModal(policy) {
    setEditingPolicy(policy)
    setName(policy.name)
    setRepositoryPattern(policy.repositoryPattern)
    setKeepLastCount(policy.keepLastCount != null ? String(policy.keepLastCount) : '')
    setMaxAgeDays(policy.maxAgeDays != null ? String(policy.maxAgeDays) : '')
    setProtectedTagPatterns(policy.protectedTagPatterns || '')
    setRunIntervalHours(policy.runIntervalHours != null ? String(policy.runIntervalHours) : '')
    setIsEnabled(policy.isEnabled)
    setModalOpen(true)
  }

  async function handleSavePolicy(e) {
    e.preventDefault()
    setSaving(true)
    try {
      const payload = {
        name: name.trim(),
        repositoryPattern: repositoryPattern.trim().toLowerCase(),
        keepLastCount: keepLastCount !== '' ? Number(keepLastCount) : null,
        maxAgeDays: maxAgeDays !== '' ? Number(maxAgeDays) : null,
        protectedTagPatterns: protectedTagPatterns.trim(),
        runIntervalHours: runIntervalHours !== '' ? Number(runIntervalHours) : null,
        isEnabled
      }

      if (editingPolicy) {
        await api.put(`/api/v1/RetentionPolicy/${editingPolicy.id}`, payload)
        notify(`Retention policy "${payload.name}" updated.`, 'ok')
      } else {
        await api.post('/api/v1/RetentionPolicy', payload)
        notify(`Retention policy "${payload.name}" created.`, 'ok')
      }
      setModalOpen(false)
      await loadPolicies()
    } catch (err) {
      notify(err.message, 'err')
    } finally {
      setSaving(false)
    }
  }

  async function handleDeletePolicy(policy) {
    if (!window.confirm(`Delete retention policy "${policy.name}"?`)) {
      return
    }
    try {
      await api.del(`/api/v1/RetentionPolicy/${policy.id}`)
      notify(`Retention policy "${policy.name}" deleted.`, 'ok')
      await loadPolicies()
    } catch (err) {
      notify(err.message, 'err')
    }
  }

  async function handleRunPolicy(policy, dryRun) {
    if (!dryRun && !window.confirm(`Execute retention policy "${policy.name}" now? Matching expired manifests will be permanently deleted from the registry.`)) {
      return
    }
    setRunningId(policy.id)
    try {
      const report = await api.post(`/api/v1/RetentionPolicy/${policy.id}/run?dryRun=${dryRun}`)
      setExecutionReport(report)
      if (!dryRun) {
        notify(`Executed "${policy.name}": deleted ${report.deletedTagsCount} tag(s) (${report.deletedDigestsCount} digest(s)).`, 'ok')
        await loadPolicies()
      }
    } catch (err) {
      notify(err.message, 'err')
    } finally {
      setRunningId(null)
    }
  }

  const filteredPolicies = policies.filter((p) => {
    if (searchBy === '0' || !filterValue.trim()) return true
    const val = filterValue.trim().toLowerCase()
    if (searchBy === 'name') return p.name.toLowerCase().includes(val)
    if (searchBy === 'pattern') return p.repositoryPattern.toLowerCase().includes(val)
    if (searchBy === 'status') return (val === 'enabled') === p.isEnabled
    return true
  })

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Retention Policies</h1>
        </div>
        <button className="primary" onClick={openCreateModal}>
          <Icon name="plus" size={15} />
          New Retention Policy
        </button>
      </div>

      <div className="card" style={{ marginBottom: 14, padding: '12px 16px' }}>
        <form
          className="row"
          onSubmit={(e) => e.preventDefault()}
        >
          <select
            value={searchBy}
            onChange={(e) => {
              setSearchBy(e.target.value)
              setFilterValue('')
            }}
            style={{ width: 195 }}
          >
            <option value="0">All Fields (No Filter)</option>
            <option value="name">By Policy Name</option>
            <option value="pattern">By Repository Pattern</option>
            <option value="status">By Status</option>
          </select>

          {searchBy === 'status' ? (
            <select
              value={filterValue}
              onChange={(e) => setFilterValue(e.target.value)}
              style={{ width: 220 }}
            >
              <option value="">Select status…</option>
              <option value="enabled">Enabled</option>
              <option value="disabled">Disabled</option>
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
                <th>Policy Name</th>
                <th>Repo Pattern</th>
                <th>Retention Rules</th>
                <th>Protected Tags</th>
                <th>Schedule</th>
                <th>Status</th>
                <th>Last Run</th>
                <th className="num">Actions</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr>
                  <td colSpan={9} className="empty">
                    <span className="spin" /> Loading retention policies…
                  </td>
                </tr>
              ) : filteredPolicies.length === 0 ? (
                <tr>
                  <td colSpan={9} className="empty">
                    No retention policies found.
                  </td>
                </tr>
              ) : (
                filteredPolicies.map((p) => (
                  <tr key={p.id}>
                    <td className="mono muted">#{p.id}</td>
                    <td><b>{p.name}</b></td>
                    <td><code>{p.repositoryPattern}</code></td>
                    <td>
                      <div className="row" style={{ gap: 6 }}>
                        {p.keepLastCount != null && (
                          <span className="badge info">Keep last {p.keepLastCount}</span>
                        )}
                        {p.maxAgeDays != null && (
                          <span className="badge accent">Max age {p.maxAgeDays}d</span>
                        )}
                      </div>
                    </td>
                    <td>
                      {p.protectedTagPatterns ? (
                        <code>{p.protectedTagPatterns}</code>
                      ) : (
                        <span className="muted">None</span>
                      )}
                    </td>
                    <td>
                      {p.runIntervalHours ? (
                        <span className="badge purple">
                          {p.runIntervalHours === 1
                            ? 'Hourly'
                            : p.runIntervalHours === 24
                              ? 'Daily'
                              : p.runIntervalHours === 168
                                ? 'Weekly'
                                : p.runIntervalHours === 720
                                  ? 'Monthly'
                                  : `Every ${p.runIntervalHours}h`}
                        </span>
                      ) : (
                        <span className="muted">Manual Only</span>
                      )}
                    </td>
                    <td>
                      <span className={`badge ${p.isEnabled ? 'ok' : 'err'}`}>
                        {p.isEnabled ? 'Enabled' : 'Disabled'}
                      </span>
                    </td>
                    <td className="muted">
                      {p.lastRunAt ? (
                        <span>{formatDate(p.lastRunAt)} ({p.lastDeletedTagsCount} deleted)</span>
                      ) : (
                        'Never'
                      )}
                    </td>
                    <td className="num">
                      <div className="actions">
                        <button
                          className="sm"
                          disabled={runningId === p.id}
                          title="Dry-Run Preview (no deletions)"
                          onClick={() => handleRunPolicy(p, true)}
                        >
                          {runningId === p.id ? <span className="spin" /> : 'Dry-Run'}
                        </button>
                        <button
                          className="primary sm"
                          disabled={runningId === p.id}
                          title="Execute retention policy now"
                          onClick={() => handleRunPolicy(p, false)}
                        >
                          Run Now
                        </button>
                        <button className="sm" onClick={() => openEditModal(p)}>
                          <Icon name="edit" size={13} /> Edit
                        </button>
                        <button className="danger sm" onClick={() => handleDeletePolicy(p)}>
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
          <form className="modal" onClick={(e) => e.stopPropagation()} onSubmit={handleSavePolicy}>
            <div className="modal-head">
              <h2>{editingPolicy ? `Edit Policy: ${editingPolicy.name}` : 'Create Retention Policy'}</h2>
            </div>

            <label className="field">
              Policy Name
              <input
                type="text"
                required
                autoFocus
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="CI Ephemeral Tag Cleanup"
              />
            </label>

            <label className="field">
              Repository Pattern
              <input
                type="text"
                required
                value={repositoryPattern}
                onChange={(e) => setRepositoryPattern(e.target.value)}
                placeholder="*, ci/*, or mobyguard/backend"
              />
              <span className="field-hint">
                Use <code>*</code> for all repositories, <code>namespace/*</code> for a namespace, or an exact repository name.
              </span>
            </label>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
              <label className="field">
                Keep Last N Tags (Optional)
                <input
                  type="number"
                  min={1}
                  value={keepLastCount}
                  onChange={(e) => setKeepLastCount(e.target.value)}
                  placeholder="e.g. 10"
                />
                <span className="field-hint">Keep the newest N non-protected tags per repository.</span>
              </label>

              <label className="field">
                Max Age in Days (Optional)
                <input
                  type="number"
                  min={1}
                  value={maxAgeDays}
                  onChange={(e) => setMaxAgeDays(e.target.value)}
                  placeholder="e.g. 30"
                />
                <span className="field-hint">Delete non-protected tags older than X days.</span>
              </label>
            </div>

            <label className="field">
              Protected Tag Patterns (Comma-Separated)
              <input
                type="text"
                value={protectedTagPatterns}
                onChange={(e) => setProtectedTagPatterns(e.target.value)}
                placeholder="latest,prod*,v*"
              />
              <span className="field-hint">
                Tags matching these patterns (and any shared digests they reference) are never deleted.
              </span>
            </label>

            <label className="field">
              Automatic Execution Schedule
              <select
                value={runIntervalHours}
                onChange={(e) => setRunIntervalHours(e.target.value)}
              >
                <option value="">Manual Only (Run on-demand)</option>
                <option value="1">Hourly</option>
                <option value="24">Daily</option>
                <option value="168">Weekly</option>
                <option value="720">Monthly</option>
              </select>
            </label>

            <label className="row" style={{ gap: 8, cursor: 'pointer' }}>
              <input
                type="checkbox"
                checked={isEnabled}
                onChange={(e) => setIsEnabled(e.target.checked)}
                style={{ width: 16, height: 16 }}
              />
              <span>Policy is Enabled</span>
            </label>

            <div className="modal-foot">
              <button type="button" className="ghost" onClick={() => setModalOpen(false)}>
                Cancel
              </button>
              <button type="submit" className="primary" disabled={saving}>
                {saving ? <span className="spin" /> : (editingPolicy ? 'Save Changes' : 'Create Policy')}
              </button>
            </div>
          </form>
        </div>
      )}

      {executionReport && (
        <div className="modal-backdrop" onClick={() => setExecutionReport(null)}>
          <div
            className="modal"
            style={{ maxWidth: 780, width: '95%' }}
            onClick={(e) => e.stopPropagation()}
          >
            <div className="modal-head">
              <h2>
                {executionReport.dryRun ? 'Dry-Run Preview' : 'Execution Report'}: {executionReport.policyName}
              </h2>
            </div>

            <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
              <span className="badge info">Matched Repos: {executionReport.matchedRepositoriesCount}</span>
              <span className="badge info">Evaluated Tags: {executionReport.evaluatedTagsCount}</span>
              <span className="badge ok">Protected Tags: {executionReport.protectedTagsCount}</span>
              <span className="badge err">
                {executionReport.dryRun ? 'Would Delete' : 'Deleted'}: {executionReport.deletedTagsCount} tag(s) ({executionReport.deletedDigestsCount} digest(s))
              </span>
              {executionReport.skippedSharedDigestTagsCount > 0 && (
                <span className="badge accent">
                  Skipped (Shared Digest): {executionReport.skippedSharedDigestTagsCount}
                </span>
              )}
            </div>

            <div className="table-wrap" style={{ maxHeight: 340, overflowY: 'auto', border: '1px solid var(--border)', borderRadius: 'var(--r-sm)' }}>
              <table>
                <thead>
                  <tr>
                    <th>Repository:Tag</th>
                    <th>Digest</th>
                    <th>Created</th>
                    <th>Outcome</th>
                    <th>Reason</th>
                  </tr>
                </thead>
                <tbody>
                  {executionReport.items?.length === 0 ? (
                    <tr>
                      <td colSpan={5} className="empty">No tags evaluated.</td>
                    </tr>
                  ) : (
                    executionReport.items.map((item, idx) => (
                      <tr key={idx}>
                        <td><code>{item.repository}:{item.tag}</code></td>
                        <td className="mono muted">{item.digest ? `${item.digest.slice(0, 19)}…` : '—'}</td>
                        <td className="muted">{formatDate(item.createdAt)}</td>
                        <td>
                          <span className={`badge ${
                            item.action === 'Deleted' || item.action === 'WouldDelete'
                              ? 'err'
                              : item.action === 'SkippedSharedDigest'
                                ? 'accent'
                                : 'ok'
                          }`}>
                            {item.action}
                          </span>
                        </td>
                        <td className="muted" style={{ fontSize: 12 }}>{item.reason}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            <div className="modal-foot">
              <button type="button" className="ghost" onClick={() => setExecutionReport(null)}>
                Close
              </button>
              {executionReport.dryRun && executionReport.deletedTagsCount > 0 && (
                <button
                  type="button"
                  className="primary"
                  onClick={() => {
                    const target = policies.find((p) => p.id === executionReport.policyId)
                    setExecutionReport(null)
                    if (target) {
                      handleRunPolicy(target, false)
                    }
                  }}
                >
                  Execute Policy Now
                </button>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
