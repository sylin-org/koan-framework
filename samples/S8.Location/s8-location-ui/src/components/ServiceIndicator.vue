<template>
  <div class="space-y-2">
    <!-- Service-specific metrics -->
    <div v-if="service.name.toLowerCase().includes('mongo')" class="text-xs">
      <div class="flex items-center justify-between">
        <span class="text-gray-600">Database</span>
        <DatabaseIcon class="w-4 h-4 text-green-500" v-if="service.status === 'healthy'" />
        <DatabaseIcon class="w-4 h-4 text-red-500" v-else />
      </div>
    </div>
    
    <div v-else-if="service.name.toLowerCase().includes('rabbit')" class="text-xs">
      <div class="flex items-center justify-between">
        <span class="text-gray-600">Message Queue</span>
        <MessageSquareIcon class="w-4 h-4 text-green-500" v-if="service.status === 'healthy'" />
        <MessageSquareIcon class="w-4 h-4 text-red-500" v-else />
      </div>
    </div>
    
    <div v-else-if="service.name.toLowerCase().includes('ollama')" class="text-xs">
      <div class="flex items-center justify-between">
        <span class="text-gray-600">AI Engine</span>
        <BrainIcon class="w-4 h-4 text-green-500" v-if="service.status === 'healthy'" />
        <BrainIcon class="w-4 h-4 text-red-500" v-else />
      </div>
    </div>
    
    <div v-else class="text-xs">
      <div class="flex items-center justify-between">
        <span class="text-gray-600">Service</span>
        <ServerIcon class="w-4 h-4 text-green-500" v-if="service.status === 'healthy'" />
        <ServerIcon class="w-4 h-4 text-red-500" v-else />
      </div>
    </div>
    
    <!-- Response time indicator -->
    <div v-if="service.responseTime" class="flex items-center space-x-1">
      <div 
        class="flex-1 h-1 rounded-full"
        :class="getResponseTimeClass(service.responseTime)"
      ></div>
      <span class="text-xs text-gray-500">{{ getResponseTimeLabel(service.responseTime) }}</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { 
  Database as DatabaseIcon,
  MessageSquare as MessageSquareIcon,
  Brain as BrainIcon,
  Server as ServerIcon
} from 'lucide-vue-next'

interface Props {
  service: {
    name: string
    status: string
    responseTime?: number
  }
}

const props = defineProps<Props>()

const getResponseTimeClass = (responseTime: number) => {
  if (responseTime < 100) return 'bg-green-400'
  if (responseTime < 500) return 'bg-yellow-400'
  return 'bg-red-400'
}

const getResponseTimeLabel = (responseTime: number) => {
  if (responseTime < 100) return 'Fast'
  if (responseTime < 500) return 'Good'
  return 'Slow'
}
</script>