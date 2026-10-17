import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ShiftCard from '@/components/ShiftCard.vue'
import type { Shift, TechnicianLoad } from '@/api/types'

const shift: Shift = {
  id: 'shift-1',
  siteId: 'site-1',
  siteName: 'Hospital',
  siteColor: '#2563eb',
  startsAt: '2026-10-12T06:00:00Z',
  endsAt: '2026-10-12T14:00:00Z',
  hours: 8,
  requiredSkillId: 'skill-1',
  requiredSkillName: 'HVAC',
  headcount: 2,
  notes: null,
  assignments: [{ id: 'a-1', technicianId: 't-1', technicianName: 'Marco Bianchi', source: 'Auto' }],
}

const hvacTech: TechnicianLoad = {
  id: 't-2',
  fullName: 'Elena',
  active: true,
  maxWeeklyHours: 40,
  scheduledHours: 0,
  skills: ['HVAC'],
}
const plumber: TechnicianLoad = {
  id: 't-3',
  fullName: 'Luca',
  active: true,
  maxWeeklyHours: 40,
  scheduledHours: 0,
  skills: ['Plumbing'],
}

describe('ShiftCard', () => {
  it('shows the time, site, skill, assigned names and open slots', () => {
    const wrapper = mount(ShiftCard, { props: { shift, selected: false, dragging: null } })

    expect(wrapper.text()).toContain('06:00–14:00')
    expect(wrapper.text()).toContain('Hospital')
    expect(wrapper.text()).toContain('HVAC')
    expect(wrapper.text()).toContain('Marco Bianchi')
    expect(wrapper.findAll('.slot')).toHaveLength(2)
    expect(wrapper.findAll('.slot.open')).toHaveLength(1)
    expect(wrapper.find('.auto-mark').exists()).toBe(true)
  })

  it('emits select on click and drop with the dragged technician id', async () => {
    const wrapper = mount(ShiftCard, { props: { shift, selected: false, dragging: null } })

    await wrapper.trigger('click')
    expect(wrapper.emitted('select')).toEqual([['shift-1']])

    await wrapper.trigger('drop', { dataTransfer: { getData: () => 't-2' } })
    expect(wrapper.emitted('drop')).toEqual([['shift-1', 't-2']])
  })

  it('hints eligibility while a technician is dragged', () => {
    expect(mount(ShiftCard, { props: { shift, selected: false, dragging: hvacTech } }).classes()).toContain('hint-ok')
    expect(mount(ShiftCard, { props: { shift, selected: false, dragging: plumber } }).classes()).toContain('hint-bad')
    const full = { ...shift, headcount: 1 }
    expect(mount(ShiftCard, { props: { shift: full, selected: false, dragging: hvacTech } }).classes()).toContain('hint-bad')
  })
})
