<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { skillsApi, techniciansApi } from '@/api'
import { ApiError } from '@/api/client'
import type { Skill, Technician, TechnicianRequest } from '@/api/types'
import ModalDialog from '@/components/ModalDialog.vue'
import { useApiErrors } from '@/composables/useApiErrors'
import { useToastStore } from '@/stores/toast'

const toast = useToastStore()
const { fail } = useApiErrors()
const technicians = ref<Technician[]>([])
const skills = ref<Skill[]>([])
const editing = ref<Technician | null | 'new'>(null)
const newSkill = ref('')
const errors = ref<Record<string, string[]>>({})
const busy = ref(false)

const form = reactive<TechnicianRequest>({ fullName: '', email: '', phone: '', maxWeeklyHours: 40, active: true, skillIds: [] })

/** The first message for a field the form does not render, so no validation error goes unseen. */
const shownFields = ['fullName', 'maxWeeklyHours']
const otherError = computed(() => {
  const key = Object.keys(errors.value).find((k) => !shownFields.includes(k))
  return key ? errors.value[key]?.[0] : undefined
})

async function load() {
  ;[technicians.value, skills.value] = await Promise.all([techniciansApi.list(), skillsApi.list()])
}

onMounted(() => load().catch(fail))

function open(t: Technician | 'new') {
  errors.value = {}
  Object.assign(
    form,
    t === 'new'
      ? { fullName: '', email: '', phone: '', maxWeeklyHours: 40, active: true, skillIds: [] }
      : {
          fullName: t.fullName,
          email: t.email ?? '',
          phone: t.phone ?? '',
          maxWeeklyHours: t.maxWeeklyHours,
          active: t.active,
          skillIds: t.skills.map((s) => s.id),
        },
  )
  editing.value = t
}

async function save() {
  errors.value = {}
  busy.value = true
  const request: TechnicianRequest = {
    ...form,
    email: form.email || null,
    phone: form.phone || null,
    maxWeeklyHours: Number(form.maxWeeklyHours),
  }
  try {
    if (editing.value === 'new') await techniciansApi.create(request)
    else if (editing.value) await techniciansApi.update(editing.value.id, request)
    editing.value = null
    toast.success('Technician saved')
    await load()
  } catch (e) {
    if (e instanceof ApiError && e.status === 400) errors.value = e.errors
    else fail(e)
  } finally {
    busy.value = false
  }
}

async function remove(t: Technician) {
  if (!window.confirm(`Delete ${t.fullName} and all their assignments?`)) return
  try {
    await techniciansApi.remove(t.id)
    await load()
  } catch (e) {
    fail(e)
  }
}

async function addSkill() {
  const name = newSkill.value.trim()
  if (!name) return
  try {
    await skillsApi.create(name)
    newSkill.value = ''
    await load()
  } catch (e) {
    fail(e)
  }
}

async function removeSkill(s: Skill) {
  if (!window.confirm(`Remove the skill "${s.name}"?`)) return
  try {
    await skillsApi.remove(s.id)
    await load()
  } catch (e) {
    fail(e)
  }
}

function toggleSkill(id: string) {
  form.skillIds = form.skillIds.includes(id) ? form.skillIds.filter((x) => x !== id) : [...form.skillIds, id]
}
</script>

<template>
  <div class="page">
    <header class="page-header">
      <h1>Technicians</h1>
      <button class="btn btn-primary" @click="open('new')">+ Technician</button>
    </header>

    <div class="card">
      <table class="table">
        <thead>
          <tr>
            <th>Name</th>
            <th>Contact</th>
            <th>Skills</th>
            <th>Max h/week</th>
            <th>Status</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="t in technicians" :key="t.id">
            <td>
              <strong>{{ t.fullName }}</strong>
            </td>
            <td class="muted">{{ t.email }}<br v-if="t.email && t.phone" />{{ t.phone }}</td>
            <td>
              <span v-for="s in t.skills" :key="s.id" class="chip skill">{{ s.name }}</span>
            </td>
            <td class="mono">{{ t.maxWeeklyHours }}</td>
            <td>
              <span class="chip" :class="t.active ? 'chip-success' : 'chip-danger'">{{ t.active ? 'Active' : 'Inactive' }}</span>
            </td>
            <td class="actions">
              <button class="btn btn-sm" @click="open(t)">Edit</button>
              <button class="btn btn-sm btn-ghost" @click="remove(t)">Delete</button>
            </td>
          </tr>
          <tr v-if="technicians.length === 0">
            <td colspan="6" class="empty">No technicians yet.</td>
          </tr>
        </tbody>
      </table>
    </div>

    <section class="skills card">
      <h2>Skills</h2>
      <p class="muted">A shift requires exactly one skill; a technician can have many.</p>
      <div class="skill-list">
        <span v-for="s in skills" :key="s.id" class="chip chip-accent">
          {{ s.name }}
          <button class="chip-x" :aria-label="`Remove ${s.name}`" @click="removeSkill(s)">✕</button>
        </span>
      </div>
      <form class="row" @submit.prevent="addSkill">
        <input v-model="newSkill" class="input" placeholder="New skill, e.g. Electrical" maxlength="80" aria-label="New skill" />
        <button class="btn" type="submit">Add</button>
      </form>
    </section>

    <ModalDialog v-if="editing" :title="editing === 'new' ? 'New technician' : 'Edit technician'" @close="editing = null">
      <form @submit.prevent="save">
        <div class="field">
          <label for="fullName">Full name</label>
          <input id="fullName" v-model="form.fullName" class="input" required maxlength="120" :aria-invalid="!!errors.fullName" />
          <span v-if="errors.fullName" class="field-error">{{ errors.fullName[0] }}</span>
        </div>
        <div class="form-row">
          <div class="field">
            <label for="email">Email</label>
            <input id="email" v-model="form.email" class="input" type="email" maxlength="200" />
          </div>
          <div class="field">
            <label for="phone">Phone</label>
            <input id="phone" v-model="form.phone" class="input" maxlength="40" />
          </div>
        </div>
        <div class="form-row">
          <div class="field">
            <label for="max">Max hours per week</label>
            <input id="max" v-model.number="form.maxWeeklyHours" class="input" type="number" min="1" max="80" required />
            <span v-if="errors.maxWeeklyHours" class="field-error">{{ errors.maxWeeklyHours[0] }}</span>
          </div>
          <fieldset class="field">
            <legend>Status</legend>
            <label class="checkbox"><input v-model="form.active" type="checkbox" /> Active</label>
          </fieldset>
        </div>
        <fieldset class="field">
          <legend>Skills</legend>
          <div class="skill-list">
            <button
              v-for="s in skills"
              :key="s.id"
              type="button"
              class="chip toggle"
              :class="{ 'chip-accent': form.skillIds.includes(s.id) }"
              :aria-pressed="form.skillIds.includes(s.id)"
              @click="toggleSkill(s.id)"
            >
              {{ s.name }}
            </button>
          </div>
        </fieldset>
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
.skill {
  margin: 2px 4px 2px 0;
}

.actions {
  text-align: right;
  white-space: nowrap;
}

.skills {
  margin-top: 20px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.skill-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.chip-x {
  border: none;
  background: transparent;
  padding: 0 0 0 2px;
  cursor: pointer;
  color: inherit;
  font-size: 11px;
}

.toggle {
  cursor: pointer;
}
</style>
