import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { installMockBridgeIfAbsent } from './interop/mockBridge.ts'
import './index.css'
import App from './App.tsx'

// In a plain browser (npm run preview) there is no WebView2 host; an in-memory one answers instead.
installMockBridgeIfAbsent()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
