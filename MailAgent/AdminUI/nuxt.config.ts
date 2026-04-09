export default defineNuxtConfig({
  compatibilityDate: '2025-07-11',
  ssr: false,

  // Use app/ as the source directory (Nuxt 4 style, compatible with Nuxt 3 via srcDir)
  srcDir: 'app',

  modules: ['@nuxt/ui'],

  css: ['~/assets/css/main.css'],

  devServer: {
    port: 3000
  },

  runtimeConfig: {
    public: {
      apiBase: '/api'
    }
  },

  // In development, proxy API requests to the .NET backend
  devProxy: {
    '/api': {
      target: 'http://localhost:5050',
      changeOrigin: true
    }
  }
})
