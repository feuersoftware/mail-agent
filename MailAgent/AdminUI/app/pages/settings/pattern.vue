<script setup lang="ts">
const { fetchSettings, updateSettings, loading, saving } = useSettings()

interface AdditionalProperty {
  id: number
  patternSettingsId: number
  name: string
  pattern: string
}

interface PatternSettings {
  id: number
  startPattern: string
  numberPattern: string
  keywordPattern: string
  factsPattern: string
  streetPattern: string
  houseNumberPattern: string
  cityPattern: string
  districtPattern: string
  zipCodePattern: string
  ricPattern: string
  longitudePattern: string
  latitudePattern: string
  reporterNamePattern: string
  reporterPhonePattern: string
  additionalProperties: AdditionalProperty[]
}

const form = ref<PatternSettings>({
  id: 1,
  startPattern: '',
  numberPattern: '',
  keywordPattern: '',
  factsPattern: '',
  streetPattern: '',
  houseNumberPattern: '',
  cityPattern: '',
  districtPattern: '',
  zipCodePattern: '',
  ricPattern: '',
  longitudePattern: '',
  latitudePattern: '',
  reporterNamePattern: '',
  reporterPhonePattern: '',
  additionalProperties: []
})

onMounted(async () => {
  const data = await fetchSettings<PatternSettings>('pattern')
  if (data) form.value = data
})

async function save() {
  await updateSettings('pattern', form.value)
}

function addProperty() {
  form.value.additionalProperties.push({ id: 0, patternSettingsId: 1, name: '', pattern: '' })
}

function removeProperty(index: number) {
  form.value.additionalProperties.splice(index, 1)
}

type PatternField = { key: keyof Omit<PatternSettings, 'additionalProperties' | 'id'>; label: string; hint: string }

const patternFields: PatternField[] = [
  { key: 'startPattern', label: 'Alarmbeginn', hint: 'Zeitstempel des Alarmbeginns' },
  { key: 'numberPattern', label: 'Einsatznummer', hint: 'z.B. eine fortlaufende Nummer wie "E2024-001234"' },
  { key: 'keywordPattern', label: 'Stichwort', hint: 'z.B. "B3" oder "THL1"' },
  { key: 'factsPattern', label: 'Sachverhalt', hint: 'Freitext-Beschreibung des Einsatzes' },
  { key: 'reporterNamePattern', label: 'Meldername', hint: 'Name des Meldenden' },
  { key: 'reporterPhonePattern', label: 'Melder-Telefonnummer', hint: 'Telefonnummer des Meldenden' },
  { key: 'cityPattern', label: 'Stadt', hint: 'Ortsname des Einsatzortes' },
  { key: 'streetPattern', label: 'Straße', hint: 'Straßenname des Einsatzortes' },
  { key: 'houseNumberPattern', label: 'Hausnummer', hint: 'Hausnummer am Einsatzort' },
  { key: 'zipCodePattern', label: 'PLZ', hint: 'Postleitzahl des Einsatzortes' },
  { key: 'districtPattern', label: 'Ortsteil', hint: 'Ortsteil oder Stadtteil' },
  { key: 'latitudePattern', label: 'Breitengrad', hint: 'Geografische Breite (Dezimalgrad)' },
  { key: 'longitudePattern', label: 'Längengrad', hint: 'Geografische Länge (Dezimalgrad)' },
  { key: 'ricPattern', label: 'RIC', hint: 'Radio Identification Code' }
]

const testInput = ref('')
const testingField = ref<string | null>(null)

function toggleTest(fieldKey: string) {
  testingField.value = testingField.value === fieldKey ? null : fieldKey
}

function getMatch(pattern: string): { match: boolean; groups: string[] } {
  if (!pattern || !testInput.value) return { match: false, groups: [] }
  try {
    const regex = new RegExp(pattern)
    const result = regex.exec(testInput.value)
    if (result) {
      const groups = result.slice(1).filter(g => g !== undefined)
      return { match: true, groups }
    }
    return { match: false, groups: [] }
  } catch {
    return { match: false, groups: [] }
  }
}

function isValidRegex(pattern: string): boolean {
  if (!pattern) return true
  try {
    new RegExp(pattern)
    return true
  } catch {
    return false
  }
}
</script>

