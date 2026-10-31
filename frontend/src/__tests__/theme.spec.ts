import { beforeEach, describe, expect, it } from 'vitest'
import { applyTheme, useTheme } from '@/lib/theme'

describe('theme', () => {
  beforeEach(() => {
    localStorage.clear()
    useTheme().set('system')
  })

  it('cycles system -> light -> dark -> system and stores the choice', () => {
    const { theme, cycle } = useTheme()
    expect(theme.value).toBe('system')
    expect(document.documentElement.dataset.theme).toBeUndefined()

    cycle()
    expect(theme.value).toBe('light')
    expect(document.documentElement.dataset.theme).toBe('light')
    expect(localStorage.getItem('judesk-theme')).toBe('light')

    cycle()
    expect(theme.value).toBe('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')

    cycle()
    expect(theme.value).toBe('system')
    expect(document.documentElement.dataset.theme).toBeUndefined()
    expect(localStorage.getItem('judesk-theme')).toBeNull()
  })

  it('applies a theme to the root element', () => {
    applyTheme('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
    applyTheme('system')
    expect(document.documentElement.dataset.theme).toBeUndefined()
  })
})
