import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Панель отдаётся сервером по пути /panel, поэтому ссылки на ассеты должны быть от него.
  base: '/panel/',
  server: {
    host: '0.0.0.0',  // Слушать на всех интерфейсах (доступно по сети)
    port: 5173,       // Порт админки
  },
})
