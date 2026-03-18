<script setup lang="ts">
const { fetchSettings, updateSettings, loading, saving } = useSettings()

interface GeneralSettings {
  id: number
  eMailPollingIntervalSeconds: number
  secretKeyPassphrase: string
  outputPath: string
  processMode: string
  eMailMode: string
  ignoreCertificateErrors: boolean
  heartbeatInterval: string | null
  heartbeatUrl: string
  disableEmailAgeThreshold: boolean
}

const form = ref<GeneralSettings>({
  id: 1,
  eMailPollingIntervalSeconds: 5,
  secretKeyPassphrase: '',
  outputPath: '',
  processMode: 'ConnectPlain',
  eMailMode: 'Imap',
  ignoreCertificateErrors: false,
  heartbeatInterval: null,
  heartbeatUrl: '',
  disableEmailAgeThreshold: false
})

const processModeOptions = [
  { label: 'ConnectPlain (Standard)', value: 'ConnectPlain' },
  { label: 'ConnectEncrypted (PGP)', value: 'ConnectEncrypted' },
  { label: 'ConnectPlainHtml', value: 'ConnectPlainHtml' },
  { label: 'ConnectEncryptedHtml (PGP + HTML)', value: 'ConnectEncryptedHtml' },
  { label: 'ConnectPgpAttachment (PGP-Anhang)', value: 'ConnectPgpAttachment' },
  { label: 'Text', value: 'Text' },
  { label: 'Pdf', value: 'Pdf' }
]

const eMailModeOptions = [
  { label: 'IMAP', value: 'Imap' },
  { label: 'Exchange (EWS)', value: 'Exchange' }
]

onMounted(async () => {
  const data = await fetchSettings<GeneralSettings>('general')
  if (data) form.value = data
})

async function save() {
  await updateSettings('general', form.value)
}
</script>

<template>
  <div>
    <div class="flex items-center justify-between mb-6">
      <div>
        <h1 class="text-2xl font-bold">Allgemeine Einstellungen</h1>
        <p class="text-muted mt-1">Grundlegende MailAgent-Konfiguration</p>
      </div>
      <UButton
        label="Speichern"
        icon="i-lucide-save"
        :loading="saving"
        @click="save"
      />
    </div>

    <div v-if="loading" class="flex items-center justify-center py-20">
      <UIcon name="i-lucide-loader-2" class="w-8 h-8 animate-spin text-primary" />
    </div>

    <div v-else class="space-y-6">
      <!-- E-Mail-Verarbeitung -->
      <UCard>
        <template #header>
          <h2 class="font-semibold">E-Mail-Verarbeitung</h2>
        </template>
        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div>
            <label class="block text-sm font-medium mb-1">Polling-Intervall (Sekunden)</label>
            <UInput v-model.number="form.eMailPollingIntervalSeconds" type="number" :min="1" :max="3600" class="w-full" />
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">E-Mail-Modus</label>
            <USelect v-model="form.eMailMode" :options="eMailModeOptions" class="w-full" />
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">Verarbeitungsmodus</label>
            <USelect v-model="form.processMode" :options="processModeOptions" class="w-full" />
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">Ausgabepfad</label>
            <UInput v-model="form.outputPath" placeholder="z.B. C:\mails\out" class="w-full" />
          </div>
          <div class="md:col-span-2">
            <label class="block text-sm font-medium mb-1">PGP-Passphrase (Secret Key)</label>
            <UInput v-model="form.secretKeyPassphrase" type="password" placeholder="Passphrase für PGP-Schlüssel" class="w-full" />
          </div>
        </div>
      </UCard>

      <!-- Optionen -->
      <UCard>
        <template #header>
          <h2 class="font-semibold">Optionen</h2>
        </template>
        <div class="space-y-4">
          <div class="flex items-center justify-between">
            <div>
              <p class="font-medium text-sm">Zertifikatsfehler ignorieren</p>
              <p class="text-xs text-muted">Nur für Testzwecke verwenden</p>
            </div>
            <USwitch v-model="form.ignoreCertificateErrors" />
          </div>
          <div class="flex items-center justify-between">
            <div>
              <p class="font-medium text-sm">E-Mail-Alterschwelle deaktivieren</p>
              <p class="text-xs text-muted">Warnung: Nur für Testzwecke</p>
            </div>
            <USwitch v-model="form.disableEmailAgeThreshold" />
          </div>
        </div>
      </UCard>

      <!-- Heartbeat -->
      <UCard>
        <template #header>
          <h2 class="font-semibold">Heartbeat</h2>
        </template>
        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div>
            <label class="block text-sm font-medium mb-1">Heartbeat-Intervall (HH:MM:SS)</label>
            <UInput v-model="form.heartbeatInterval" placeholder="z.B. 00:05:00 (leer = deaktiviert)" class="w-full" />
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">Heartbeat URL</label>
            <UInput v-model="form.heartbeatUrl" placeholder="https://example.com/heartbeat" class="w-full" />
          </div>
        </div>
      </UCard>

      <div class="flex justify-end">
        <UButton
          label="Speichern"
          icon="i-lucide-save"
          :loading="saving"
          @click="save"
        />
      </div>
    </div>
  </div>
</template>
