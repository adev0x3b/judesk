<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { authApi } from '@/api'
import { ApiError } from '@/api/client'
import ModalDialog from '@/components/ModalDialog.vue'
import { useApiErrors } from '@/composables/useApiErrors'
import { useConfigStore } from '@/stores/config'
import { useToastStore } from '@/stores/toast'

const emit = defineEmits<{ close: [] }>()

const config = useConfigStore()
const toast = useToastStore()
const { fail } = useApiErrors()

const form = reactive({ currentPassword: '', newPassword: '', confirm: '' })
const errors = ref<Record<string, string[]>>({})
const busy = ref(false)

function error(field: string) {
  return errors.value[field]?.[0]
}

/** The first message for a field the form does not render, so no validation error goes unseen. */
const shownFields = ['currentPassword', 'newPassword', 'confirm']
const otherError = computed(() => {
  const key = Object.keys(errors.value).find((k) => !shownFields.includes(k))
  return key ? errors.value[key]?.[0] : undefined
})

async function submit() {
  errors.value = {}
  if (form.newPassword.length < config.passwordMinLength) {
    errors.value = { newPassword: [`Use at least ${config.passwordMinLength} characters.`] }
    return
  }
  if (form.confirm !== form.newPassword) {
    errors.value = { confirm: ['The passwords do not match.'] }
    return
  }
  busy.value = true
  try {
    await authApi.changePassword(form.currentPassword, form.newPassword)
    toast.success('Password changed')
    emit('close')
  } catch (e) {
    if (e instanceof ApiError && e.status === 400) errors.value = e.errors
    else fail(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <ModalDialog title="Change password" @close="emit('close')">
    <form @submit.prevent="submit">
      <div class="field">
        <label for="current-password">Current password</label>
        <input
          id="current-password"
          v-model="form.currentPassword"
          class="input"
          type="password"
          autocomplete="current-password"
          required
          autofocus
          :aria-invalid="!!error('currentPassword')"
        />
        <span v-if="error('currentPassword')" class="field-error">{{ error('currentPassword') }}</span>
      </div>
      <div class="field">
        <label for="new-password">New password</label>
        <input
          id="new-password"
          v-model="form.newPassword"
          class="input"
          type="password"
          autocomplete="new-password"
          required
          :minlength="config.passwordMinLength"
          :aria-invalid="!!error('newPassword')"
        />
        <span v-if="error('newPassword')" class="field-error">{{ error('newPassword') }}</span>
        <span v-else class="faint">At least {{ config.passwordMinLength }} characters.</span>
      </div>
      <div class="field">
        <label for="confirm-password">Confirm new password</label>
        <input
          id="confirm-password"
          v-model="form.confirm"
          class="input"
          type="password"
          autocomplete="new-password"
          required
          :aria-invalid="!!error('confirm')"
        />
        <span v-if="error('confirm')" class="field-error">{{ error('confirm') }}</span>
      </div>
      <p v-if="otherError" class="field-error" role="alert">{{ otherError }}</p>
      <div class="modal-actions">
        <button type="button" class="btn" @click="emit('close')">Cancel</button>
        <button type="submit" class="btn btn-primary" :disabled="busy">{{ busy ? 'Saving…' : 'Change password' }}</button>
      </div>
    </form>
  </ModalDialog>
</template>
