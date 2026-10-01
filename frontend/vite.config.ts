import react from '@vitejs/plugin-react'
// defineConfig do vitest, não do vite: é o que conhece a seção "test".
import { defineConfig } from 'vitest/config'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  test: {
    // Só os testes de componente precisam de DOM; os de lógica pura rodam igual nele.
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}']
  }
})
