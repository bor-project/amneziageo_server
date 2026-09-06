import { NavLink, Outlet } from 'react-router-dom'
import { useHealth } from '@/api/health'
import { useAppDispatch, useAppSelector } from '@/store/hooks'
import { sidebarToggled } from '@/store/uiSlice'

const links = [
  { to: '/', label: 'Обзор' },
  { to: '/interfaces', label: 'Интерфейсы' },
  { to: '/clients', label: 'Клиенты' },
]

export function Layout() {
  const open = useAppSelector((s) => s.ui.sidebarOpen)
  const dispatch = useAppDispatch()
  const health = useHealth()

  return (
    <div className="flex h-full bg-slate-50 text-slate-900">
      {open && (
        <aside className="w-56 shrink-0 border-r border-slate-200 bg-white">
          <div className="px-4 py-4 text-lg font-semibold text-brand">AmneziaGeo</div>
          <nav className="flex flex-col gap-1 px-2">
            {links.map((l) => (
              <NavLink
                key={l.to}
                to={l.to}
                end={l.to === '/'}
                className={({ isActive }) =>
                  `rounded px-3 py-2 text-sm ${
                    isActive ? 'bg-brand-soft font-medium text-brand' : 'text-slate-600 hover:bg-slate-100'
                  }`
                }
              >
                {l.label}
              </NavLink>
            ))}
          </nav>
        </aside>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex items-center gap-4 border-b border-slate-200 bg-white px-4 py-3">
          <button
            type="button"
            onClick={() => dispatch(sidebarToggled())}
            className="rounded border border-slate-200 px-2 py-1 text-sm text-slate-600 hover:bg-slate-50"
          >
            Меню
          </button>
          <span className="text-sm text-slate-500">
            {health.isPending && 'проверка связи'}
            {health.isError && 'сервер не отвечает'}
            {health.data && `сервер ${health.data.version}`}
          </span>
        </header>

        <main className="min-h-0 flex-1 overflow-auto p-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
