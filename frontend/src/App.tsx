import { Route, Routes } from 'react-router'

import { Toaster } from '@/components/shared/Toaster'
import { LandingPage } from '@/pages/LandingPage'
import { WizardPage } from '@/pages/WizardPage'

export default function App() {
  return (
    <>
      <Routes>
        <Route path="/" element={<LandingPage />} />
        <Route path="/applications/:id" element={<WizardPage />} />
        <Route path="/applications/:id/:step" element={<WizardPage />} />
      </Routes>
      <Toaster />
    </>
  )
}
