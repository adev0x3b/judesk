<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { usersApi } from '@/api'
import { ApiError } from '@/api/client'
import type { CreateUserRequest, User } from '@/api/types'
import ModalDialog from '@/components/ModalDialog.vue'
import { useAuthStore } from '@/stores/auth'
import { useApiErrors } from '@/composables/useApiErrors'
import { useToastStore } from '@/stores/toast'

const auth = useAuthStore()
const toast = useToastStore()
const { fail } = useApiErrors()
const users = ref<User[]>([])
const creating = ref(false)
const errors = ref<Record<string, string[]>>({})
const busy = ref(false)
const form = reactive<CreateUserRequest>({ email: '', displayName: '', password: '', role: 'Planner' })

/** The first message for a field the form does not render, so no validation error goes unseen. */
const shownFields = ['displayName', 'email', 'password']
const otherError = computed(() => {
  const key = Object.keys(errors.value).find((k) => !shownFields.includes(k))
  return key ? errors.value[key]?.[0] : undefined
})

async function load() {
  users.value = await usersApi.list()
}

onMounted(() => load().catch(fail))

/** The form starts blank every time it opens, and leaves nothing behind when cancelled. */
function reset() {
  errors.value = {}
  Object.assign(form, { email: '', displayName: '', password: '', role: 'Planner' })
}

function open() {
  reset()
  creating.value = true
}

function cancel() {
  reset()
  creating.value = false
}

async function save() {
  errors.value = {}
  busy.value = true
  try {
    await usersApi.create({ ...form })
    cancel()
    toast.success('User created')
    await load()
  } catch (e) {
    if (e instanceof ApiError && e.status === 400) errors.value = e.errors
    else fail(e)
  } finally {
    busy.value = false
  }
}

async function remove(u: User) {
  if (!window.confirm(`Delete the account ${u.email}?`)) return
  try {
    await usersApi.remove(u.id)
    await load()
  } catch (e) {
    fail(e)
  }
}
</script>

<template>
  <div class="page">
    <header class="page-header">
      <h1>Users</h1>
      <button class="btn btn-primary" @click="open">+ User</button>
    </header>

    <div class="card">
      <table class="table">
        <thead>
          <tr>
            <th>Name</th>
            <th>Email</th>
            <th>Role</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="u in users" :key="u.id">
            <td>
              <strong>{{ u.displayName }}</strong>
            </td>
            <td class="muted">{{ u.email }}</td>
            <td>
              <span class="chip" :class="u.role === 'Admin' ? 'chip-accent' : ''">{{ u.role }}</span>
            </td>
            <td class="actions">
              <button v-if="u.id !== auth.user?.id" class="btn btn-sm btn-ghost" @click="remove(u)">Delete</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <ModalDialog v-if="creating" title="New user" @close="cancel">
      <form @submit.prevent="save">
        <div class="field">
          <label for="displayName">Name</label>
          <input id="displayName" v-model="form.displayName" class="input" required maxlength="120" />
          <span v-if="errors.displayName" class="field-error">{{ errors.displayName[0] }}</span>
        </div>
        <div class="field">
          <label for="email">Email</label>
          <input id="email" v-model="form.email" class="input" type="email" required />
          <span v-if="errors.email" class="field-error">{{ errors.email[0] }}</span>
        </div>
        <div class="field">
          <label for="password">Password</label>
          <input
            id="password"
            v-model="form.password"
            class="input"
            type="password"
            required
            minlength="8"
            autocomplete="new-password"
          />
          <span v-if="errors.password" class="field-error">{{ errors.password[0] }}</span>
        </div>
        <div class="field">
          <label for="role">Role</label>
          <select id="role" v-model="form.role" class="input">
            <option value="Planner">Planner</option>
            <option value="Admin">Admin</option>
          </select>
        </div>
        <p v-if="otherError" class="field-error" role="alert">{{ otherError }}</p>
        <div class="modal-actions">
          <button type="button" class="btn" @click="cancel">Cancel</button>
          <button type="submit" class="btn btn-primary" :disabled="busy">Create</button>
        </div>
      </form>
    </ModalDialog>
  </div>
</template>

<style scoped>
.actions {
  text-align: right;
}
</style>
