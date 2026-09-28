import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  base: '/aula-adaptada/',
  plugins: [react()],
  // Without this, Vite can bind IPv6-loopback only ([::1]:5173) and "localhost" in the browser
  // fails to connect depending on how the OS resolves it — explicit IPv4 loopback avoids that.
  server: { host: '127.0.0.1', port: 5173 },
})
