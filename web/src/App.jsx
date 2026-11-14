import { useEffect, useState } from 'react'
import { api, loadSession, saveSession } from './api'
import Icon from './components/Icon'
import AuthPage from './pages/AuthPage'
import RegistryPage from './pages/RegistryPage'
import PermissionsPage from './pages/PermissionsPage'
import UsersPage from './pages/UsersPage'
import AuditLogsPage from './pages/AuditLogsPage'
import ProfilePage from './pages/ProfilePage'

export default function App() {
  const [ready, setReady] = useState(false)
  const [setupRequired, setSetupRequired] = useState(false)
  const [user, setUser] = useState(() => loadSession())
  const [route, setRoute] = useState(() => window.location.hash.slice(1) || '/')
  const [theme, setTheme] = useState(() => localStorage.getItem('mobyguard_theme') || 'dark')
  const [toasts, setToasts] = useState([])

  function notify(message, type = 'info') {
    const id = Date.now() + Math.random()
    setToasts((prev) => [...prev, { id, message, type }])
    setTimeout(() => {
      setToasts((prev) => prev.filter((t) => t.id !== id))
    }, 3800)
  }

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme)
    localStorage.setItem('mobyguard_theme', theme)
  }, [theme])

  useEffect(() => {
    if (!user || setupRequired) {
      document.title = setupRequired ? 'Setup Admin — MobyGuard' : 'Sign In — MobyGuard'
      return
    }
    const titles = {
      '/': 'Images & Tags — MobyGuard',
      '/permissions': 'Policies — MobyGuard',
      '/users': 'Users & Roles — MobyGuard',
      '/audit': 'Audit Logs — MobyGuard',
      '/profile': 'My Account — MobyGuard'
    }
    const matched = Object.keys(titles).find((p) => p !== '/' && route.startsWith(p))
    document.title = titles[matched] || titles['/'] || 'MobyGuard'
  }, [user, setupRequired, route])

  useEffect(() => {
    const onHash = () => setRoute(window.location.hash.slice(1) || '/')
    const onUnauth = () => setUser(null)
    window.addEventListener('hashchange', onHash)
    window.addEventListener('mobyguard:unauthorized', onUnauth)
    return () => {
      window.removeEventListener('hashchange', onHash)
      window.removeEventListener('mobyguard:unauthorized', onUnauth)
    }
  }, [])

  useEffect(() => {
    async function init() {
      try {
        const status = await api.get('/api/v1/User/setup-status')
        if (status?.setupRequired) {
          setSetupRequired(true)
          saveSession(null)
          setUser(null)
          setReady(true)
          return
        }
        const existing = loadSession()
        if (existing?.token) {
          await api.get('/api/v1/User/me')
          setUser(existing)
        }
      } catch {
        saveSession(null)
        setUser(null)
      } finally {
        setReady(true)
      }
    }
    init()
  }, [])

  function handleAuthenticated(loginResponse) {
    saveSession(loginResponse)
    setSetupRequired(false)
    setUser(loginResponse)
  }

  async function handleLogout() {
    const existing = loadSession()
    if (existing?.token) {
      try {
        await api.post('/api/v1/User/logout', { refreshToken: existing.refreshToken })
      } catch {}
    }
    saveSession(null)
    setUser(null)
  }

  if (!ready) {
    return (
      <div style={{ height: '100%', display: 'grid', placeItems: 'center' }}>
        <span className="spin" />
      </div>
    )
  }

  if (!user || setupRequired) {
    return (
      <>
        <AuthPage
          setupRequired={setupRequired}
          onAuthenticated={handleAuthenticated}
          notify={notify}
        />
        <ToastStack toasts={toasts} />
      </>
    )
  }

  const isAdmin = user.userType === 'Admin'

  const navItems = [
    { section: 'Registry' },
    { path: '/', label: 'Images & Tags', icon: 'box' },
    { section: 'Access Control' },
    { path: '/permissions', label: 'Policies', icon: 'shield' },
    ...(isAdmin
      ? [
          { path: '/users', label: 'Users & Roles', icon: 'users' },
          { path: '/audit', label: 'Audit Logs', icon: 'logs' }
        ]
      : []),
    { section: 'Account' },
    { path: '/profile', label: 'My Account', icon: 'user' }
  ]

  let PageComponent = RegistryPage
  if (route.startsWith('/permissions')) PageComponent = PermissionsPage
  else if (isAdmin && route.startsWith('/users')) PageComponent = UsersPage
  else if (isAdmin && route.startsWith('/audit')) PageComponent = AuditLogsPage
  else if (route.startsWith('/profile')) PageComponent = ProfilePage

  const initials = (user.username || 'U').slice(0, 2).toUpperCase()

  return (
    <div className="shell">
      <aside>
        <div className="brand">
          <div className="brand-logo">
            <img src="/logo.png" alt="MobyGuard" />
          </div>
          <div className="brand-text">
            <b>MobyGuard</b>
            <span>Registry v3 Control Plane</span>
          </div>
        </div>

        <nav>
          {navItems.map((item, idx) =>
            item.section ? (
              <div key={idx} className="nav-section">{item.section}</div>
            ) : (
              <a
                key={item.path}
                href={`#${item.path}`}
                className={route === item.path ? 'active' : ''}
              >
                <Icon name={item.icon} size={17} />
                <span>{item.label}</span>
              </a>
            )
          )}
        </nav>

        <div className="sidebar-footer">
          <div className="avatar">{initials}</div>
          <div className="who">
            <b>{user.username}</b>
            <span>{user.userType}</span>
          </div>
          <button
            className="ghost icon-btn sm"
            title={theme === 'dark' ? 'Switch to Light Mode' : 'Switch to Dark Mode'}
            onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}
          >
            <Icon name={theme === 'dark' ? 'sun' : 'moon'} size={15} />
          </button>
          <button
            className="ghost icon-btn sm"
            title="Sign Out"
            onClick={handleLogout}
          >
            <Icon name="logout" size={15} />
          </button>
        </div>
      </aside>

      <main>
        <PageComponent user={user} notify={notify} />
      </main>

      <ToastStack toasts={toasts} />
    </div>
  )
}

function ToastStack({ toasts }) {
  if (!toasts.length) return null
  return (
    <div className="toast-stack">
      {toasts.map((t) => (
        <div key={t.id} className={`toast ${t.type}`}>
          <span>{t.message}</span>
        </div>
      ))}
    </div>
  )
}
