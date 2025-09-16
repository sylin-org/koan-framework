<template>
  <div class="card">
    <div class="card-header">
      <div class="flex items-center justify-between">
        <h2 class="text-lg font-semibold text-gray-900">System Health</h2>
        <div class="flex items-center space-x-2">
          <div class="flex items-center text-sm">
            <div 
              class="w-3 h-3 rounded-full mr-2"
              :class="getOverallStatusClass()"
            ></div>
            <span :class="getOverallStatusTextClass()">{{ overallStatus }}</span>
          </div>
          <button @click="refresh" class="btn-secondary text-sm" :disabled="isLoading">
            <RefreshCwIcon class="w-4 h-4" :class="{ 'animate-spin': isLoading }" />
          </button>
        </div>
      </div>
    </div>
    
    <div class="card-body">
      <div v-if="error" class="p-4 bg-red-50 border border-red-200 rounded-lg mb-4">
        <div class="flex items-center space-x-2">
          <AlertCircleIcon class="w-5 h-5 text-red-500" />
          <span class="text-red-700 font-medium">Health Check Failed</span>
        </div>
        <p class="text-red-600 text-sm mt-1">{{ error }}</p>
      </div>

      <div v-else class="space-y-4">
        <!-- Overall System Status -->
        <div class="p-4 rounded-lg" :class="getOverallStatusBg()">
          <div class="flex items-center justify-between">
            <div>
              <h3 class="font-medium" :class="getOverallStatusTextClass()">
                Overall System Status
              </h3>
              <p class="text-sm opacity-75">
                Last checked: {{ lastChecked }}
              </p>
            </div>
            <div class="text-right">
              <div class="text-2xl font-bold" :class="getOverallStatusTextClass()">
                {{ overallStatus }}
              </div>
            </div>
          </div>
        </div>

        <!-- Service Status Grid -->
        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          <div 
            v-for="service in services" 
            :key="service.name"
            class="p-4 border rounded-lg"
            :class="getServiceBorderClass(service.status)"
          >
            <div class="flex items-center justify-between mb-2">
              <h4 class="font-medium text-gray-900">{{ service.name }}</h4>
              <span class="status-indicator" :class="getServiceStatusClass(service.status)">
                {{ service.status }}
              </span>
            </div>
            
            <div class="flex items-center justify-between text-sm text-gray-600">
              <span>Response Time</span>
              <span class="font-mono">
                {{ service.responseTime ? `${service.responseTime}ms` : 'N/A' }}
              </span>
            </div>
            
            <!-- Service-specific indicators -->
            <div class="mt-3">
              <ServiceIndicator :service="service" />
            </div>
          </div>
        </div>

        <!-- Quick Actions -->
        <div class="pt-4 border-t border-gray-200">
          <h3 class="text-sm font-medium text-gray-700 mb-3">Quick Actions</h3>
          <div class="flex flex-wrap gap-2">
            <button 
              @click="testConnection"
              class="btn-secondary text-sm"
              :disabled="isLoading"
            >
              <WifiIcon class="w-4 h-4 mr-1" />
              Test API Connection
            </button>
            <button 
              @click="viewLogs"
              class="btn-secondary text-sm"
            >
              <FileTextIcon class="w-4 h-4 mr-1" />
              View Logs
            </button>
            <button 
              @click="checkDatabase"
              class="btn-secondary text-sm"
              :disabled="isLoading"
            >
              <DatabaseIcon class="w-4 h-4 mr-1" />
              Check Database
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { locationApi, type SystemHealth } from '@/services/api'
import { 
  RefreshCw as RefreshCwIcon,
  AlertCircle as AlertCircleIcon,
  Wifi as WifiIcon,
  FileText as FileTextIcon,
  Database as DatabaseIcon
} from 'lucide-vue-next'
import ServiceIndicator from './ServiceIndicator.vue'

const healthData = ref<SystemHealth | null>(null)
const isLoading = ref(false)
const error = ref('')

const overallStatus = computed(() => {
  if (!healthData.value) return 'Unknown'
  switch (healthData.value.status) {
    case 'ok': return 'Healthy'
    case 'degraded': return 'Degraded'
    case 'unhealthy': return 'Unhealthy'
    default: return 'Unknown'
  }
})

const services = computed(() => {
  if (!healthData.value) return []
  return healthData.value.services || []
})

const lastChecked = computed(() => {
  if (!healthData.value) return 'Never'
  return new Date(healthData.value.timestamp).toLocaleTimeString()
})

const getOverallStatusClass = () => {
  switch (healthData.value?.status) {
    case 'ok': return 'bg-green-400'
    case 'degraded': return 'bg-yellow-400'
    case 'unhealthy': return 'bg-red-400'
    default: return 'bg-gray-400'
  }
}

const getOverallStatusTextClass = () => {
  switch (healthData.value?.status) {
    case 'ok': return 'text-green-700'
    case 'degraded': return 'text-yellow-700'
    case 'unhealthy': return 'text-red-700'
    default: return 'text-gray-700'
  }
}

const getOverallStatusBg = () => {
  switch (healthData.value?.status) {
    case 'ok': return 'bg-green-50 border border-green-200'
    case 'degraded': return 'bg-yellow-50 border border-yellow-200'
    case 'unhealthy': return 'bg-red-50 border border-red-200'
    default: return 'bg-gray-50 border border-gray-200'
  }
}

const getServiceStatusClass = (status: string) => {
  switch (status) {
    case 'healthy': return 'status-success'
    case 'unhealthy': return 'status-error'
    default: return 'status-warning'
  }
}

const getServiceBorderClass = (status: string) => {
  switch (status) {
    case 'healthy': return 'border-green-200 bg-green-50/50'
    case 'unhealthy': return 'border-red-200 bg-red-50/50'
    default: return 'border-gray-200'
  }
}

const refresh = async () => {
  isLoading.value = true
  error.value = ''
  
  try {
    const health = await locationApi.getHealth()
    healthData.value = health
  } catch (err: any) {
    error.value = 'Failed to fetch system health. API may be unavailable.'
    console.error('Health check failed:', err)
  } finally {
    isLoading.value = false
  }
}

const testConnection = async () => {
  isLoading.value = true
  try {
    await locationApi.getHealth()
    alert('✅ API connection successful!')
  } catch (err) {
    alert('❌ API connection failed!')
  } finally {
    isLoading.value = false
  }
}

const viewLogs = () => {
  // In a real implementation, this would open a logs viewer
  alert('📄 Logs viewer would open here')
}

const checkDatabase = async () => {
  isLoading.value = true
  try {
    // Simple database check by trying to fetch locations
    await locationApi.getLocations({ limit: 1 })
    alert('✅ Database connection successful!')
  } catch (err) {
    alert('❌ Database connection failed!')
  } finally {
    isLoading.value = false
  }
}

onMounted(() => {
  refresh()
  // Auto-refresh every 30 seconds
  setInterval(refresh, 30000)
})
</script>