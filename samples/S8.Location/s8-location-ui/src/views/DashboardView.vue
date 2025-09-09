<template>
  <div class="min-h-screen bg-gray-50">
    <!-- Header -->
    <header class="bg-white shadow-sm border-b border-gray-200">
      <div class="mx-auto px-6 py-4">
        <div class="flex items-center justify-between">
          <div>
            <h1 class="text-2xl font-bold text-gray-900">S8 Location Dashboard</h1>
            <p class="text-sm text-gray-600 mt-1">Real-time location processing and AI standardization</p>
          </div>
          <div class="flex items-center space-x-4">
            <div class="text-sm text-gray-500">
              Connected to: <span class="font-mono text-primary-600">{{ apiUrl }}</span>
            </div>
            <button 
              @click="refreshAll"
              class="btn-primary flex items-center space-x-2"
              :disabled="isRefreshing"
            >
              <RefreshCwIcon class="w-4 h-4" :class="{ 'animate-spin': isRefreshing }" />
              <span>Refresh All</span>
            </button>
          </div>
        </div>
      </div>
    </header>

    <!-- Main Content -->
    <main class="mx-auto px-6 py-8">
      <div class="grid grid-cols-1 xl:grid-cols-3 gap-8">
        <!-- Left Column - Live Stream & Testing -->
        <div class="xl:col-span-2 space-y-8">
          <!-- Live Location Stream -->
          <LocationStream ref="locationStreamRef" />
          
          <!-- AI Address Tester -->
          <AddressTester />
        </div>

        <!-- Right Column - System Info -->
        <div class="space-y-8">
          <!-- System Health -->
          <SystemHealth ref="systemHealthRef" />
          
          <!-- Quick Stats -->
          <QuickStats />
          
          <!-- Recent Activity -->
          <RecentActivity />
        </div>
      </div>
    </main>

    <!-- Footer -->
    <footer class="bg-white border-t border-gray-200 mt-16">
      <div class="mx-auto px-6 py-6">
        <div class="flex items-center justify-between text-sm text-gray-600">
          <div>
            <span>S8 Location Processing System</span>
            <span class="mx-2">•</span>
            <span>Built with Sora Framework</span>
          </div>
          <div class="flex items-center space-x-4">
            <span>Version 1.0.0</span>
            <a 
              href="http://localhost:4914/swagger" 
              target="_blank"
              class="text-primary-600 hover:text-primary-700 flex items-center"
            >
              API Docs
              <ExternalLinkIcon class="w-3 h-3 ml-1" />
            </a>
          </div>
        </div>
      </div>
    </footer>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { RefreshCw as RefreshCwIcon, ExternalLink as ExternalLinkIcon } from 'lucide-vue-next'
import LocationStream from '@/components/LocationStream.vue'
import AddressTester from '@/components/AddressTester.vue'
import SystemHealth from '@/components/SystemHealth.vue'
import QuickStats from '@/components/QuickStats.vue'
import RecentActivity from '@/components/RecentActivity.vue'

const locationStreamRef = ref<InstanceType<typeof LocationStream> | null>(null)
const systemHealthRef = ref<InstanceType<typeof SystemHealth> | null>(null)
const isRefreshing = ref(false)

const apiUrl = computed(() => {
  return import.meta.env.VITE_API_URL || 'localhost:4914'
})

const refreshAll = async () => {
  isRefreshing.value = true
  
  try {
    // Trigger refresh on child components
    await Promise.all([
      systemHealthRef.value?.refresh(),
      // Add other component refresh methods as needed
    ])
  } catch (error) {
    console.error('Failed to refresh dashboard:', error)
  } finally {
    isRefreshing.value = false
  }
}

onMounted(() => {
  console.log('🚀 S8 Location Dashboard initialized')
})
</script>