---
name: modern-web-guidance
description: >-
  Standards and workflow for building, auditing, and refactoring modern web dashboards and React SPAs.
  Use this skill when creating or improving web UI components, layouts, styles, accessibility,
  responsive navigation, forms, modals, and state interactions.
---

# Modern Web Guidance

Follow these conventions whenever building, scanning, or refactoring a web dashboard or React application.

## 1. Design Tokens & Semantic CSS Architecture

* **Prefer semantic CSS classes over repetitive inline styles**: Centralize recurring layout patterns in `styles.css` using CSS custom properties (`var(--bg)`, `var(--panel)`, `var(--border)`, `var(--accent)`, etc.):
  * `.filter-bar`: Standardized toolbar card for search/filter forms.
  * `.pagination-bar`: Standardized table footer for page counts and `Previous` / `Next` controls.
  * `.form-grid-2`: Responsive 2-column form field grid (`grid-template-columns: repeat(auto-fit, minmax(200px, 1fr))`).
  * `.meta-grid` & `.meta-card`: Consistent metadata key-value cards.
  * `.code-bar`: Copyable command/digest bar with monospace typography and action button.
  * `.alert-card`: Semantic callout banner (`.alert-card.warn`, `.alert-card.info`, `.alert-card.err`).
* **Theme parity**: All surfaces, borders, badges, and focus rings must adapt cleanly between `[data-theme='dark']` and `[data-theme='light']` using CSS variables and `color-mix(in srgb, ...)`.
* **Tabular numerals**: Apply `font-variant-numeric: tabular-nums` to IDs, counts, sizes, timestamps, and metrics so columns align vertically.

## 2. Responsive Shell & Mobile Navigation

* **Mobile Topbar & Drawer**: Whenever a sidebar (`aside`) collapses below `860px`, render a `.mobile-topbar` with a menu toggle button (`aria-label="Toggle navigation"`, `aria-expanded`) and a backdrop overlay (`.sidebar-backdrop`) that toggles `aside.open`, closing automatically on route selection or `Escape`.
* **Persistent App Footer**: Keep a single `<footer className="app-footer">` at the bottom of `<main>` displaying the application name and single project version.

## 3. Accessibility (a11y) & Keyboard UX

* **Modals**:
  * Add `role="dialog"` and `aria-modal="true"` to `.modal` containers.
  * Support closing modals with the `Escape` key via a `keydown` listener.
  * Include an explicit close button (`aria-label="Close modal"`) in `.modal-head`.
* **Interactive Elements**:
  * Clickable list rows (`.list-item`) must either be `<button>` elements or carry `role="button"`, `tabIndex={0}`, and an `onKeyDown` handler for `Enter` and `Space`.
  * Icon-only buttons (`.icon-btn`) must include `aria-label` in addition to `title`.
* **Forms & Inputs**:
  * Include appropriate `autoComplete` attributes on authentication and account forms (`username`, `current-password`, `new-password`, `one-time-code`).
* **Live Regions**:
  * Toast containers (`.toast-stack`) must include `role="status"` and `aria-live="polite"`, and error banners should use `role="alert"`.

## 4. React State & Async Hygiene

* **Avoid `setTimeout(..., 0)` state workarounds**: Pass explicit override arguments (e.g., `loadLogs(1, '0', '')`) when resetting filters and reloading data in the same event handler.
* **Clipboard safety**: Wrap `navigator.clipboard.writeText(...)` in a `try/catch` block so non-HTTPS contexts do not throw unhandled promise rejections.

## 5. Verification Steps

1. Run `npm --prefix web run build` to verify zero Vite/Rollup build errors or broken imports.
2. Run `dotnet build MobyGuard.sln --no-restore` and the integration test suite to verify the static assets and API contracts remain intact.
