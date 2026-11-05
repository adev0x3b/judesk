import { DOMWrapper, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@/api/client'
import ChangePasswordDialog from '@/components/ChangePasswordDialog.vue'
import { useConfigStore } from '@/stores/config'
import { useToastStore } from '@/stores/toast'

vi.mock('@/api', () => ({
  authApi: { changePassword: vi.fn<() => Promise<void>>() },
}))

/** The dialog teleports to <body>, so its fields are found on the document, not the wrapper. */
const field = (selector: string) => new DOMWrapper(document.querySelector(selector) as HTMLElement)

async function fill(current: string, next: string, confirm: string) {
  await field('#current-password').setValue(current)
  await field('#new-password').setValue(next)
  await field('#confirm-password').setValue(confirm)
  await field('form').trigger('submit')
}

describe('ChangePasswordDialog', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    // jsdom has no <dialog>.showModal().
    HTMLDialogElement.prototype.showModal ??= function () {}
  })

  it('checks the length and the confirmation before calling the API', async () => {
    const { authApi } = await import('@/api')
    useConfigStore().passwordMinLength = 12
    const wrapper = mount(ChangePasswordDialog)

    await fill('old', 'short', 'short')
    expect(document.body.textContent).toContain('Use at least 12 characters.')
    expect(authApi.changePassword).not.toHaveBeenCalled()

    await fill('old', 'long-enough-password', 'something-else')
    expect(document.body.textContent).toContain('The passwords do not match.')
    expect(authApi.changePassword).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('shows the API field error and, on success, toasts and closes', async () => {
    const { authApi } = await import('@/api')
    const wrapper = mount(ChangePasswordDialog)
    vi.mocked(authApi.changePassword).mockRejectedValueOnce(
      new ApiError(400, { title: 'Validation failed', errors: { currentPassword: ['Wrong password.'] } }),
    )

    await fill('wrong', 'long-enough-password', 'long-enough-password')
    await Promise.resolve()
    await wrapper.vm.$nextTick()
    expect(document.body.textContent).toContain('Wrong password.')
    expect(wrapper.emitted('close')).toBeUndefined()

    vi.mocked(authApi.changePassword).mockResolvedValueOnce()
    await fill('right', 'long-enough-password', 'long-enough-password')
    await Promise.resolve()
    await wrapper.vm.$nextTick()
    expect(authApi.changePassword).toHaveBeenLastCalledWith('right', 'long-enough-password')
    expect(useToastStore().toasts.map((t) => t.title)).toContain('Password changed')
    expect(wrapper.emitted('close')).toHaveLength(1)
    wrapper.unmount()
  })
})
