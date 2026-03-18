export function useSettings() {
  const toast = useToast()
  const config = useRuntimeConfig()
  const apiBase = config.public.apiBase

  const loading = ref(false)
  const saving = ref(false)

  async function fetchAllSettings(): Promise<Record<string, any> | null> {
    loading.value = true
    try {
      return await $fetch<Record<string, any>>(`${apiBase}/settings`)
    } catch (e: any) {
      toast.add({
        title: 'Fehler beim Laden',
        description: e?.data?.message || e?.message || 'Einstellungen konnten nicht geladen werden.',
        color: 'error'
      })
      return null
    } finally {
      loading.value = false
    }
  }

  async function fetchSettings<T = any>(sectionName: string): Promise<T | null> {
    loading.value = true
    try {
      return await $fetch<T>(`${apiBase}/settings/${sectionName}`)
    } catch (e: any) {
      toast.add({
        title: 'Fehler beim Laden',
        description: e?.data?.message || e?.message || `${sectionName} konnte nicht geladen werden.`,
        color: 'error'
      })
      return null
    } finally {
      loading.value = false
    }
  }

  async function updateSettings(sectionName: string, data: any): Promise<boolean> {
    saving.value = true
    try {
      await $fetch(`${apiBase}/settings/${sectionName}`, {
        method: 'PUT',
        body: data
      })
      toast.add({
        title: 'Gespeichert',
        description: `Einstellungen wurden erfolgreich gespeichert.`,
        color: 'success'
      })
      return true
    } catch (e: any) {
      toast.add({
        title: 'Fehler beim Speichern',
        description: e?.data?.message || e?.message || `Einstellungen konnten nicht gespeichert werden.`,
        color: 'error'
      })
      return false
    } finally {
      saving.value = false
    }
  }

  return {
    loading,
    saving,
    fetchAllSettings,
    fetchSettings,
    updateSettings
  }
}
