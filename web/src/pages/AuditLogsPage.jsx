import { useEffect, useState } from 'react'
import { api, formatDate } from '../api'
import Icon from '../components/Icon'

export default function AuditLogsPage({ notify }) {
  const [logs, setLogs] = useState([])
  const [totalRecords, setTotalRecords] = useState(0)
  const [pageNumber, setPageNumber] = useState(1)
  const [pageSize] = useState(20)
  const [loading, setLoading] = useState(true)

  const [searchBy, setSearchBy] = useState('0')
  const [valueStr, setValueStr] = useState('')
  const [descriptions, setDescriptions] = useState([])

  async function loadFilters() {
    try {
      const res = await api.get('/api/v1/AuditLog/filters')
      if (res?.valuesStr?.description) {
        setDescriptions(Object.keys(res.valuesStr.description))
      }
    } catch {}
  }

  async function loadLogs(page = pageNumber) {
    setLoading(true)
    try {
      const params = new URLSearchParams({
        PageNumber: String(page),
        PageSize: String(pageSize),
        SearchBy: searchBy
      })
      if (valueStr.trim()) {
        params.set('ValueStr', valueStr.trim())
      }

      const res = await api.get(`/api/v1/AuditLog?${params.toString()}`)
      setLogs(res?.data || [])
      setTotalRecords(res?.totalRecords || 0)
    } catch (err) {
      notify(err.message, 'err')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadFilters()
    loadLogs(1)
  }, [])

  const totalPages = Math.max(1, Math.ceil(totalRecords / pageSize))

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Audit Logs</h1>
        </div>
        <button onClick={() => loadLogs(pageNumber)} disabled={loading}>
          <Icon name="refresh" size={15} />
          Refresh
        </button>
      </div>

      <div className="card" style={{ marginBottom: 14, padding: '12px 16px' }}>
        <form
          className="row"
          onSubmit={(e) => {
            e.preventDefault()
            setPageNumber(1)
            loadLogs(1)
          }}
        >
          <select
            value={searchBy}
            onChange={(e) => {
              setSearchBy(e.target.value)
              setValueStr('')
            }}
            style={{ width: 175 }}
          >
            <option value="0">All Fields (No Filter)</option>
            <option value="1">By Username</option>
            <option value="2">By Repository</option>
            <option value="3">By Granted Actions</option>
            <option value="4">By Event Type</option>
            <option value="5">By Client IP</option>
          </select>

          {searchBy === '4' ? (
            <select
              value={valueStr}
              onChange={(e) => setValueStr(e.target.value)}
              style={{ width: 220 }}
            >
              <option value="">Select event type…</option>
              {descriptions.map((d) => (
                <option key={d} value={d}>{d}</option>
              ))}
            </select>
          ) : (
            <input
              type="text"
              disabled={searchBy === '0'}
              placeholder={searchBy === '0' ? 'Select a filter field first…' : 'Enter search value…'}
              value={valueStr}
              onChange={(e) => setValueStr(e.target.value)}
              style={{ width: 260 }}
            />
          )}

          <button type="submit">
            <Icon name="search" size={14} /> Search
          </button>

          {searchBy !== '0' && (
            <button
              type="button"
              className="ghost"
              onClick={() => {
                setSearchBy('0')
                setValueStr('')
                setPageNumber(1)
                setTimeout(() => loadLogs(1), 0)
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
                <th>Event</th>
                <th>Username</th>
                <th>Repository / Scope</th>
                <th>Actions</th>
                <th>Client IP</th>
                <th>Timestamp</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr>
                  <td colSpan={7} className="empty">
                    <span className="spin" /> Loading audit logs…
                  </td>
                </tr>
              ) : logs.length === 0 ? (
                <tr>
                  <td colSpan={7} className="empty">No matching audit logs found.</td>
                </tr>
              ) : (
                logs.map((log) => (
                  <tr key={log.id}>
                    <td className="mono muted">#{log.id}</td>
                    <td>
                      <span className={`badge ${
                        log.description === 'Manifest_Deleted' || log.description === 'Permission_Deleted'
                          ? 'err'
                          : log.description === 'Token_Issued'
                            ? 'info'
                            : 'ok'
                      }`}>
                        {log.description}
                      </span>
                    </td>
                    <td><b>{log.username}</b></td>
                    <td><code>{log.repository || '—'}</code></td>
                    <td>
                      {log.actions ? <span className="badge accent">{log.actions}</span> : '—'}
                    </td>
                    <td className="mono muted">{log.clientIp || '—'}</td>
                    <td className="muted">{formatDate(log.createdAt)}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {totalPages > 1 && (
          <div className="row" style={{ padding: '10px 16px', borderTop: '1px solid var(--border)', justifyContent: 'space-between' }}>
            <span className="muted" style={{ fontSize: 12.5 }}>
              Page {pageNumber} of {totalPages} ({totalRecords} events)
            </span>
            <div className="row" style={{ gap: 6 }}>
              <button
                className="sm"
                disabled={pageNumber <= 1}
                onClick={() => {
                  const next = pageNumber - 1
                  setPageNumber(next)
                  loadLogs(next)
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
                  loadLogs(next)
                }}
              >
                Next
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
