import { Route, Routes } from 'react-router-dom'
import { Layout } from '@/components/Layout'
import { Clients } from '@/pages/Clients'
import { Dashboard } from '@/pages/Dashboard'
import { Interfaces } from '@/pages/Interfaces'

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Dashboard />} />
        <Route path="interfaces" element={<Interfaces />} />
        <Route path="clients" element={<Clients />} />
      </Route>
    </Routes>
  )
}
