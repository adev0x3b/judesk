import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'

import App from './App.vue'
import router from './router'
import { ApiError } from './api/client'
import { useToastStore } from './stores/toast'

const app = createApp(App)

app.use(createPinia())
app.use(router)

// A call that fails inside a click handler with no catch of its own still gets a toast, so no
// action fails silently. A 401 already sends the user to the login page.
app.config.errorHandler = (e) => {
  if (e instanceof ApiError && e.isUnauthorized) return
  if (import.meta.env.DEV) console.error(e)
  const toast = useToastStore()
  if (e instanceof ApiError) toast.error(e.title, e.detail ? [e.detail] : [])
  else toast.error('Something went wrong')
}

app.mount('#app')
