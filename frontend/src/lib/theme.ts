import { ref } from 'vue'

export type Theme = 'system' | 'light' | 'dark'

const KEY = 'judesk-theme'
const ORDER: Theme[] = ['system', 'light', 'dark']

export const THEME_LABELS: Record<Theme, string> = {
  system: 'System theme',
  light: 'Light theme',
  dark: 'Dark theme',
}

function stored(): Theme {
  try {
    const value = localStorage.getItem(KEY)
    return value === 'light' || value === 'dark' ? value : 'system'
  } catch {
    return 'system'
  }
}

const current = ref<Theme>(stored())

/** Forces the colour scheme on the root element; "system" removes the override. */
export function applyTheme(theme: Theme) {
  const root = document.documentElement
  if (theme === 'system') delete root.dataset.theme
  else root.dataset.theme = theme
}

/** Theme picked by the user, kept in local storage so it survives a reload. */
export function useTheme() {
  function set(theme: Theme) {
    current.value = theme
    applyTheme(theme)
    try {
      if (theme === 'system') localStorage.removeItem(KEY)
      else localStorage.setItem(KEY, theme)
    } catch {
      // Private mode or storage disabled: the choice lasts until the page reloads.
    }
  }
  function cycle() {
    set(ORDER[(ORDER.indexOf(current.value) + 1) % ORDER.length]!)
  }
  return { theme: current, set, cycle }
}

applyTheme(current.value)