<template>
  <div>
    <div class="flex items-center justify-between mb-6">
      <div>
        <h1 class="text-2xl font-bold">Muster (Pattern)</h1>
        <p class="text-muted mt-1">Regex-Muster für die Alarm-Auswertung</p>
      </div>
      <UButton
        label="Speichern"
        icon="i-lucide-save"
        :loading="saving"
        @click="save"
      />
    </div>

    <UCard class="mb-6" variant="subtle">
      <div class="flex gap-3">
        <UIcon name="i-lucide-info" class="w-5 h-5 text-primary shrink-0 mt-0.5" />
        <div class="text-sm text-muted">
          <p>Definiere hier die regulären Ausdrücke (Regex), mit denen Informationen aus Alarm-E-Mails extrahiert werden. Verwende <strong>benannte Capture-Gruppen</strong> wie <code class="bg-default px-1 py-0.5 rounded text-xs">(?&lt;value&gt;...)</code>.</p>
        </div>
      </div>
    </UCard>

    <!-- Global test input -->
    <UCard class="mb-6">
      <template #header>
        <div class="flex items-center gap-2">
          <UIcon name="i-lucide-flask-conical" class="w-4 h-4 text-primary" />
          <h2 class="font-semibold">Regex-Tester</h2>
        </div>
      </template>
      <div>
        <label class="block text-sm font-medium mb-1">Beispiel-Alarmtext</label>
        <UTextarea
          v-model="testInput"
          placeholder="Füge hier einen Beispiel-Alarmtext ein, um die Muster zu testen..."
          :rows="6"
          class="w-full"
        />
      </div>
    </UCard>

    <div v-if="loading" class="flex items-center justify-center py-20">
      <UIcon name="i-lucide-loader-2" class="w-8 h-8 animate-spin text-primary" />
    </div>

    <div v-else class="space-y-6">
      <UCard>
        <template #header>
          <h2 class="font-semibold">Standard-Muster</h2>
        </template>
        <div class="space-y-5">
          <div v-for="field in patternFields" :key="field.key">
            <div class="flex items-center justify-between mb-1">
              <label class="block text-sm font-medium">{{ field.label }}</label>
              <UButton
                icon="i-lucide-flask-conical"
                variant="ghost"
                size="xs"
                :color="testingField === field.key ? 'primary' : 'neutral'"
                title="Muster testen"
                @click="toggleTest(field.key)"
              />
            </div>
            <p class="text-xs text-muted mb-1">{{ field.hint }}</p>
            <UTextarea
              v-model="(form[field.key] as string)"
              placeholder="Regex-Muster eingeben"
              :color="!isValidRegex(form[field.key] as string) ? 'error' : undefined"
              :rows="2"
              class="w-full font-mono"
            />
            <p v-if="!isValidRegex(form[field.key] as string)" class="text-xs text-error mt-1">
              Ungültiger regulärer Ausdruck
            </p>

            <!-- Inline test result -->
            <div v-if="testingField === field.key && testInput" class="mt-2 p-3 rounded-lg bg-elevated border border-default">
              <div v-if="!form[field.key]" class="text-xs text-muted">Kein Muster eingetragen.</div>
              <div v-else-if="!isValidRegex(form[field.key] as string)" class="text-xs text-error">Das Muster ist ungültig.</div>
              <div v-else>
                <div class="flex items-center gap-2 mb-1">
                  <div :class="['w-2 h-2 rounded-full', getMatch(form[field.key] as string).match ? 'bg-green-500' : 'bg-red-500']" />
                  <span class="text-xs font-medium">
                    {{ getMatch(form[field.key] as string).match ? 'Treffer gefunden' : 'Kein Treffer' }}
                  </span>
                </div>
                <div v-if="getMatch(form[field.key] as string).groups.length > 0" class="mt-1">
                  <span class="text-xs text-muted">Capture-Gruppen:</span>
                  <div class="flex flex-wrap gap-1 mt-1">
                    <UBadge
                      v-for="(group, gi) in getMatch(form[field.key] as string).groups"
                      :key="gi"
                      variant="subtle"
                      color="success"
                      size="sm"
                    >
                      {{ group }}
                    </UBadge>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </UCard>

      <!-- Additional Properties -->
      <UCard>
        <template #header>
          <div class="flex items-center justify-between">
            <h2 class="font-semibold">Zusätzliche Eigenschaften</h2>
            <UButton
              label="Hinzufügen"
              icon="i-lucide-plus"
              variant="outline"
              size="sm"
              @click="addProperty"
            />
          </div>
        </template>

        <div v-if="form.additionalProperties.length === 0" class="text-sm text-muted py-4 text-center">
          Keine zusätzlichen Eigenschaften konfiguriert
        </div>

        <div v-else class="space-y-3">
          <div
            v-for="(prop, index) in form.additionalProperties"
            :key="index"
            class="border border-default rounded-lg p-3"
          >
            <div class="flex items-start gap-2">
              <div class="flex-1 grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div>
                  <label class="block text-xs font-medium mb-1">Name</label>
                  <UInput v-model="prop.name" placeholder="Eigenschaftsname" size="sm" />
                </div>
                <div>
                  <label class="block text-xs font-medium mb-1">Muster (Regex)</label>
                  <UTextarea
                    v-model="prop.pattern"
                    placeholder="Regex-Muster"
                    size="sm"
                    :color="!isValidRegex(prop.pattern) ? 'error' : undefined"
                    :rows="2"
                    class="w-full font-mono"
                  />
                  <p v-if="!isValidRegex(prop.pattern)" class="text-xs text-error mt-1">
                    Ungültiger regulärer Ausdruck
                  </p>
                </div>
              </div>
              <UButton
                icon="i-lucide-x"
                variant="ghost"
                color="error"
                size="xs"
                class="mt-5"
                @click="removeProperty(index)"
              />
            </div>
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
