<script setup lang="ts">
const toast = useToast()
const config = useRuntimeConfig()
const apiBase = config.public.apiBase

interface EmailAccount {
  id: number
  name: string
  apiKey: string
  eMailHost: string
  eMailPort: number
  eMailUsername: string
  eMailPassword: string
  eMailSubjectFilter: string
  eMailSenderFilter: string
  authenticationType: string
}

const accounts = ref<EmailAccount[]>([])
const loading = ref(false)
const saving = ref(false)

const modalOpen = ref(false)
const editingAccount = ref<EmailAccount | null>(null)

const confirmDeleteOpen = ref(false)
const accountToDelete = ref<EmailAccount | null>(null)

const authTypeOptions = [
  { label: 'Basic (Benutzername/Passwort)', value: 'Basic' },
  { label: 'O365 (OAuth2)', value: 'O365' }
]

const emptyAccount = (): EmailAccount => ({
  id: 0,
  name: '',
  apiKey: '',
  eMailHost: '',
  eMailPort: 993,
  eMailUsername: '',
  eMailPassword: '',
  eMailSubjectFilter: '',
  eMailSenderFilter: '',
  authenticationType: 'Basic'
})

const formAccount = ref<EmailAccount>(emptyAccount())

async function loadAccounts() {
  loading.value = true
  try {
    accounts.value = await $fetch<EmailAccount[]>(`${apiBase}/settings/mailboxes`)
  } catch (e: any) {
    toast.add({ title: 'Fehler', description: 'Konten konnten nicht geladen werden.', color: 'error' })
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editingAccount.value = null
  formAccount.value = emptyAccount()
  modalOpen.value = true
}

function openEdit(account: EmailAccount) {
  editingAccount.value = account
  formAccount.value = { ...account }
  modalOpen.value = true
}

async function saveAccount() {
  saving.value = true
  try {
    if (editingAccount.value) {
      await $fetch(`${apiBase}/settings/mailboxes/${editingAccount.value.id}`, {
        method: 'PUT',
        body: formAccount.value
      })
      toast.add({ title: 'Gespeichert', description: 'E-Mail-Konto wurde aktualisiert.', color: 'success' })
    } else {
      await $fetch(`${apiBase}/settings/mailboxes`, {
        method: 'POST',
        body: formAccount.value
      })
      toast.add({ title: 'Erstellt', description: 'E-Mail-Konto wurde hinzugefügt.', color: 'success' })
    }
    modalOpen.value = false
    await loadAccounts()
  } catch (e: any) {
    toast.add({ title: 'Fehler', description: e?.data?.message || 'Speichern fehlgeschlagen.', color: 'error' })
  } finally {
    saving.value = false
  }
}

function confirmDelete(account: EmailAccount) {
  accountToDelete.value = account
  confirmDeleteOpen.value = true
}

async function deleteAccount() {
  if (!accountToDelete.value) return
  try {
    await $fetch(`${apiBase}/settings/mailboxes/${accountToDelete.value.id}`, { method: 'DELETE' })
    toast.add({ title: 'Gelöscht', description: 'E-Mail-Konto wurde entfernt.', color: 'success' })
    confirmDeleteOpen.value = false
    accountToDelete.value = null
    await loadAccounts()
  } catch (e: any) {
    toast.add({ title: 'Fehler', description: 'Löschen fehlgeschlagen.', color: 'error' })
  }
}

const authenticating = ref<number | null>(null)

async function authenticateO365(account: EmailAccount) {
  authenticating.value = account.id
  try {
    toast.add({ title: 'O365-Authentifizierung', description: 'Browser wird geöffnet...', color: 'info' })
    await $fetch(`${apiBase}/auth/o365/${account.id}`, { method: 'POST' })
    toast.add({ title: 'Authentifizierung erfolgreich', description: `${account.eMailUsername} wurde erfolgreich authentifiziert.`, color: 'success' })
  } catch (e: any) {
    toast.add({ title: 'Authentifizierung fehlgeschlagen', description: e?.data?.detail || e?.message || 'O365-Authentifizierung fehlgeschlagen.', color: 'error' })
  } finally {
    authenticating.value = null
  }
}

onMounted(loadAccounts)
</script>

<template>
  <div>
    <div class="flex items-center justify-between mb-6">
      <div>
        <h1 class="text-2xl font-bold">E-Mail-Konten</h1>
        <p class="text-muted mt-1">Postfächer für die E-Mail-Überwachung konfigurieren</p>
      </div>
      <UButton
        label="Konto hinzufügen"
        icon="i-lucide-plus"
        @click="openCreate"
      />
    </div>

    <div v-if="loading" class="flex items-center justify-center py-20">
      <UIcon name="i-lucide-loader-2" class="w-8 h-8 animate-spin text-primary" />
    </div>

    <div v-else-if="accounts.length === 0" class="text-center py-16">
      <UIcon name="i-lucide-mail-x" class="w-12 h-12 text-muted mx-auto mb-4" />
      <h3 class="text-lg font-semibold">Keine E-Mail-Konten konfiguriert</h3>
      <p class="text-muted mt-2 mb-4">Fügen Sie ein Postfach hinzu, um die E-Mail-Überwachung zu starten.</p>
      <UButton label="Erstes Konto hinzufügen" icon="i-lucide-plus" @click="openCreate" />
    </div>

    <div v-else class="space-y-4">
      <UCard v-for="account in accounts" :key="account.id">
        <div class="flex items-start gap-4">
          <div class="p-2 rounded-lg bg-primary/10">
            <UIcon name="i-lucide-mail" class="w-6 h-6 text-primary" />
          </div>
          <div class="flex-1 min-w-0">
            <div class="flex items-center gap-2 flex-wrap">
              <h3 class="font-semibold">{{ account.name }}</h3>
              <UBadge variant="subtle" :color="account.authenticationType === 'O365' ? 'info' : 'neutral'">
                {{ account.authenticationType }}
              </UBadge>
            </div>
            <p class="text-sm text-muted mt-1">{{ account.eMailUsername }} @ {{ account.eMailHost }}:{{ account.eMailPort }}</p>
            <div v-if="account.eMailSubjectFilter || account.eMailSenderFilter" class="text-xs text-muted mt-1">
              <span v-if="account.eMailSubjectFilter">Betreff-Filter: {{ account.eMailSubjectFilter }}</span>
              <span v-if="account.eMailSubjectFilter && account.eMailSenderFilter"> · </span>
              <span v-if="account.eMailSenderFilter">Absender-Filter: {{ account.eMailSenderFilter }}</span>
            </div>
          </div>
          <div class="flex items-center gap-2 shrink-0">
            <UButton
              v-if="account.authenticationType === 'O365'"
              icon="i-lucide-key"
              label="O365 Auth"
              variant="outline"
              size="sm"
              :loading="authenticating === account.id"
              @click="authenticateO365(account)"
            />
            <UButton
              icon="i-lucide-pencil"
              variant="ghost"
              size="sm"
              @click="openEdit(account)"
            />
            <UButton
              icon="i-lucide-trash-2"
              variant="ghost"
              color="error"
              size="sm"
              @click="confirmDelete(account)"
            />
          </div>
        </div>
      </UCard>
    </div>

    <!-- Create/Edit Modal -->
    <UModal v-model:open="modalOpen" :ui="{ width: 'sm:max-w-2xl' }">
      <template #content>
        <div class="p-6 space-y-5">
          <div>
            <h2 class="text-lg font-semibold">{{ editingAccount ? 'Konto bearbeiten' : 'Neues Konto' }}</h2>
          </div>

          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div class="sm:col-span-2">
              <label class="block text-sm font-medium mb-1">Name</label>
              <UInput v-model="formAccount.name" placeholder="z.B. Hauptpostfach" />
            </div>
            <div class="sm:col-span-2">
              <label class="block text-sm font-medium mb-1">Connect API Key</label>
              <UInput v-model="formAccount.apiKey" type="password" placeholder="API Key" />
            </div>
            <div>
              <label class="block text-sm font-medium mb-1">Authentifizierungstyp</label>
              <USelect v-model="formAccount.authenticationType" :options="authTypeOptions" />
            </div>
            <div>
              <label class="block text-sm font-medium mb-1">E-Mail-Port</label>
              <UInput v-model.number="formAccount.eMailPort" type="number" :min="1" :max="65535" />
            </div>
            <div class="sm:col-span-2">
              <label class="block text-sm font-medium mb-1">E-Mail-Host</label>
              <UInput v-model="formAccount.eMailHost" placeholder="z.B. imap.example.com oder outlook.office365.com" />
            </div>
            <div>
              <label class="block text-sm font-medium mb-1">Benutzername</label>
              <UInput v-model="formAccount.eMailUsername" placeholder="user@example.com" />
            </div>
            <div>
              <label class="block text-sm font-medium mb-1">Passwort</label>
              <UInput v-model="formAccount.eMailPassword" type="password" placeholder="Passwort (leer bei O365)" :disabled="formAccount.authenticationType === 'O365'" />
            </div>
            <div>
              <label class="block text-sm font-medium mb-1">Betreff-Filter (Regex)</label>
              <UInput v-model="formAccount.eMailSubjectFilter" placeholder="z.B. .*Alarm.*" />
            </div>
            <div>
              <label class="block text-sm font-medium mb-1">Absender-Filter (Regex)</label>
              <UInput v-model="formAccount.eMailSenderFilter" placeholder="z.B. alarmierung@leitstelle.de" />
            </div>
          </div>

          <div v-if="formAccount.authenticationType === 'O365'" class="p-3 rounded-lg bg-info/10 border border-info/20 text-sm text-muted">
            <div class="flex gap-2">
              <UIcon name="i-lucide-info" class="w-4 h-4 text-info shrink-0 mt-0.5" />
              <p>Nach dem Speichern können Sie die O365-Authentifizierung über die Schaltfläche <strong>O365 Auth</strong> in der Kontenübersicht starten. Ein Browser-Fenster öffnet sich für die interaktive Anmeldung.</p>
            </div>
          </div>

          <div class="flex items-center justify-between pt-2 border-t border-default">
            <UButton label="Abbrechen" variant="ghost" @click="modalOpen = false" />
            <UButton
              :label="editingAccount ? 'Speichern' : 'Hinzufügen'"
              icon="i-lucide-save"
              :loading="saving"
              @click="saveAccount"
            />
          </div>
        </div>
      </template>
    </UModal>

    <!-- Delete Confirmation Modal -->
    <UModal v-model:open="confirmDeleteOpen">
      <template #content>
        <div class="p-6 space-y-4">
          <div class="flex items-center gap-3">
            <div class="p-2 rounded-full bg-error/10">
              <UIcon name="i-lucide-trash-2" class="w-5 h-5 text-error" />
            </div>
            <h2 class="text-lg font-semibold">Konto löschen</h2>
          </div>
          <p class="text-sm text-muted">
            E-Mail-Konto <strong>{{ accountToDelete?.name }}</strong> wirklich löschen? Diese Aktion kann nicht rückgängig gemacht werden.
          </p>
          <div class="flex items-center justify-end gap-3 pt-2 border-t border-default">
            <UButton label="Abbrechen" variant="ghost" @click="confirmDeleteOpen = false" />
            <UButton label="Löschen" color="error" icon="i-lucide-trash-2" @click="deleteAccount" />
          </div>
        </div>
      </template>
    </UModal>
  </div>
</template>
