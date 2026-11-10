import { useEffect, useState } from 'react'
import { api, formatBytes, formatDate } from '../api'
import Icon from '../components/Icon'

export default function RegistryPage({ notify }) {
  const [repos, setRepos] = useState([])
  const [repoSearch, setRepoSearch] = useState('')
  const [selectedRepo, setSelectedRepo] = useState(null)

  const [tagsData, setTagsData] = useState(null)
  const [selectedRef, setSelectedRef] = useState(null)
  const [manifest, setManifest] = useState(null)

  const [loadingRepos, setLoadingRepos] = useState(true)
  const [loadingTags, setLoadingTags] = useState(false)
  const [loadingManifest, setLoadingManifest] = useState(false)
  const [repoError, setRepoError] = useState('')

  const host = window.location.host || 'registry.yourcompany.com'

  async function fetchRepositories() {
    setLoadingRepos(true)
    setRepoError('')
    try {
      const list = await api.get('/api/Registry/repositories')
      setRepos(list || [])
      if (list?.length > 0 && !selectedRepo) {
        selectRepository(list[0])
      }
    } catch (err) {
      setRepoError(err.message)
    } finally {
      setLoadingRepos(false)
    }
  }

  async function selectRepository(repo) {
    setSelectedRepo(repo)
    setSelectedRef(null)
    setManifest(null)
    setLoadingTags(true)
    try {
      const data = await api.get(`/api/Registry/tags?repositoryName=${encodeURIComponent(repo.name)}`)
      setTagsData(data)
      if (data?.tags?.length > 0) {
        const preferred = data.tags.includes('latest') ? 'latest' : data.tags[0]
        selectTagOrDigest(repo.name, preferred)
      }
    } catch (err) {
      notify(err.message, 'err')
    } finally {
      setLoadingTags(false)
    }
  }

  async function selectTagOrDigest(repoName, reference) {
    setSelectedRef(reference)
    setLoadingManifest(true)
    try {
      const data = await api.get(
        `/api/Registry/manifest?repositoryName=${encodeURIComponent(repoName)}&reference=${encodeURIComponent(reference)}`
      )
      setManifest(data)
    } catch (err) {
      notify(err.message, 'err')
      setManifest(null)
    } finally {
      setLoadingManifest(false)
    }
  }

  async function handleDeleteManifest(reference) {
    if (!selectedRepo) return
    if (!window.confirm(`Delete "${selectedRepo.name}:${reference}" from the Docker Registry? Note: In Registry v3, deleting a manifest removes all tags pointing to the same sha256 digest.`)) {
      return
    }
    try {
      await api.del(
        `/api/Registry/manifest?repositoryName=${encodeURIComponent(selectedRepo.name)}&reference=${encodeURIComponent(reference)}`
      )
      notify(`Deleted ${selectedRepo.name}:${reference}`, 'ok')
      await selectRepository(selectedRepo)
    } catch (err) {
      notify(err.message, 'err')
    }
  }

  function copyText(text, label) {
    navigator.clipboard.writeText(text)
    notify(`${label} copied`, 'ok')
  }

  useEffect(() => {
    fetchRepositories()
  }, [])

  const filteredRepos = repos.filter((r) =>
    r.name.toLowerCase().includes(repoSearch.trim().toLowerCase())
  )

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Registry Images & Tags</h1>
        </div>
        <button onClick={fetchRepositories} disabled={loadingRepos}>
          <Icon name="refresh" size={15} />
          Refresh Registry
        </button>
      </div>

      {repoError && (
        <div className="card" style={{ borderColor: 'color-mix(in srgb, var(--warn) 40%, transparent)', marginBottom: 16 }}>
          <div className="row" style={{ justifyContent: 'space-between' }}>
            <div>
              <b style={{ color: 'var(--warn)' }}>Upstream Docker Registry Notice</b>
              <p className="muted" style={{ margin: '4px 0 0', fontSize: 13 }}>{repoError}</p>
            </div>
            <button className="sm" onClick={fetchRepositories}>Retry</button>
          </div>
        </div>
      )}

      <div className="registry-grid">
        {/* Column 1: Repositories */}
        <div className="card tight">
          <div style={{ padding: '12px 14px', borderBottom: '1px solid var(--border)' }}>
            <div className="row" style={{ justifyContent: 'space-between', marginBottom: 8 }}>
              <h3 style={{ margin: 0 }}>Repositories ({filteredRepos.length})</h3>
            </div>
            <input
              type="text"
              placeholder="Filter repositories…"
              value={repoSearch}
              onChange={(e) => setRepoSearch(e.target.value)}
              style={{ width: '100%' }}
            />
          </div>

          <div style={{ maxHeight: 580, overflowY: 'auto' }}>
            {loadingRepos ? (
              <div className="empty"><span className="spin" /> Loading repositories…</div>
            ) : filteredRepos.length === 0 ? (
              <div className="empty">No repositories found.</div>
            ) : (
              filteredRepos.map((repo) => (
                <div
                  key={repo.name}
                  className={`list-item ${selectedRepo?.name === repo.name ? 'active' : ''}`}
                  onClick={() => selectRepository(repo)}
                >
                  <div style={{ minWidth: 0 }}>
                    <div className="row" style={{ gap: 6 }}>
                      <Icon name="box" size={15} />
                      <b style={{ fontSize: 13.5, wordBreak: 'break-all' }}>{repo.name}</b>
                    </div>
                    <div className="row" style={{ gap: 4, marginTop: 4 }}>
                      {repo.canPull && <span className="badge info">pull</span>}
                      {repo.canPush && <span className="badge ok">push</span>}
                      {repo.canDelete && <span className="badge err">delete</span>}
                    </div>
                  </div>
                </div>
              ))
            )}
          </div>
        </div>

        {/* Column 2: Tags */}
        <div className="card tight">
          <div style={{ padding: '12px 14px', borderBottom: '1px solid var(--border)' }}>
            <h3 style={{ margin: 0 }}>
              Tags {selectedRepo ? `(${tagsData?.tags?.length || 0})` : ''}
            </h3>
            <div className="muted" style={{ fontSize: 12, marginTop: 2 }}>
              {selectedRepo ? selectedRepo.name : 'Select a repository'}
            </div>
          </div>

          <div style={{ maxHeight: 580, overflowY: 'auto' }}>
            {!selectedRepo ? (
              <div className="empty">Select a repository to view its tags.</div>
            ) : loadingTags ? (
              <div className="empty"><span className="spin" /> Loading tags…</div>
            ) : !tagsData?.tags?.length ? (
              <div className="empty">No tags in this repository.</div>
            ) : (
              tagsData.tags.map((tag) => (
                <div
                  key={tag}
                  className={`list-item ${selectedRef === tag ? 'active' : ''}`}
                  onClick={() => selectTagOrDigest(selectedRepo.name, tag)}
                >
                  <div className="row" style={{ gap: 6, minWidth: 0 }}>
                    <Icon name="tag" size={14} />
                    <span className="mono" style={{ fontWeight: 600 }}>{tag}</span>
                  </div>
                  <div className="row" style={{ gap: 4 }} onClick={(e) => e.stopPropagation()}>
                    <button
                      className="ghost icon-btn sm"
                      title={`Copy docker pull ${host}/${selectedRepo.name}:${tag}`}
                      onClick={() => copyText(`docker pull ${host}/${selectedRepo.name}:${tag}`, 'Pull command')}
                    >
                      <Icon name="copy" size={13} />
                    </button>
                    {tagsData.canDelete && (
                      <button
                        className="ghost danger icon-btn sm"
                        title="Delete tag manifest"
                        onClick={() => handleDeleteManifest(tag)}
                      >
                        <Icon name="trash" size={13} />
                      </button>
                    )}
                  </div>
                </div>
              ))
            )}
          </div>
        </div>

        {/* Column 3: Manifest & Layer Inspector */}
        <div className="card">
          {!selectedRef ? (
            <div className="empty">
              <Icon name="layers" size={28} />
              <div>Select a tag to inspect its manifest, config blob, and filesystem layers.</div>
            </div>
          ) : loadingManifest ? (
            <div className="empty"><span className="spin" /> Inspecting manifest…</div>
          ) : !manifest ? (
            <div className="empty">Could not load manifest details.</div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
              <div className="row" style={{ justifyContent: 'space-between', alignItems: 'flex-start' }}>
                <div>
                  <div className="row" style={{ gap: 8 }}>
                    <h2 style={{ margin: 0 }}>{manifest.repository}:{manifest.reference}</h2>
                    <span className="badge accent">
                      {manifest.isMultiArch ? 'Multi-Arch Index' : `${manifest.os || 'linux'}/${manifest.architecture || 'amd64'}`}
                    </span>
                    <span className="badge">{formatBytes(manifest.totalSizeBytes)}</span>
                  </div>
                  <div className="row" style={{ gap: 8, marginTop: 6 }}>
                    <code style={{ fontSize: 11.5, wordBreak: 'break-all' }}>{manifest.digest || '—'}</code>
                    {manifest.digest && (
                      <button className="ghost sm" onClick={() => copyText(manifest.digest, 'Digest')}>
                        <Icon name="copy" size={12} /> Copy Digest
                      </button>
                    )}
                  </div>
                </div>

                {selectedRepo?.canDelete && (
                  <button className="danger sm" onClick={() => handleDeleteManifest(manifest.reference)}>
                    <Icon name="trash" size={14} />
                    Delete Manifest
                  </button>
                )}
              </div>

              {/* Pull Command Box */}
              <div className="row" style={{ justifyContent: 'space-between', background: 'var(--bg2)', padding: '9px 12px', borderRadius: 'var(--r-sm)', border: '1px solid var(--border)' }}>
                <code style={{ border: 0, background: 'none' }}>
                  docker pull {host}/{manifest.repository}:{manifest.reference}
                </code>
                <button className="ghost sm" onClick={() => copyText(`docker pull ${host}/${manifest.repository}:${manifest.reference}`, 'Pull command')}>
                  <Icon name="copy" size={13} /> Copy
                </button>
              </div>

              {/* Metadata Grid */}
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 10 }}>
                <div style={{ background: 'var(--bg2)', padding: '10px 12px', borderRadius: 'var(--r-sm)', border: '1px solid var(--border)' }}>
                  <div className="dim" style={{ fontSize: 11, textTransform: 'uppercase', fontWeight: 650 }}>Media Type</div>
                  <div className="mono" style={{ fontSize: 12, marginTop: 3, wordBreak: 'break-all' }}>{manifest.mediaType}</div>
                </div>
                <div style={{ background: 'var(--bg2)', padding: '10px 12px', borderRadius: 'var(--r-sm)', border: '1px solid var(--border)' }}>
                  <div className="dim" style={{ fontSize: 11, textTransform: 'uppercase', fontWeight: 650 }}>Created</div>
                  <div style={{ fontSize: 13, marginTop: 3 }}>{manifest.createdAt ? formatDate(manifest.createdAt) : 'Multi-arch / N/A'}</div>
                </div>
                {!manifest.isMultiArch && (
                  <div style={{ background: 'var(--bg2)', padding: '10px 12px', borderRadius: 'var(--r-sm)', border: '1px solid var(--border)' }}>
                    <div className="dim" style={{ fontSize: 11, textTransform: 'uppercase', fontWeight: 650 }}>Layers</div>
                    <div style={{ fontSize: 13, marginTop: 3 }}>{manifest.layers.length} filesystem blobs ({formatBytes(manifest.totalSizeBytes)})</div>
                  </div>
                )}
              </div>

              {/* Multi-Arch Platforms Table */}
              {manifest.isMultiArch && manifest.platforms?.length > 0 && (
                <div>
                  <h3>Supported CPU Architectures ({manifest.platforms.length})</h3>
                  <div className="table-wrap" style={{ border: '1px solid var(--border)', borderRadius: 'var(--r-sm)' }}>
                    <table>
                      <thead>
                        <tr>
                          <th>Platform</th>
                          <th>Manifest Digest</th>
                          <th className="num">Manifest Size</th>
                          <th />
                        </tr>
                      </thead>
                      <tbody>
                        {manifest.platforms.map((p) => (
                          <tr key={p.digest}>
                            <td>
                              <span className="badge info">
                                {p.os}/{p.architecture}{p.variant ? `/${p.variant}` : ''}
                              </span>
                            </td>
                            <td className="mono" style={{ fontSize: 12 }}>{p.digest}</td>
                            <td className="num muted">{formatBytes(p.size)}</td>
                            <td className="num">
                              <button
                                className="sm"
                                onClick={() => selectTagOrDigest(manifest.repository, p.digest)}
                              >
                                Inspect Platform Layers
                              </button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {/* Config Runtime Details */}
              {!manifest.isMultiArch && (manifest.entrypoint?.length > 0 || manifest.cmd?.length > 0 || manifest.exposedPorts?.length > 0) && (
                <div>
                  <h3>Container Config Blob Settings</h3>
                  <div style={{ display: 'grid', gridTemplateColumns: '120px 1fr', rowGap: 6, fontSize: 13, background: 'var(--bg2)', padding: 12, borderRadius: 'var(--r-sm)', border: '1px solid var(--border)' }}>
                    {manifest.entrypoint?.length > 0 && (
                      <>
                        <span className="muted">ENTRYPOINT:</span>
                        <code>{JSON.stringify(manifest.entrypoint)}</code>
                      </>
                    )}
                    {manifest.cmd?.length > 0 && (
                      <>
                        <span className="muted">CMD:</span>
                        <code>{JSON.stringify(manifest.cmd)}</code>
                      </>
                    )}
                    {manifest.exposedPorts?.length > 0 && (
                      <>
                        <span className="muted">EXPOSE:</span>
                        <div className="row" style={{ gap: 4 }}>
                          {manifest.exposedPorts.map((port) => (
                            <span key={port} className="badge purple">{port}</span>
                          ))}
                        </div>
                      </>
                    )}
                  </div>
                </div>
              )}

              {/* Single-Arch Layers Breakdown */}
              {!manifest.isMultiArch && manifest.layers?.length > 0 && (
                <div>
                  <h3>Filesystem Layer Blobs ({manifest.layers.length})</h3>
                  <div className="table-wrap" style={{ border: '1px solid var(--border)', borderRadius: 'var(--r-sm)' }}>
                    <table>
                      <thead>
                        <tr>
                          <th>#</th>
                          <th>Blob Digest (sha256)</th>
                          <th>Proportion</th>
                          <th className="num">Compressed Size</th>
                        </tr>
                      </thead>
                      <tbody>
                        {manifest.layers.map((layer, idx) => {
                          const pct = manifest.totalSizeBytes > 0
                            ? Math.max(2, Math.round((layer.size / manifest.totalSizeBytes) * 100))
                            : 0
                          return (
                            <tr key={`${layer.digest}-${idx}`}>
                              <td className="muted">{idx + 1}</td>
                              <td>
                                <code style={{ fontSize: 11.5 }}>{layer.digest}</code>
                              </td>
                              <td style={{ width: 140 }}>
                                <div className="bar">
                                  <i style={{ width: `${pct}%` }} />
                                </div>
                              </td>
                              <td className="num"><b>{formatBytes(layer.size)}</b></td>
                            </tr>
                          )
                        })}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
