<script setup lang="ts">
const { fetchAllSettings, loading } = useSettings()

interface SectionInfo {
  slug: string
  label: string
  description: string
  icon: string
  to: string
}

const sections: SectionInfo[] = [
  { slug: 'general', label: 'Allgemeine Einstellungen', description: 'Polling-Intervall, Verarbeitungsmodus, Heartbeat', icon: 'i-lucide-settings', to: '/settings/general' },
  { slug: 'mailboxes', label: 'E-Mail-Konten', description: 'IMAP/Exchange-Postfächer konfigurieren', icon: 'i-lucide-mail', to: '/settings/mailboxes' },
  { slug: 'pattern', label: 'Muster (Pattern)', description: 'Regex-Muster für die Alarmauswertung', icon: 'i-lucide-regex', to: '/settings/pattern' }
]

interface OverviewSection {
  section: string
  exists: boolean
}

const allSettings = ref<OverviewSection[]>([])

onMounted(async () => {
  const data = await fetchAllSettings()
  if (data) allSettings.value = data.sections ?? []
})

function sectionExists(slug: string): boolean {
  return allSettings.value.some(s => s.section === slug && s.exists)
}

const needsSetup = computed(() => {
  if (allSettings.value.length === 0) return false
  return !sectionExists('mailboxes')
})
</script>

<template>
  <div>
    <div class="mb-8">
      <h1 class="text-2xl font-bold">Dashboard</h1>
      <p class="text-muted mt-1">Übersicht der MailAgent-Konfiguration</p>
    </div>

    <div v-if="loading" class="flex items-center justify-center py-20">
      <UIcon name="i-lucide-loader-2" class="w-8 h-8 animate-spin text-primary" />
    </div>

    <div v-else>
      <!-- Setup banner -->
      <UCard v-if="needsSetup" class="mb-6 ring-2 ring-primary">
        <div class="flex items-center gap-4">
          <div class="p-3 rounded-full bg-primary/10">
            <UIcon name="i-lucide-rocket" class="w-8 h-8 text-primary" />
          </div>
          <div class="flex-1">
            <h3 class="font-semibold">Ersteinrichtung erforderlich</h3>
            <p class="text-sm text-muted mt-1">
              Es sind noch keine E-Mail-Konten konfiguriert. Konfigurieren Sie mindestens ein Postfach, um den MailAgent zu starten.
            </p>
          </div>
          <UButton
            label="E-Mail-Konto hinzufügen"
            icon="i-lucide-arrow-right"
            trailing
            to="/settings/mailboxes"
          />
        </div>
      </UCard>

      <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
        <NuxtLink
          v-for="section in sections"
          :key="section.slug"
          :to="section.to"
          class="block"
        >
          <UCard class="hover:ring-2 hover:ring-primary transition-all cursor-pointer h-full">
            <div class="flex items-start gap-4">
              <div class="p-2 rounded-lg bg-primary/10">
                <UIcon :name="section.icon" class="w-6 h-6 text-primary" />
              </div>
              <div class="flex-1 min-w-0">
                <h3 class="font-semibold">{{ section.label }}</h3>
                <p class="text-sm text-muted mt-1">{{ section.description }}</p>
                <UBadge
                  v-if="sectionExists(section.slug)"
                  variant="subtle"
                  color="success"
                  class="mt-2"
                >
                  Konfiguriert
                </UBadge>
                <UBadge
                  v-else-if="allSettings.length > 0"
                  variant="subtle"
                  color="neutral"
                  class="mt-2"
                >
                  Nicht konfiguriert
                </UBadge>
              </div>
            </div>
          </UCard>
        </NuxtLink>
      </div>
    </div>
  </div>
</template>
