const STORAGE_KEY = 'mobyguard_session'

const ERROR_MESSAGES = {
  _invalidCredentials: 'Invalid username or password.',
  _userIsInactive: 'This account has been deactivated.',
  _serviceAccountCannotLogin: 'Service accounts can only authenticate via Docker CLI (/api/Token), not the web UI.',
  _tooManyRequests: 'Too many failed login attempts. Please wait 15 minutes.',
  _userNotFound: 'User not found.',
  _userAlreadyExists: 'A user with that username already exists.',
  _emailAlreadyExists: 'A user with that email address already exists.',
  _cannotDeleteYourself: 'You cannot delete your own account.',
  _cannotChangeOwnTypeOrStatus: 'You cannot demote or deactivate your own admin account.',
  _invalidCurrentPassword: 'Your current password is incorrect.',
  _userTypeNotFound: 'Role / UserType not found.',
  _userTypeAlreadyExists: 'That role already exists.',
  _cannotDeleteSystemUserType: 'Built-in system roles (Admin, User, Service) cannot be modified or deleted.',
  _userTypeInUse: 'Cannot delete a role that is currently assigned to users.',
  _repositoryPermissionNotFound: 'Repository permission rule not found.',
  _repositoryPermissionAlreadyExists: 'A permission rule for this user and repository pattern already exists.',
  _invalidRepositoryPattern: 'Invalid pattern. Use "*", "namespace/*", or a lowercase repository name like "olympia/app".',
  _noPermissionActionsSelected: 'Select at least one action (Pull, Push, or Delete).',
  _setupAlreadyCompleted: 'Initial setup has already been completed.',
  _registryRepositoryAccessDenied: 'You do not have permission for this repository operation.',
  _registryManifestNotFound: 'Manifest or tag not found in the Docker Registry.',
  _registryUpstreamError: 'Could not reach the upstream Docker Registry container. Check RegistryUrl in appsettings.json.',
  _internalServerError: 'An unexpected server error occurred.'
}

export function formatErrorMessage(codeOrMessage) {
  if (!codeOrMessage) return 'Request failed.'
  return ERROR_MESSAGES[codeOrMessage] || codeOrMessage
}

export function loadSession() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? JSON.parse(raw) : null
  } catch {
    return null
  }
}

export function saveSession(session) {
  if (!session) {
    localStorage.removeItem(STORAGE_KEY)
  } else {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
  }
}

async function request(method, path, body) {
  const session = loadSession()
  const headers = {}
  if (session?.token) {
    headers['Authorization'] = `Bearer ${session.token}`
  }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }

  const res = await fetch(path, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined
  })

  if (res.status === 204) {
    return null
  }

  const contentType = res.headers.get('content-type') || ''
  const isJson = contentType.includes('json')
  const data = isJson ? await res.json().catch(() => null) : await res.text().catch(() => '')

  if (!res.ok) {
    if (res.status === 401 && !path.includes('/login')) {
      saveSession(null)
      window.dispatchEvent(new CustomEvent('mobyguard:unauthorized'))
    }
    const detail = data?.detail || data?.title || (typeof data === 'string' && data) || `HTTP ${res.status}`
    throw new Error(formatErrorMessage(detail))
  }

  return data
}

export const api = {
  get: (path) => request('GET', path),
  post: (path, body) => request('POST', path, body),
  put: (path, body) => request('PUT', path, body),
  del: (path) => request('DELETE', path)
}

export function formatBytes(bytes) {
  if (!bytes || bytes <= 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(1024))
  return `${(bytes / Math.pow(1024, i)).toFixed(i === 0 ? 0 : 1)} ${units[i]}`
}

export function formatDate(iso) {
  if (!iso) return '—'
  const d = new Date(iso)
  if (isNaN(d.getTime()) || d.getFullYear() <= 1971) return 'Never'
  return d.toLocaleString()
}
