<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import type { Shift, ShiftRequest, Site, Skill } from '@/api/types'
import { ApiError } from '@/api/client'
import { dateOf, timeOf, toIso } from '@/lib/week'
import ModalDialog from './ModalDialog.vue'

const props = defineProps<{
  sites: Site[]
  skills: Skill[]
  /** Edit this shift; omitted for a new one. */
  shift?: Shift
  /** Default day for a new shift. */
  day?: string
  /** True while the parent is waiting on the API, so the form cannot be submitted twice. */
  saving?: boolean
}>()

const emit = defineEmits<{
  close: []
  save: [request: ShiftRequest, id?: string]
}>()

const form = reactive({
  siteId: props.shift?.siteId ?? props.sites[0]?.id ?? '',
  day: props.shift ? dateOf(props.shift.startsAt) : (props.day ?? ''),
  start: props.shift ? timeOf(props.shift.startsAt) : '08:00',
  end: props.shift ? timeOf(props.shift.endsAt) : '16:00',
  requiredSkillId: props.shift?.requiredSkillId ?? props.skills[0]?.id ?? '',
  headcount: props.shift?.headcount ?? 1,
  notes: props.shift?.notes ?? '',
})

const errors = ref<Record<string, string[]>>({})
const busy = ref(false)
const locked = computed(() => (props.shift?.assignments.length ?? 0) > 0)
const incomplete = computed(() => props.sites.length === 0 || props.skills.length === 0)

function error(field: string) {
  return errors.value[field]?.[0]
}

/** The first message for a field the form does not render, so no validation error goes unseen. */
const shownFields = ['siteId', 'endsAt', 'headcount', 'form']
const otherError = computed(() => {
  const key = Object.keys(errors.value).find((k) => !shownFields.includes(k))
  return key ? errors.value[key]?.[0] : undefined
})

async function submit() {
  errors.value = {}
  if (form.end <= form.start) {
    errors.value = { endsAt: ['Must be after the start.'] }
    return
  }
  busy.value = true
  try {
    emit(
      'save',
      {
        siteId: form.siteId,
        startsAt: toIso(form.day, form.start),
        endsAt: toIso(form.day, form.end),
        requiredSkillId: form.requiredSkillId,
        headcount: Number(form.headcount),
        notes: form.notes.trim() === '' ? null : form.notes.trim(),
      },
      props.shift?.id,
    )
  } finally {
    busy.value = false
  }
}

/** Called by the parent when the API rejects the request. */
function showErrors(e: ApiError) {
  errors.value = Object.keys(e.errors).length > 0 ? e.errors : { form: [e.detail ?? e.title] }
}

defineExpose({ showErrors })
</script>

<template>
  <ModalDialog :title="shift ? 'Edit shift' : 'New shift'" @close="emit('close')">
    <form @submit.prevent="submit">
      <div class="field">
        <label for="site">Site</label>
        <select id="site" v-model="form.siteId" class="input" required>
          <option v-for="site in sites" :key="site.id" :value="site.id">{{ site.name }}</option>
        </select>
        <span v-if="error('siteId')" class="field-error">{{ error('siteId') }}</span>
      </div>

      <div class="field">
        <label for="day">Day</label>
        <input id="day" v-model="form.day" class="input" type="date" required :disabled="locked" />
      </div>

      <div class="form-row">
        <div class="field">
          <label for="start">Start (UTC)</label>
          <input id="start" v-model="form.start" class="input" type="time" required :disabled="locked" />
        </div>
        <div class="field">
          <label for="end">End (UTC)</label>
          <input
            id="end"
            v-model="form.end"
            class="input"
            type="time"
            required
            :disabled="locked"
            :aria-invalid="!!error('endsAt')"
          />
          <span v-if="error('endsAt')" class="field-error">{{ error('endsAt') }}</span>
        </div>
      </div>

      <div class="form-row">
        <div class="field">
          <label for="skill">Required skill</label>
          <select id="skill" v-model="form.requiredSkillId" class="input" required :disabled="locked">
            <option v-for="skill in skills" :key="skill.id" :value="skill.id">{{ skill.name }}</option>
          </select>
        </div>
        <div class="field">
          <label for="headcount">Technicians needed</label>
          <input
            id="headcount"
            v-model.number="form.headcount"
            class="input"
            type="number"
            min="1"
            max="20"
            required
            :aria-invalid="!!error('headcount')"
          />
          <span v-if="error('headcount')" class="field-error">{{ error('headcount') }}</span>
        </div>
      </div>

      <div class="field">
        <label for="notes">Notes</label>
        <textarea id="notes" v-model="form.notes" class="input" rows="2" maxlength="500"></textarea>
      </div>

      <p v-if="locked" class="faint">
        Time and skill are locked while technicians are assigned. Unassign them first to change those.
      </p>
      <p v-if="incomplete" class="field-error">Create a site and a skill first.</p>
      <p v-if="error('form')" class="field-error" role="alert">{{ error('form') }}</p>
      <p v-else-if="otherError" class="field-error" role="alert">{{ otherError }}</p>

      <div class="modal-actions">
        <button type="button" class="btn" @click="emit('close')">Cancel</button>
        <button type="submit" class="btn btn-primary" :disabled="busy || saving || incomplete">
          {{ shift ? 'Save' : 'Create shift' }}
        </button>
      </div>
    </form>
  </ModalDialog>
</template>
