<template>
  <div class="card">
    <div class="card-header">
      <h2 class="text-lg font-semibold text-gray-900">AI Address Tester</h2>
      <p class="text-sm text-gray-600 mt-1">Test address standardization and AI correction in real-time</p>
    </div>
    
    <div class="card-body">
      <div class="space-y-6">
        <!-- Input Section -->
        <div>
          <label for="address-input" class="block text-sm font-medium text-gray-700 mb-2">
            Enter Address to Test
          </label>
          <div class="flex space-x-3">
            <input
              id="address-input"
              v-model="testAddress"
              type="text"
              placeholder="e.g. 123 main st, new york ny"
              class="flex-1 px-3 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent"
              @keyup.enter="testAddress && resolveAddress()"
            />
            <button
              @click="resolveAddress"
              :disabled="!testAddress || isLoading"
              class="btn-primary disabled:opacity-50 disabled:cursor-not-allowed flex items-center space-x-2"
            >
              <LoaderIcon v-if="isLoading" class="w-4 h-4 animate-spin" />
              <SearchIcon v-else class="w-4 h-4" />
              <span>{{ isLoading ? 'Processing...' : 'Test' }}</span>
            </button>
          </div>
        </div>

        <!-- Quick Test Examples -->
        <div>
          <p class="text-sm font-medium text-gray-700 mb-2">Quick Examples:</p>
          <div class="flex flex-wrap gap-2">
            <button
              v-for="example in examples"
              :key="example"
              @click="testAddress = example"
              class="px-3 py-1 text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 rounded-full transition-colors duration-200"
            >
              {{ example }}
            </button>
          </div>
        </div>

        <!-- Results Section -->
        <div v-if="result || error" class="space-y-4">
          <div v-if="error" class="p-4 bg-red-50 border border-red-200 rounded-lg">
            <div class="flex items-center space-x-2">
              <AlertCircleIcon class="w-5 h-5 text-red-500" />
              <span class="text-red-700 font-medium">Error</span>
            </div>
            <p class="text-red-600 text-sm mt-1">{{ error }}</p>
          </div>

          <div v-if="result" class="space-y-4">
            <!-- Comparison Display -->
            <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
              <!-- Original Address -->
              <div class="p-4 bg-gray-50 border border-gray-200 rounded-lg">
                <h3 class="text-sm font-medium text-gray-700 mb-2 flex items-center">
                  <FileTextIcon class="w-4 h-4 mr-2" />
                  Original Address
                </h3>
                <p class="font-mono text-sm text-gray-800 break-words">{{ result.original }}</p>
              </div>

              <!-- AI Corrected Address -->
              <div class="p-4 bg-primary-50 border border-primary-200 rounded-lg">
                <h3 class="text-sm font-medium text-primary-700 mb-2 flex items-center">
                  <BrainIcon class="w-4 h-4 mr-2" />
                  AI Corrected Address
                </h3>
                <p class="font-mono text-sm text-primary-800 font-medium break-words">{{ result.corrected }}</p>
                
                <!-- Confidence Score -->
                <div class="mt-3">
                  <div class="flex items-center justify-between text-xs text-primary-600 mb-1">
                    <span>Confidence Score</span>
                    <span>{{ Math.round(result.confidence * 100) }}%</span>
                  </div>
                  <div class="w-full bg-primary-100 rounded-full h-2">
                    <div 
                      class="bg-primary-500 h-2 rounded-full transition-all duration-500"
                      :style="{ width: `${result.confidence * 100}%` }"
                    ></div>
                  </div>
                </div>
              </div>
            </div>

            <!-- Changes Highlighted -->
            <div v-if="hasChanges" class="p-4 bg-yellow-50 border border-yellow-200 rounded-lg">
              <h3 class="text-sm font-medium text-yellow-800 mb-2 flex items-center">
                <EditIcon class="w-4 h-4 mr-2" />
                Changes Made by AI
              </h3>
              <div class="text-sm">
                <DiffViewer :original="result.original" :corrected="result.corrected" />
              </div>
            </div>

            <!-- Canonical ID -->
            <div class="p-4 bg-green-50 border border-green-200 rounded-lg">
              <h3 class="text-sm font-medium text-green-700 mb-2 flex items-center">
                <HashIcon class="w-4 h-4 mr-2" />
                Canonical Location ID
              </h3>
              <p class="font-mono text-xs text-green-600 break-all">{{ result.canonicalId }}</p>
              <p class="text-xs text-green-600 mt-1">This ID is used for deduplication across all sources</p>
            </div>

            <!-- Performance Metrics -->
            <div class="grid grid-cols-3 gap-4 text-center">
              <div>
                <div class="text-lg font-semibold text-gray-900">{{ processingTime }}ms</div>
                <div class="text-xs text-gray-500">Processing Time</div>
              </div>
              <div>
                <div class="text-lg font-semibold text-primary-600">{{ hasChanges ? 'Yes' : 'No' }}</div>
                <div class="text-xs text-gray-500">AI Modified</div>
              </div>
              <div>
                <div class="text-lg font-semibold text-success-600">{{ result.canonicalId ? 'Found' : 'New' }}</div>
                <div class="text-xs text-gray-500">Canonical Match</div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { locationApi } from '@/services/api'
import { 
  Search as SearchIcon, 
  Loader as LoaderIcon,
  AlertCircle as AlertCircleIcon,
  FileText as FileTextIcon,
  Brain as BrainIcon,
  Edit as EditIcon,
  Hash as HashIcon
} from 'lucide-vue-next'
import DiffViewer from './DiffViewer.vue'

const testAddress = ref('')
const isLoading = ref(false)
const result = ref<{
  original: string
  corrected: string
  canonicalId: string
  confidence: number
} | null>(null)
const error = ref('')
const processingTime = ref(0)

const examples = [
  '123 main st',
  '456 elm street, apt 2b',
  '789 oak ave, suite 100',
  '321 pine rd, new york, ny',
  '555 broadway avenue',
]

const hasChanges = computed(() => {
  return result.value && result.value.original !== result.value.corrected
})

const resolveAddress = async () => {
  if (!testAddress.value.trim()) return

  isLoading.value = true
  error.value = ''
  result.value = null
  
  const startTime = Date.now()

  try {
    const response = await locationApi.resolveAddress(testAddress.value.trim())
    processingTime.value = Date.now() - startTime
    result.value = response
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Failed to resolve address. Make sure the S8.Location API is running.'
    processingTime.value = Date.now() - startTime
  } finally {
    isLoading.value = false
  }
}
</script>