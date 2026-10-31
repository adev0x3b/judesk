<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { sitesApi } from '@/api'
import { ApiError } from '@/api/client'
import type { Site, SiteRequest } from '@/api/types'
import ModalDialog from '@/components/ModalDialog.vue'
import { useApiErrors } from '@/composables/useApiErrors'
import { useToastStore } from '@/stores/toast'

const toast = useToastStore()
const { fail } = useApiErrors()
const sites = ref<Site[]>([])
const editing = ref<Site | null | 'new'>(null)
const errors = ref<Record<string, string[]>>({})
const busy = ref(false)
const form = reactive<SiteRequest>({ name: '', address: '', color: '#2563eb' })

/** The first message for a field the form does not render, so no validation error goes unseen. */
const shownFields = ['name', 'color']
const otherError = computed(() => {
  const key = Object.keys(errors.value).find((k) => !shownFields.includes(k))
  return key ? errors.value[key]?.[0] : undefined
})

async function load() {
  sites.value = await sitesApi.list()
}

onMounted(() => load().catch(fail))

function open(s: Site | 'new') {
  errors.value = {}
  Object.assign(
    form,
    s === 'new'
      ? { name: '', address: '', color: '#2563eb' }
      : { name: s.name, address: s.address ?? '', color: s.color ?? '#2563eb' },
  )
  editing.value = s
}

async function save() {
  errors.value = {}
  busy.value = true
  const request: SiteRequest = { name: form.name, address: form.address || null, color: form.color || null }
  try {
    if (editing.value === 'new') await sitesApi.create(request)
    else if (editing.value) await sitesApi.update(editing.value.id, request)
    editing.value = null
    toast.success('Site saved')
    await load()
  } catch (e) {
    if (e instanceof ApiError && e.status === 400) errors.value = e.errors
    else fail(e)
  } finally {
    busy.value = false
  }
}

async function remove(s: Site) {
  if (!window.confirm(`Delete ${s.name} and all its shifts?`)) return
  try {
    await sitesApi.remove(s.id)
    await load()
  } catch (e) {
    fail(e)
  }
}
</script>

<template>
  <div class="page">
    <header class="page-header">
      <h1>Sites</h1>
      <button class="btn btn-primary" @click="open('new')">+ Site</button>
    </header>

    <div class="card">
      <table class="table">
        <thead>
          <tr>
            <th></th>
            <th>Name</th>
            <th>Address</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="s in sites" :key="s.id">
            <td class="swatch-cell"><span class="swatch" :style="{ background: s.color ?? 'var(--text-3)' }"></span></td>
            <td>
              <strong>{{ s.name }}</strong>
            </td>
            <td class="muted">{{ s.address }}</td>
            <td class="actions">
              <button class="btn btn-sm" @click="open(s)">Edit</button>
              <button class="btn btn-sm btn-ghost" @click="remove(s)">Delete</button>
            </td>
          </tr>
          <tr v-if="sites.length === 0">
            <td colspan="4" class="empty">No sites yet.</td>
          </tr>
        </tbody>
      </table>
    </div>

    <ModalDialog v-if="editing" :title="editing === 'new' ? 'New site' : 'Edit site'" @close="editing = null">
      <form @submit.prevent="save">
        <div class="field">
          <label for="name">Name</label>
          <input id="name" v-model="form.name" class="input" required maxlength="120" :aria-invalid="!!errors.name" />
          <span v-if="errors.name" class="field-error">{{ errors.name[0] }}</span>
        </div>
        <div class="field">
          <label for="address">Address</label>
          <input id="address" v-model="form.address" class="input" maxlength="300" />
        </div>
        <div class="field">
          <label for="color">Colour</label>
          <input id="color" v-model="form.color" class="input color" type="color" :aria-invalid="!!errors.color" />
          <span v-if="errors.color" class="field-error">{{ errors.color[0] }}</span>
        </div>
        <p v-if="otherError" class="field-error" role="alert">{{ otherError }}</p>
        <div class="modal-actions">
          <button type="button" class="btn" @click="editing = null">Cancel</button>
          <button type="submit" class="btn btn-primary" :disabled="busy">Save</button>
        </div>
      </form>
    </ModalDialog>
  </div>
</template>

<style scoped>
.swatch-cell {
  width: 32px;
}

.swatch {
  display: inline-block;
  width: 14px;
  height: 14px;
  border-radius: 4px;
}

.actions {
  text-align: right;
  white-space: nowrap;
}

.color {
  width: 64px;
  height: 36px;
  padding: 2px;
}
</style>
